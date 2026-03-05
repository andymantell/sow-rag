# SoW Improver — CLAUDE.md

> AI context file. For human-readable documentation see `README.md`.

## What This Is
ASP.NET Core 8 / Blazor Server app for RAG-based SoW document improvement.
Corpus PDFs → embedding cache → `EmbeddingRetriever` → per-section LLM rewrite.
See README for architecture overview, prerequisites, and configuration.

## Project Root
`C:\goaco\AI\SOW-RAG\SoWImprover\`

## Key Files
```
Services/
  DefinitionGeneratorService.cs  # BackgroundService — startup orchestrator
  FoundryClientFactory.cs        # Chat + embedding clients (local/cloud)
  EmbeddingService.cs            # Wraps EmbeddingClient; sequential calls only
  EmbeddingRetriever.cs          # Cosine similarity retrieval + section matching
  DefinitionBuilder.cs           # 2-pass LLM: per-doc analysis → per-section synthesis
  SoWImproverService.cs          # Per-section RAG improvement pipeline
  DocumentLoader.cs              # Python subprocess PDF extraction + chunking
Models/
  GoodDefinition.cs              # Singleton: sections, EmbeddingRetriever, volatile IsReady
Components/Pages/Home.razor      # Main layout
Components/Shared/               # DefinitionSidebar, UploadPanel, ResultsPanel
sample-sows/embeddings-cache.json  # Auto-generated; delete to force recompute
```

## Critical Don'ts

- **Never use `FoundryLocalManager.StartWebServiceAsync()`** — it starts an in-process Foundry service that cannot find models downloaded by the CLI. Always connect via `foundry service status` subprocess.
- **Never send multiple texts in one `GenerateEmbeddingsAsync` call to Ollama** — Ollama concatenates them and exceeds context limits. `EmbedBatchAsync` loops sequentially.
- **`OpenAIClientOptions.Endpoint` must include `/v1`** — the SDK appends `chat/completions` (not `/v1/chat/completions`), so the endpoint must be `http://host:PORT/v1`.

## Key Implementation Notes

**FoundryClientFactory threading:** Three semaphores — `_lock` (chat), `_embeddingLock` (embedding), `_rootLock` (Foundry service URL). Service URL cached in `_cachedServiceRoot` so `foundry service status` subprocess runs once.

**Model ID resolution (chat only):** Config alias (e.g. `phi-4`) resolved to actual loaded model ID (e.g. `Phi-4-trtrtx-gpu:1`) by querying `/v1/models`. Ollama embedding model name is passed directly — no resolution needed.

**Embedding cache:** `{corpus}/embeddings-cache.json`. Valid when SHA256 fingerprint of `{filename}|{size}` for all PDFs and model name both match. Cache entries use `GlobalIndex` (not per-document `ChunkIndex`) for correct ordering across multiple documents.

**Chunking:** `text.Split([' ', '\n', '\r', '\t'], ...)` — must split on all whitespace. Splitting on space only causes markdown table rows to become single oversized tokens that exceed Ollama's context limit. `EmbedAsync` also truncates at 8000 chars as a safety net.

**Canonical sections:** Defined in both `DefinitionBuilder.CanonicalSections` and `DefinitionGeneratorService.CanonicalSections` — must be kept in sync (11 sections).

**GoodDefinition threading:** `IsReady` is `volatile bool` — acts as release fence. `Retriever` and `Sections` are safe to read after `IsReady == true`.

**Per-section LLM prompts:** Use `$$"""..."""` raw strings (double-dollar) to allow literal `{` in JSON examples alongside `{{interpolations}}`.

**`RuntimeIdentifier = win-x64`** required in `.csproj` (Microsoft.AI.Foundry.Local NuGet constraint, even though FoundryLocalManager is unused at runtime).

## Current Task
Embedding-based RAG implementation complete. Smoke test in progress — last run had chunker whitespace-split fix applied. To continue:
1. `del SoWImprover\sample-sows\embeddings-cache.json`
2. `cd SoWImprover && dotnet run`
3. Verify startup completes (embeddings computed, definition generated, app ready)
4. Test `/api/improve` with a real SoW upload via the browser UI
