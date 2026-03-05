# Embedding-Based RAG Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace TF-IDF retrieval with semantic embedding-based retrieval (nomic-embed-text via Foundry Local) and replace the LLM section-matching call with embedding cosine similarity, with a startup cache so embeddings survive restarts.

**Architecture:** A new `EmbeddingService` wraps Foundry Local's `/v1/embeddings` endpoint. `EmbeddingRetriever` replaces `SimpleRetriever` with an async interface; it also handles section-title matching via cosine similarity. Chunk vectors are cached to `{corpus-folder}/embeddings-cache.json` and reloaded on startup unless the corpus has changed. `GoodDefinition` holds the fully-initialised retriever so it's available to `SoWImproverService` without an extra DI registration.

**Tech Stack:** ASP.NET Core 8, Blazor Server, OpenAI .NET SDK v2.9.1 (`EmbeddingClient`), nomic-embed-text, System.Text.Json, System.Security.Cryptography.SHA256.

**Pre-requisite:** Download the embedding model before running:
```
foundry model download nomic-embed-text
```

---

### Task 1: Add config keys and `GetEmbeddingClientAsync` to `FoundryClientFactory`

**Files:**
- Modify: `SoWImprover/appsettings.json`
- Modify: `SoWImprover/Services/FoundryClientFactory.cs`

**Step 1: Add the two new config keys to `appsettings.json`**

Open `appsettings.json`. Add `EmbeddingModelName` inside `Foundry` and `MatchThreshold` inside `Docs`:

```json
{
  "Foundry": {
    "UseLocal": true,
    "LocalModelName": "phi-4",
    "EmbeddingModelName": "nomic-embed-text",
    "CloudEndpoint": "",
    "CloudApiKey": "",
    "CloudModelName": ""
  },
  "Docs": {
    "KnownGoodFolder": "./sample-sows",
    "ChunkSize": 500,
    "ChunkOverlap": 50,
    "TopKChunks": 5,
    "MatchThreshold": 0.6
  },
  ...
}
```

**Step 2: Add `GetEmbeddingClientAsync` to `FoundryClientFactory.cs`**

Read `Services/FoundryClientFactory.cs` first. It uses `_lock` (a `SemaphoreSlim`) and `_cached` for the chat client. Add a second cached field and method following the exact same pattern.

Add the using at the top (after the existing `using OpenAI.Chat;`):
```csharp
using OpenAI.Embeddings;
```

Add two new members to the class (after the existing `_cached` field):
```csharp
private EmbeddingClient? _cachedEmbedding;
```

Add the new method after `GetChatClientAsync`:
```csharp
/// <summary>
/// Returns an <see cref="EmbeddingClient"/> pointed at the local Foundry endpoint.
/// Only supported when <c>Foundry:UseLocal</c> is true.
/// </summary>
public async Task<EmbeddingClient> GetEmbeddingClientAsync(CancellationToken ct = default)
{
    if (_cachedEmbedding is not null) return _cachedEmbedding;

    await _lock.WaitAsync(ct);
    try
    {
        if (_cachedEmbedding is not null) return _cachedEmbedding;

        if (!config.GetValue<bool>("Foundry:UseLocal"))
            throw new InvalidOperationException(
                "Embedding client is only supported in local mode (Foundry:UseLocal = true).");

        var modelName = config["Foundry:EmbeddingModelName"] ?? "nomic-embed-text";
        var serviceRoot = await DiscoverFoundryEndpointAsync(ct);
        var sdkEndpoint = serviceRoot + "/v1";

        logger.LogInformation("Embedding client — endpoint: {Url}, model: {Model}",
            sdkEndpoint, modelName);

        var openAiClient = new OpenAIClient(
            new ApiKeyCredential("OPENAI_API_KEY"),
            new OpenAIClientOptions { Endpoint = new Uri(sdkEndpoint) });

        _cachedEmbedding = openAiClient.GetEmbeddingClient(modelName);
        return _cachedEmbedding;
    }
    finally
    {
        _lock.Release();
    }
}
```

**Step 3: Verify the project still builds**

```
cd C:\goaco\AI\SOW-RAG\SoWImprover
dotnet build
```
Expected: Build succeeded, 0 errors.

---

### Task 2: Create `EmbeddingService`

**Files:**
- Create: `SoWImprover/Services/EmbeddingService.cs`

**Step 1: Create the file**

```csharp
using OpenAI.Embeddings;

namespace SoWImprover.Services;

/// <summary>
/// Wraps the Foundry Local embeddings endpoint, providing single and batch embedding calls.
/// </summary>
public class EmbeddingService(FoundryClientFactory factory)
{
    private EmbeddingClient? _client;

    private async Task<EmbeddingClient> GetClientAsync(CancellationToken ct)
    {
        _client ??= await factory.GetEmbeddingClientAsync(ct);
        return _client;
    }

    /// <summary>Returns the embedding vector for a single text input.</summary>
    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var client = await GetClientAsync(ct);
        var result = await client.GenerateEmbeddingAsync(text, cancellationToken: ct);
        return result.Value.ToFloats().ToArray();
    }

    /// <summary>
    /// Returns embedding vectors for multiple inputs in a single API call.
    /// Results are in the same order as <paramref name="texts"/>.
    /// </summary>
    public async Task<float[][]> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (texts.Count == 0) return [];
        var client = await GetClientAsync(ct);
        var result = await client.GenerateEmbeddingsAsync(texts, cancellationToken: ct);
        return result.Value
            .OrderBy(e => e.Index)
            .Select(e => e.ToFloats().ToArray())
            .ToArray();
    }
}
```

**Step 2: Verify build**

```
dotnet build
```
Expected: Build succeeded, 0 errors.

---

### Task 3: Create `EmbeddingRetriever`

**Files:**
- Create: `SoWImprover/Services/EmbeddingRetriever.cs`

This class takes pre-computed chunk vectors and canonical section name vectors, and exposes async `RetrieveAsync` and `MatchSectionsAsync` methods. It does **not** call the embedding API itself at construction — vectors are passed in by `DefinitionGeneratorService`.

**Step 1: Create the file**

```csharp
using SoWImprover.Models;

namespace SoWImprover.Services;

/// <summary>
/// Semantic retriever using pre-computed embedding vectors.
/// Replaces <c>SimpleRetriever</c>; exposes the same chunk retrieval interface
/// plus section-title matching via cosine similarity.
/// </summary>
public class EmbeddingRetriever
{
    private readonly List<DocumentChunk> _chunks;
    private readonly float[][] _vectors;       // parallel array: _vectors[i] is the embedding for _chunks[i]
    private readonly Dictionary<string, float[]> _canonicalEmbeddings; // canonical section name → vector
    private readonly EmbeddingService _embeddingService;
    private readonly int _topK;

    /// <summary>Total number of corpus chunks.</summary>
    public int ChunkCount => _chunks.Count;

    /// <summary>Number of distinct source documents.</summary>
    public int DocumentCount { get; }

    public EmbeddingRetriever(
        List<DocumentChunk> chunks,
        float[][] vectors,
        Dictionary<string, float[]> canonicalEmbeddings,
        EmbeddingService embeddingService,
        int topK)
    {
        if (chunks.Count != vectors.Length)
            throw new ArgumentException("chunks and vectors must be the same length.");

        _chunks = chunks;
        _vectors = vectors;
        _canonicalEmbeddings = canonicalEmbeddings;
        _embeddingService = embeddingService;
        _topK = topK;
        DocumentCount = chunks.Select(c => c.SourceFile).Distinct().Count();
    }

    /// <summary>
    /// Returns the top-k corpus chunks most semantically similar to <paramref name="query"/>.
    /// </summary>
    public async Task<List<DocumentChunk>> RetrieveAsync(string query, CancellationToken ct = default)
    {
        var queryVec = await _embeddingService.EmbedAsync(query, ct);
        return _chunks
            .Select((c, i) => (chunk: c, score: CosineSimilarity(queryVec, _vectors[i])))
            .OrderByDescending(x => x.score)
            .Take(_topK)
            .Select(x => x.chunk)
            .ToList();
    }

    /// <summary>
    /// Maps each uploaded section title to the best-matching canonical section name
    /// (or null if no match exceeds <paramref name="threshold"/>).
    /// All titles are embedded in a single batch API call.
    /// </summary>
    public async Task<Dictionary<string, string?>> MatchSectionsAsync(
        IList<string> uploadedTitles,
        float threshold,
        CancellationToken ct = default)
    {
        var vectors = await _embeddingService.EmbedBatchAsync(
            uploadedTitles.ToArray(), ct);

        var result = new Dictionary<string, string?>(uploadedTitles.Count);
        for (var i = 0; i < uploadedTitles.Count; i++)
        {
            var best = _canonicalEmbeddings
                .Select(kv => (name: kv.Key, score: CosineSimilarity(vectors[i], kv.Value)))
                .OrderByDescending(x => x.score)
                .FirstOrDefault();

            result[uploadedTitles[i]] = best.score >= threshold ? best.name : null;
        }
        return result;
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        float dot = 0f, normA = 0f, normB = 0f;
        for (var i = 0; i < a.Length; i++)
        {
            dot  += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        return (normA == 0f || normB == 0f) ? 0f : dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }
}
```

**Step 2: Verify build**

```
dotnet build
```
Expected: Build succeeded, 0 errors.

---

### Task 4: Update `GoodDefinition` to hold the retriever

**Files:**
- Modify: `SoWImprover/Models/GoodDefinition.cs`

`GoodDefinition` is the app's "everything is ready" sentinel. Add the retriever to it so `SoWImproverService` can access it without a separate DI dependency.

**Step 1: Add the `Retriever` property and update `SetReady`**

Open `Models/GoodDefinition.cs`.

Add a using at the top (before `namespace`):
```csharp
using SoWImprover.Services;
```

Add a new property after `ChunkCount`:
```csharp
/// <summary>The semantic retriever, available once IsReady is true.</summary>
public EmbeddingRetriever? Retriever { get; private set; }
```

Replace the existing `SetReady` signature and body:
```csharp
/// <summary>
/// Sets all properties and marks the definition as ready.
/// IsReady is set last (volatile write) to act as a release fence.
/// </summary>
public void SetReady(
    IReadOnlyList<DefinedSection> sections,
    EmbeddingRetriever retriever,
    int documentCount,
    int chunkCount)
{
    Sections = sections;
    MarkdownContent = string.Join("\n\n", sections.Select(s => $"## {s.Name}\n\n{s.Content}"));
    Retriever = retriever;
    DocumentCount = documentCount;
    ChunkCount = chunkCount;
    _isReady = true;
}
```

**Step 2: Verify build**

```
dotnet build
```
Expected: Errors on `DefinitionGeneratorService.cs` (wrong `SetReady` call) — that's expected, fixed in Task 5.

---

### Task 5: Rewrite `DefinitionGeneratorService` with cache logic

**Files:**
- Modify: `SoWImprover/Services/DefinitionGeneratorService.cs`

This service now:
1. Loads chunks from the corpus folder
2. Computes or loads cached embeddings
3. Pre-embeds canonical section names (always in-memory)
4. Builds `EmbeddingRetriever`
5. Builds the definition of good
6. Calls `definition.SetReady()` with retriever

The constructor takes `EmbeddingService` instead of `SimpleRetriever`.

**Step 1: Replace the entire file**

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SoWImprover.Models;

namespace SoWImprover.Services;

public class DefinitionGeneratorService(
    DocumentLoader loader,
    DefinitionBuilder builder,
    EmbeddingService embeddingService,
    GoodDefinition definition,
    IConfiguration config,
    ILogger<DefinitionGeneratorService> logger) : BackgroundService
{
    // Canonical section names — must match DefinitionBuilder.CanonicalSections exactly.
    private static readonly IReadOnlyList<string> CanonicalSections =
    [
        "Background and Context",
        "Objectives",
        "Scope of Services",
        "Deliverables",
        "Milestones and Timeline",
        "Governance and Reporting",
        "Roles and Responsibilities",
        "Dependencies and Assumptions",
        "Acceptance Criteria",
        "Pricing and Payment",
        "Change Control",
    ];

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var folder = config["Docs:KnownGoodFolder"] ?? "./sample-sows";
        var topK = config.GetValue<int>("Docs:TopKChunks", 5);

        logger.LogInformation("Loading corpus from: {Folder}", folder);
        definition.SetProgress("Loading corpus…");

        // Load chunks (throws if folder is empty)
        var chunks = loader.LoadFolder(folder);
        var documents = loader.GetCachedTexts();

        logger.LogInformation("Loaded {Docs} document(s), {Chunks} chunks", documents.Count, chunks.Count);

        // Compute or load cached chunk embeddings
        definition.SetProgress("Computing embeddings…");
        var vectors = await GetOrComputeChunkVectorsAsync(chunks, folder, ct);

        // Pre-embed canonical section names (always in-memory — 11 short strings)
        definition.SetProgress("Preparing section index…");
        logger.LogInformation("Embedding {Count} canonical section names", CanonicalSections.Count);
        var canonicalVectors = await embeddingService.EmbedBatchAsync(CanonicalSections, ct);
        var canonicalEmbeddings = CanonicalSections
            .Zip(canonicalVectors, (name, vec) => (name, vec))
            .ToDictionary(x => x.name, x => x.vec);

        var retriever = new EmbeddingRetriever(
            chunks, vectors, canonicalEmbeddings, embeddingService, topK);

        // Build the definition of good
        logger.LogInformation("Generating definition from {Count} document(s)", documents.Count);
        var sections = await builder.BuildDefinitionAsync(documents, definition.SetProgress, ct);

        definition.SetReady(sections, retriever, retriever.DocumentCount, retriever.ChunkCount);

        logger.LogInformation("Definition of good is ready ({Count} section(s))", sections.Count);
    }

    /// <summary>
    /// Returns chunk embedding vectors, loading from cache if valid or recomputing otherwise.
    /// Cache file: <c>{folder}/embeddings-cache.json</c>
    /// Cache is valid when fingerprint and model name both match current values.
    /// </summary>
    private async Task<float[][]> GetOrComputeChunkVectorsAsync(
        List<DocumentChunk> chunks,
        string folder,
        CancellationToken ct)
    {
        var modelName = config["Foundry:EmbeddingModelName"] ?? "nomic-embed-text";
        var cacheFile = Path.Combine(folder, "embeddings-cache.json");
        var fingerprint = ComputeCorpusFingerprint(folder);

        // Try loading from cache
        if (File.Exists(cacheFile))
        {
            try
            {
                var cached = JsonSerializer.Deserialize<EmbeddingCacheFile>(
                    await File.ReadAllTextAsync(cacheFile, ct));

                if (cached?.Fingerprint == fingerprint && cached.Model == modelName
                    && cached.Entries.Count == chunks.Count)
                {
                    logger.LogInformation(
                        "Loaded {Count} chunk embeddings from cache", chunks.Count);
                    return cached.Entries
                        .OrderBy(e => e.ChunkIndex)
                        .Select(e => e.Vector)
                        .ToArray();
                }

                logger.LogInformation("Embedding cache is stale — recomputing");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not read embedding cache — recomputing");
            }
        }

        // Recompute
        logger.LogInformation("Embedding {Count} chunks (this may take a moment)…", chunks.Count);
        var texts = chunks.Select(c => c.Text).ToArray();
        var vectors = await embeddingService.EmbedBatchAsync(texts, ct);

        // Persist cache
        try
        {
            var cacheData = new EmbeddingCacheFile
            {
                Fingerprint = fingerprint,
                Model = modelName,
                Entries = chunks
                    .Select((c, i) => new EmbeddingCacheEntry
                    {
                        SourceFile = c.SourceFile,
                        ChunkIndex = c.ChunkIndex,
                        Vector = vectors[i]
                    })
                    .ToList()
            };
            await File.WriteAllTextAsync(
                cacheFile,
                JsonSerializer.Serialize(cacheData, new JsonSerializerOptions { WriteIndented = false }),
                ct);
            logger.LogInformation("Embedding cache saved to {Path}", cacheFile);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not save embedding cache — continuing without it");
        }

        return vectors;
    }

    /// <summary>
    /// SHA256 of sorted "filename|size" entries for all PDFs in the folder.
    /// Detects additions, removals, and replacements without hashing file contents.
    /// </summary>
    private static string ComputeCorpusFingerprint(string folder)
    {
        var entries = Directory
            .GetFiles(folder, "*.pdf")
            .OrderBy(f => f)
            .Select(f => $"{Path.GetFileName(f)}|{new FileInfo(f).Length}");

        var combined = string.Join("\n", entries);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

// ── Cache file models ────────────────────────────────────────────────────────

file sealed class EmbeddingCacheFile
{
    public string Fingerprint { get; set; } = "";
    public string Model { get; set; } = "";
    public List<EmbeddingCacheEntry> Entries { get; set; } = [];
}

file sealed class EmbeddingCacheEntry
{
    public string SourceFile { get; set; } = "";
    public int ChunkIndex { get; set; }
    public float[] Vector { get; set; } = [];
}
```

**Step 2: Verify build**

```
dotnet build
```
Expected: Errors on `Program.cs` (still registering `SimpleRetriever`) and `SoWImproverService.cs` (still using `SimpleRetriever`) — fixed in Tasks 6 and 7.

---

### Task 6: Update `SoWImproverService` to use the retriever from `GoodDefinition`

**Files:**
- Modify: `SoWImprover/Services/SoWImproverService.cs`

`SoWImproverService` currently injects `SimpleRetriever` and has an LLM-based `MatchSectionsAsync`. Both are replaced.

**Step 1: Update the constructor — remove `SimpleRetriever`, add `IConfiguration`**

Change the primary constructor from:
```csharp
public class SoWImproverService(
    FoundryClientFactory factory,
    SimpleRetriever retriever,
    ILogger<SoWImproverService> logger)
```
to:
```csharp
public class SoWImproverService(
    FoundryClientFactory factory,
    IConfiguration config,
    ILogger<SoWImproverService> logger)
```

**Step 2: Update `ImproveAsync` to use the embedding retriever**

In `ImproveAsync`, replace:
```csharp
var matching = await MatchSectionsAsync(client, uploadedTitles, canonicalNames, ct);
```
with:
```csharp
progress?.Report("Matching sections…");
var threshold = config.GetValue<float>("Docs:MatchThreshold", 0.6f);
var matching = await definition.Retriever!.MatchSectionsAsync(uploadedTitles, threshold, ct);
```

Also remove the `canonicalNames` variable declaration (no longer needed):
```csharp
// DELETE this line:
var canonicalNames = definition.Sections.Select(s => s.Name).ToList();
```

**Step 3: Update section retrieval inside the loop to use `RetrieveAsync`**

Change:
```csharp
var chunks = retriever.Retrieve(section.Body);
```
to:
```csharp
var chunks = await definition.Retriever!.RetrieveAsync(section.Body, ct);
```

**Step 4: Delete the `MatchSectionsAsync` method and `ParseMatchingJson` method**

These are the two private static methods starting at roughly lines 97 and 121. Delete both entirely:
- `private static async Task<Dictionary<string, string?>> MatchSectionsAsync(...)`
- `private static Dictionary<string, string?> ParseMatchingJson(...)`

Also remove the `using System.Text.Json;` at the top if it's no longer used anywhere else in the file (check — `ParseMatchingJson` was the only user of it).

**Step 5: Verify build**

```
dotnet build
```
Expected: Error on `Program.cs` only (still registering `SimpleRetriever`).

---

### Task 7: Update `Program.cs` and delete `SimpleRetriever`

**Files:**
- Modify: `SoWImprover/Program.cs`
- Delete: `SoWImprover/Services/SimpleRetriever.cs`

**Step 1: Update `Program.cs`**

Replace the `SimpleRetriever` singleton factory block:
```csharp
// DELETE this entire block:
builder.Services.AddSingleton(sp =>
{
    var loader = sp.GetRequiredService<DocumentLoader>();
    var cfg = sp.GetRequiredService<IConfiguration>();
    var folder = cfg["Docs:KnownGoodFolder"] ?? "./sample-sows";
    var chunks = loader.LoadFolder(folder);   // throws if empty
    return new SimpleRetriever(chunks, cfg);
});
```

Add `EmbeddingService` registration in its place:
```csharp
builder.Services.AddSingleton<EmbeddingService>();
```

Remove the eager resolution line:
```csharp
// DELETE this line:
_ = app.Services.GetRequiredService<SimpleRetriever>();
```

The final `Program.cs` singleton block should look like:
```csharp
builder.Services.AddSingleton<GoodDefinition>();
builder.Services.AddSingleton<DocumentLoader>();
builder.Services.AddSingleton<FoundryClientFactory>();
builder.Services.AddSingleton<DefinitionBuilder>();
builder.Services.AddSingleton<EmbeddingService>();
builder.Services.AddSingleton<DiffService>();
builder.Services.AddSingleton<SoWImproverService>();
builder.Services.AddHostedService<DefinitionGeneratorService>();
```

**Step 2: Delete `SimpleRetriever.cs`**

```
del C:\goaco\AI\SOW-RAG\SoWImprover\Services\SimpleRetriever.cs
```

**Step 3: Final build — must be clean**

```
dotnet build
```
Expected: Build succeeded, 0 errors, 0 warnings (or warnings only for nullable reference types, not errors).

---

### Task 8: Smoke test the full pipeline

**Step 1: Start the app**

```
cd C:\goaco\AI\SOW-RAG\SoWImprover
dotnet run
```

**Step 2: Watch the startup log**

Expected log sequence (roughly):
```
Loading corpus from: ./sample-sows
Loaded 4 document(s), 31 chunks
Computing embeddings…       ← calls nomic-embed-text
Embedding cache saved to ./sample-sows/embeddings-cache.json
Embedding 11 canonical section names
Analysing document 1 of 4: ...
...
Definition of good is ready (11 section(s))
```

**Step 3: Verify the cache file was created**

Check that `SoWImprover/sample-sows/embeddings-cache.json` exists and is non-empty.

**Step 4: Restart the app and confirm cache is used**

Stop the app (Ctrl+C), restart with `dotnet run`. The startup log should now show:
```
Loaded 4 chunk embeddings from cache
```
Not `Embedding X chunks (this may take a moment)…`

**Step 5: Upload a SoW PDF and verify improvement still works**

Navigate to `http://localhost:5194`, upload a PDF, click "Improve document". Confirm:
- Progress messages appear: "Matching sections…", "Improving: {title} (1 of N)…"
- Results panel shows original and improved side by side
- No errors in console

**Step 6: Verify cache invalidation**

Add or remove a PDF from `./sample-sows/`, restart the app. Log should show:
```
Embedding cache is stale — recomputing
```
Then save a new cache.
