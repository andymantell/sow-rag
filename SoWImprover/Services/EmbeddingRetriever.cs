using SoWImprover.Models;

namespace SoWImprover.Services;

/// <summary>
/// Semantic retriever using pre-computed embedding vectors.
/// </summary>
public class EmbeddingRetriever
{
    private readonly List<DocumentChunk> _chunks;
    private readonly float[][] _vectors;       // parallel to _chunks
    private readonly IEmbeddingService _embeddingService;
    private readonly int _topK;
    private readonly float _minScore;

    /// <summary>Total number of corpus chunks.</summary>
    public int ChunkCount => _chunks.Count;

    /// <summary>Number of distinct source documents.</summary>
    public int DocumentCount { get; }

    public EmbeddingRetriever(
        List<DocumentChunk> chunks,
        float[][] vectors,
        IEmbeddingService embeddingService,
        int topK,
        float minScore = 0f)
    {
        if (chunks.Count != vectors.Length)
            throw new ArgumentException("chunks and vectors must be the same length.");

        _chunks = chunks;
        _vectors = vectors;
        _embeddingService = embeddingService;
        _topK = topK;
        _minScore = minScore;
        DocumentCount = chunks.Select(c => c.SourceFile).Distinct().Count();
    }

    /// <summary>
    /// Returns the top-k corpus chunks most semantically similar to <paramref name="query"/>,
    /// filtered by the minimum similarity threshold.
    /// </summary>
    public async Task<List<ScoredChunk>> RetrieveAsync(string query, CancellationToken ct = default)
    {
        var queryVec = await _embeddingService.EmbedAsync(query, ct);
        return _chunks
            .Select((c, i) => new ScoredChunk(c, CosineSimilarity(queryVec, _vectors[i])))
            .OrderByDescending(x => x.Score)
            .Where(x => x.Score >= _minScore)
            .Take(_topK)
            .ToList();
    }

    internal static float CosineSimilarity(float[] a, float[] b)
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

/// <summary>A chunk paired with its cosine similarity score against the query.</summary>
public record ScoredChunk(DocumentChunk Chunk, float Score);
