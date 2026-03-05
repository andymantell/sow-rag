using SoWImprover.Models;

namespace SoWImprover.Services;

/// <summary>
/// Semantic retriever using pre-computed embedding vectors.
/// Replaces <c>SimpleRetriever</c>; exposes async chunk retrieval
/// plus section-title matching via cosine similarity.
/// </summary>
public class EmbeddingRetriever
{
    private readonly List<DocumentChunk> _chunks;
    private readonly float[][] _vectors;       // parallel to _chunks: _vectors[i] is the embedding for _chunks[i]
    private readonly Dictionary<string, float[]> _canonicalEmbeddings; // section name → vector
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

        if (canonicalEmbeddings.Count == 0)
            throw new ArgumentException(
                "canonicalEmbeddings must not be empty; provide at least one section vector.");

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
        if (a.Length != b.Length)
            throw new ArgumentException(
                $"Vector dimension mismatch: {a.Length} vs {b.Length}.");

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
