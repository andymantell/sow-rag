using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SoWImprover.Models;

namespace SoWImprover.Services;

public class CorpusInitialisationService(
    DocumentLoader loader,
    IEmbeddingService embeddingService,
    IChatService chatService,
    DefinitionBuilder definitionBuilder,
    IConfiguration configuration,
    ILogger<CorpusInitialisationService> logger)
{
    public async Task InitialiseAsync(GoodDefinition definition, Action<string> progress, CancellationToken ct)
    {
        var folder = configuration["Docs:KnownGoodFolder"] ?? "./sample-sows";
        var topK = configuration.GetValue<int>("Docs:TopKChunks", 5);
        var minScore = configuration.GetValue<float>("Docs:MinChunkScore", 0.3f);

        logger.LogInformation("Loading corpus from: {Folder}", folder);
        progress("Loading corpus…");

        var chunks = loader.LoadFolder(folder);
        var documents = loader.GetCachedTexts();

        logger.LogInformation("Loaded {Docs} document(s), {Chunks} chunks",
            documents.Count, chunks.Count);
        progress($"Loaded {documents.Count} document(s), {chunks.Count} chunks — preparing embeddings…");

        var vectors = await GetOrComputeChunkVectorsAsync(chunks, folder, progress, ct);

        await RedactChunksAsync(chunks, folder, progress, ct);

        logger.LogInformation("Generating definition from {Count} document(s)", documents.Count);
        progress($"Starting analysis of {documents.Count} document(s)…");
        var sections = await GetOrBuildDefinitionAsync(documents, folder, progress, ct);

        var retriever = new EmbeddingRetriever(chunks, vectors, embeddingService, topK, minScore);

        definition.SetReady(sections, retriever, retriever.DocumentCount, retriever.ChunkCount);

        logger.LogInformation("Definition of good is ready ({Count} section(s))", sections.Count);
    }

    /// <summary>
    /// Returns definition sections, loading from cache if valid or rebuilding via LLM otherwise.
    /// Cache file: <c>{folder}/definition-cache.json</c>.
    /// Cache is valid when corpus fingerprint and LLM model name both match.
    /// </summary>
    private async Task<IReadOnlyList<DefinedSection>> GetOrBuildDefinitionAsync(
        IReadOnlyList<(string FileName, string Text)> documents,
        string folder,
        Action<string> progress,
        CancellationToken ct)
    {
        var modelName = configuration.GetValue<bool>("Foundry:UseLocal")
            ? configuration["Foundry:LocalModelName"] ?? "phi-4"
            : configuration["Foundry:CloudModelName"] ?? "";
        var cacheFile = Path.Combine(folder, "definition-cache.json");
        var fingerprint = ComputeCorpusFingerprint(folder);

        // Try loading from cache
        if (File.Exists(cacheFile))
        {
            try
            {
                progress("Loading definition from cache…");
                var cached = JsonSerializer.Deserialize<DefinitionCacheFile>(
                    await File.ReadAllTextAsync(cacheFile, ct));

                if (cached?.Fingerprint == fingerprint && cached.Model == modelName)
                {
                    var sections = cached.Sections
                        .Select(s => new DefinedSection(s.Name, s.Content))
                        .ToList();

                    logger.LogInformation(
                        "Loaded {Count} definition section(s) from cache", sections.Count);
                    return sections;
                }

                logger.LogInformation("Definition cache is stale — rebuilding");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not read definition cache — rebuilding");
            }
        }

        // Rebuild via LLM
        var result = await definitionBuilder.BuildDefinitionAsync(documents, progress, ct);

        // Persist cache
        try
        {
            var cacheData = new DefinitionCacheFile
            {
                Fingerprint = fingerprint,
                Model = modelName,
                Sections = result
                    .Select(s => new DefinitionCacheSection { Name = s.Name, Content = s.Content })
                    .ToList()
            };
            var json = JsonSerializer.Serialize(
                cacheData, new JsonSerializerOptions { WriteIndented = false });
            await File.WriteAllTextAsync(cacheFile, json, ct);
            logger.LogInformation("Definition cache saved to {Path}", cacheFile);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not save definition cache — continuing without it");
        }

        return result;
    }

    /// <summary>
    /// Returns chunk embedding vectors, loading from cache if valid or recomputing otherwise.
    /// Cache file: <c>{folder}/embeddings-cache.json</c>.
    /// Cache is valid when fingerprint and model name both match.
    /// </summary>
    private async Task<float[][]> GetOrComputeChunkVectorsAsync(
        List<DocumentChunk> chunks,
        string folder,
        Action<string> progress,
        CancellationToken ct)
    {
        var modelName = configuration.GetValue<bool>("Foundry:UseLocal")
            ? configuration["Ollama:EmbeddingModelName"] ?? "nomic-embed-text"
            : configuration["Foundry:CloudEmbeddingDeployment"] ?? "text-embedding-3-small";
        var cacheFile = Path.Combine(folder, "embeddings-cache.json");
        var fingerprint = ComputeCorpusFingerprint(folder);

        // Try loading from cache
        if (File.Exists(cacheFile))
        {
            try
            {
                progress("Loading embeddings from cache…");
                var cached = JsonSerializer.Deserialize<EmbeddingCacheFile>(
                    await File.ReadAllTextAsync(cacheFile, ct));

                if (cached?.Fingerprint == fingerprint
                    && cached.Model == modelName
                    && cached.Entries.Count == chunks.Count)
                {
                    logger.LogInformation(
                        "Loaded {Count} chunk embeddings from cache", chunks.Count);
                    return cached.Entries
                        .OrderBy(e => e.GlobalIndex)
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
        logger.LogInformation(
            "Embedding {Count} chunks (this may take a moment)…", chunks.Count);
        progress($"Computing embeddings for {chunks.Count} chunks (first run only — results will be cached)…");
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
                        GlobalIndex = i,
                        Vector = vectors[i]
                    })
                    .ToList()
            };
            var json = JsonSerializer.Serialize(
                cacheData, new JsonSerializerOptions { WriteIndented = false });
            await File.WriteAllTextAsync(cacheFile, json, ct);
            logger.LogInformation("Embedding cache saved to {Path}", cacheFile);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not save embedding cache — continuing without it");
        }

        return vectors;
    }

    /// <summary>
    /// Redacts identifying details from all chunks using regex pre-pass + LLM.
    /// Results are cached in <c>{folder}/redactions-cache.json</c>, keyed by
    /// corpus fingerprint and chat model name.
    /// </summary>
    private async Task RedactChunksAsync(
        List<DocumentChunk> chunks,
        string folder,
        Action<string> progress,
        CancellationToken ct)
    {
        var modelName = configuration.GetValue<bool>("Foundry:UseLocal")
            ? configuration["Foundry:LocalModelName"] ?? "phi-4"
            : configuration["Foundry:CloudModelName"] ?? "";
        var cacheFile = Path.Combine(folder, "redactions-cache.json");
        var fingerprint = ComputeCorpusFingerprint(folder);

        // Try loading from cache
        if (File.Exists(cacheFile))
        {
            try
            {
                progress("Loading redactions from cache…");
                var cached = JsonSerializer.Deserialize<RedactionCacheFile>(
                    await File.ReadAllTextAsync(cacheFile, ct));

                if (cached?.Fingerprint == fingerprint
                    && cached.Model == modelName
                    && cached.Entries.Count == chunks.Count)
                {
                    // Populate RedactedText on each chunk from cache
                    var lookup = cached.Entries
                        .ToDictionary(e => $"{e.SourceFile}|{e.ChunkIndex}", e => e.RedactedText);

                    foreach (var chunk in chunks)
                    {
                        var key = $"{chunk.SourceFile}|{chunk.ChunkIndex}";
                        chunk.RedactedText = lookup.GetValueOrDefault(key, chunk.Text);
                    }

                    logger.LogInformation(
                        "Loaded {Count} chunk redactions from cache", chunks.Count);
                    return;
                }

                logger.LogInformation("Redaction cache is stale — recomputing");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not read redaction cache — recomputing");
            }
        }

        // Recompute via LLM
        logger.LogInformation(
            "Redacting {Count} chunks via LLM (first run only — results will be cached)…",
            chunks.Count);

        for (var i = 0; i < chunks.Count; i++)
        {
            progress($"Redacting chunk {i + 1} of {chunks.Count}…");
            try
            {
                chunks[i].RedactedText = await ChunkRedactor.RedactWithLlmAsync(
                    chunks[i].Text, chatService, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "LLM redaction failed for chunk {Index} — using regex fallback", i);
                chunks[i].RedactedText = ChunkRedactor.Redact(chunks[i].Text);
            }
        }

        // Persist cache
        try
        {
            var cacheData = new RedactionCacheFile
            {
                Fingerprint = fingerprint,
                Model = modelName,
                Entries = chunks
                    .Select(c => new RedactionCacheEntry
                    {
                        SourceFile = c.SourceFile,
                        ChunkIndex = c.ChunkIndex,
                        RedactedText = c.RedactedText
                    })
                    .ToList()
            };
            var json = JsonSerializer.Serialize(
                cacheData, new JsonSerializerOptions { WriteIndented = false });
            await File.WriteAllTextAsync(cacheFile, json, ct);
            logger.LogInformation("Redaction cache saved to {Path}", cacheFile);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not save redaction cache — continuing without it");
        }
    }

    /// <summary>
    /// SHA256 of sorted "filename|size" entries for all PDFs in the folder.
    /// Detects additions, removals, and replacements without hashing file contents.
    /// </summary>
    internal static string ComputeCorpusFingerprint(string folder)
    {
        var entries = Directory
            .GetFiles(folder, "*.pdf")
            .OrderBy(f => f)
            .Select(f =>
            {
                var info = new FileInfo(f);
                return $"{info.Name}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            });

        var combined = string.Join("\n", entries);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
