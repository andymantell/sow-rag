using SoWImprover.Models;

namespace SoWImprover.Services;

/// <summary>
/// Semantic retriever using pre-computed embedding vectors.
/// Provides cosine-similarity-based chunk retrieval and section title matching.
/// </summary>
public class EmbeddingRetriever
{
    private readonly List<DocumentChunk> _chunks;
    private readonly float[][] _vectors;       // parallel to _chunks
    private readonly Dictionary<string, float[]> _canonicalEmbeddings; // section name → vector (name + definition content)
    private readonly EmbeddingService _embeddingService;
    private readonly ILogger<EmbeddingRetriever> _logger;
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
        ILogger<EmbeddingRetriever> logger,
        int topK)
    {
        if (chunks.Count != vectors.Length)
            throw new ArgumentException("chunks and vectors must be the same length.");
        if (canonicalEmbeddings.Count == 0)
            throw new ArgumentException("canonicalEmbeddings must not be empty.");

        _chunks = chunks;
        _vectors = vectors;
        _canonicalEmbeddings = canonicalEmbeddings;
        _embeddingService = embeddingService;
        _logger = logger;
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
    /// Maps each uploaded section to the best-matching canonical section name, or null if none
    /// exceeds <paramref name="threshold"/>. <paramref name="titles"/> and
    /// <paramref name="embeddingTexts"/> must be parallel lists of the same length —
    /// titles are used as dictionary keys, embeddingTexts (title + body) are what gets embedded.
    /// </summary>
    public async Task<Dictionary<string, string?>> MatchSectionsAsync(
        IList<string> titles,
        IList<string> embeddingTexts,
        float threshold,
        CancellationToken ct = default)
    {
        if (titles.Count != embeddingTexts.Count)
            throw new ArgumentException("titles and embeddingTexts must be the same length.");

        var vectors = await _embeddingService.EmbedBatchAsync(embeddingTexts.ToArray(), ct);

        var result = new Dictionary<string, string?>(titles.Count);
        for (var i = 0; i < titles.Count; i++)
        {
            var best = _canonicalEmbeddings
                .Select(kv => (name: kv.Key, score: CosineSimilarity(vectors[i], kv.Value)))
                .OrderByDescending(x => x.score)
                .FirstOrDefault();

            var matched = best.score >= threshold;
            _logger.LogInformation(
                "Section match: '{Title}' → '{Best}' (score {Score:F3}, threshold {Threshold:F2}, {Result})",
                titles[i], best.name, best.score, threshold,
                matched ? "MATCHED" : "NO MATCH");

            result[titles[i]] = matched ? best.name : null;
        }
        return result;
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException($"Vector dimension mismatch: {a.Length} vs {b.Length}.");

        float dot = 0f, normA = 0f, normB = 0f;
        for (var i = 0; i < a.Length; i++)
        {
            dot   += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        return (normA == 0f || normB == 0f) ? 0f : dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }
}
