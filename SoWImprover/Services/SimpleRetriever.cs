using SoWImprover.Models;

namespace SoWImprover.Services;

public class SimpleRetriever
{
    private readonly List<DocumentChunk> _chunks;
    private readonly Dictionary<string, double> _idf;
    private readonly int _topK;

    /// <summary>Total number of chunks loaded from the corpus.</summary>
    public int ChunkCount => _chunks.Count;

    /// <summary>Number of distinct source documents in the corpus.</summary>
    public int DocumentCount { get; }

    public SimpleRetriever(List<DocumentChunk> chunks, IConfiguration config)
    {
        _chunks = chunks;
        _topK = config.GetValue<int>("Docs:TopKChunks", 5);
        _idf = ComputeIdf(chunks);
        DocumentCount = chunks.Select(c => c.SourceFile).Distinct().Count();
    }

    /// <summary>
    /// Returns the top-<em>k</em> chunks from the corpus most relevant to <paramref name="query"/>
    /// using TF-IDF cosine similarity.
    /// </summary>
    public List<DocumentChunk> Retrieve(string query)
    {
        var queryVec = ComputeTfIdf(Tokenize(query), _idf);
        return _chunks
            .Select(c => (chunk: c, score: CosineSimilarity(queryVec, ComputeTfIdf(Tokenize(c.Text), _idf))))
            .OrderByDescending(x => x.score)
            .Take(_topK)
            .Select(x => x.chunk)
            .ToList();
    }

    private static Dictionary<string, double> ComputeIdf(List<DocumentChunk> chunks)
    {
        var docCount = chunks.Count;
        var termDocFreq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var chunk in chunks)
        {
            foreach (var term in Tokenize(chunk.Text).Distinct(StringComparer.OrdinalIgnoreCase))
                termDocFreq[term] = termDocFreq.GetValueOrDefault(term) + 1;
        }

        return termDocFreq.ToDictionary(
            kv => kv.Key,
            kv => Math.Log((double)(docCount + 1) / (kv.Value + 1)) + 1.0,
            StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, double> ComputeTfIdf(
        List<string> terms, Dictionary<string, double> idf)
    {
        if (terms.Count == 0) return [];

        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var grp in terms.GroupBy(t => t, StringComparer.OrdinalIgnoreCase))
        {
            var tf = (double)grp.Count() / terms.Count;
            var idfScore = idf.GetValueOrDefault(grp.Key, 1.0);
            result[grp.Key] = tf * idfScore;
        }
        return result;
    }

    private static double CosineSimilarity(
        Dictionary<string, double> a, Dictionary<string, double> b)
    {
        double dot = 0, normA = 0, normB = 0;
        foreach (var kv in a)
        {
            normA += kv.Value * kv.Value;
            if (b.TryGetValue(kv.Key, out var bVal))
                dot += kv.Value * bVal;
        }
        foreach (var kv in b)
            normB += kv.Value * kv.Value;

        return (normA == 0 || normB == 0) ? 0 : dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    private static List<string> Tokenize(string text)
        => text.ToLowerInvariant()
               .Split([' ', '\n', '\r', '\t', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '"', '\'', '-', '/'],
                      StringSplitOptions.RemoveEmptyEntries)
               .Where(t => t.Length > 2)
               .ToList();
}
