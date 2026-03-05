# Embedding-Based RAG Design

**Goal:** Replace TF-IDF retrieval with semantic embedding-based retrieval and replace the LLM section-matching call with embedding cosine similarity, laying the groundwork for corpus growth.

**Architecture:** A new `EmbeddingService` wraps Foundry Local's `/v1/embeddings` endpoint via the OpenAI SDK's `EmbeddingClient`. `EmbeddingRetriever` replaces `SimpleRetriever` with the same interface. Chunk embeddings are computed at startup and persisted to a cache file in the corpus folder; the cache is invalidated automatically when the corpus changes. Section matching (uploaded title → canonical name) uses cosine similarity over pre-embedded canonical section names instead of an LLM call.

**Tech Stack:** OpenAI .NET SDK v2.x `EmbeddingClient`, nomic-embed-text via Foundry Local `/v1/embeddings`, System.Text.Json for cache serialisation, System.Security.Cryptography for corpus fingerprinting.

---

## Components

### EmbeddingService
- New file: `Services/EmbeddingService.cs`
- `FoundryClientFactory` gets a second method `GetEmbeddingClientAsync()` returning `EmbeddingClient` using `OpenAIClient.GetEmbeddingClient(embeddingModelName)`
- `EmbeddingService` exposes:
  - `EmbedAsync(string text, CancellationToken ct) → float[]`
  - `EmbedBatchAsync(IEnumerable<string> texts, CancellationToken ct) → float[][]`
- Batch call sends all inputs in one API request (Foundry Local `/v1/embeddings` accepts array input)

### EmbeddingRetriever
- New file: `Services/EmbeddingRetriever.cs`
- Replaces `SimpleRetriever` — identical public interface: `Retrieve(string query)`, `ChunkCount`, `DocumentCount`
- Constructed from `List<DocumentChunk>` + corresponding `float[][]` vectors
- `Retrieve`: embeds query via `EmbeddingService.EmbedAsync`, computes cosine similarity against all stored vectors, returns top-k chunks by score
- Also holds pre-embedded canonical section names: `Dictionary<string, float[]>` (11 entries)
- Exposes `MatchSection(string uploadedTitle, float threshold) → string?` — embeds title, returns best canonical match above threshold or null

### Embedding Cache
- Cache file: `{KnownGoodFolder}/embeddings-cache.json`
- **Corpus fingerprint:** SHA256 of sorted `filename|size` strings for all PDFs in the folder
- Cache JSON structure:
  ```json
  {
    "fingerprint": "<sha256hex>",
    "model": "nomic-embed-text",
    "entries": [
      { "sourceFile": "sow1.pdf", "chunkIndex": 0, "vector": [0.1, ...] }
    ]
  }
  ```
- Cache is valid if `fingerprint` and `model` both match current values
- On cache miss: embed all chunks in batch, write cache, continue
- On cache hit: deserialise vectors, skip embedding API calls

### FoundryClientFactory changes
- Add `GetEmbeddingClientAsync(CancellationToken ct) → Task<EmbeddingClient>`
- Reuses same service URL resolved by `foundry service status`
- Uses `Foundry:EmbeddingModelName` config key (no model ID resolution needed — nomic-embed-text is not a chat model so `/v1/models` list format may differ; use config value directly)

### SoWImproverService changes
- Remove `MatchSectionsAsync` (LLM call)
- New `MatchSections(IReadOnlyList<DocumentSection> sections, EmbeddingRetriever retriever) → Dictionary<string, string?>`
  - For each section title: call `retriever.MatchSection(title, threshold)`
  - `threshold` from `Docs:MatchThreshold` config (default 0.6)
- `EmbeddingRetriever` injected (replaces `SimpleRetriever`)

### DefinitionGeneratorService changes
- At startup, after loading chunks:
  1. Compute corpus fingerprint
  2. Attempt cache load from `{KnownGoodFolder}/embeddings-cache.json`
  3. If cache miss/invalid: call `EmbeddingService.EmbedBatchAsync` for all chunk texts, save cache
  4. Embed 11 canonical section names (always in-memory, not cached — trivially fast)
  5. Construct `EmbeddingRetriever` with chunks + vectors + canonical embeddings
  6. Register as singleton (replaces `SimpleRetriever` singleton)

### Program.cs changes
- Register `EmbeddingService` as singleton
- Remove `SimpleRetriever` registration
- `EmbeddingRetriever` constructed and registered by `DefinitionGeneratorService` (same pattern as current `SimpleRetriever`)

---

## Config Changes

`appsettings.json` additions:
```json
"Foundry": {
  "EmbeddingModelName": "nomic-embed-text"
},
"Docs": {
  "MatchThreshold": 0.6
}
```

---

## Files Changed

| File | Action |
|---|---|
| `Services/EmbeddingService.cs` | Create |
| `Services/EmbeddingRetriever.cs` | Create |
| `Services/SimpleRetriever.cs` | Delete |
| `Services/FoundryClientFactory.cs` | Add `GetEmbeddingClientAsync()` |
| `Services/DefinitionGeneratorService.cs` | Add cache logic, construct `EmbeddingRetriever` |
| `Services/SoWImproverService.cs` | Replace LLM match with `retriever.MatchSection()` |
| `Program.cs` | Swap registrations |
| `appsettings.json` | Two new keys |

---

## What Does Not Change

- `DefinitionBuilder` — definition of good generation is unchanged
- `ImproveSectionAsync` prompt — still uses definition + retrieved chunks
- All Blazor components (`ResultsPanel`, `UploadPanel`, `DefinitionSidebar`, `Home.razor`)
- CSS, models, `DocumentLoader`

---

## Design Decisions

- **Cache location in corpus folder** — co-located with the PDFs it represents; makes it obvious and easy to delete/reset
- **Model name in cache** — ensures cache is invalidated if embedding model changes
- **Canonical section names not cached** — 11 short strings, trivial to re-embed on every startup
- **Threshold as config** — 0.6 is a reasonable starting point for nomic-embed-text on short phrases; can be tuned without code changes
- **No model ID resolution for embedding model** — nomic-embed-text is passed directly; if Foundry Local requires a resolved ID, the factory can be extended later
- **Batch embedding at startup** — one API call for all chunks rather than one-per-chunk; keeps startup time acceptable as corpus grows
