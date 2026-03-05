using OpenAI.Embeddings;

namespace SoWImprover.Services;

/// <summary>
/// Wraps the Foundry Local embeddings endpoint, providing single and batch embedding calls.
/// </summary>
public class EmbeddingService(FoundryClientFactory factory)
{
    // nomic-embed-text context limit is 8192 tokens (~32K chars). Truncate conservatively
    // so chunks containing long markdown table rows or other non-prose content never exceed it.
    private const int MaxEmbedChars = 8000;

    /// <summary>Returns the embedding vector for a single text input.</summary>
    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        if (text.Length > MaxEmbedChars)
            text = text[..MaxEmbedChars];
        var client = await factory.GetEmbeddingClientAsync(ct);
        var result = await client.GenerateEmbeddingAsync(text, cancellationToken: ct);
        return result.Value.ToFloats().ToArray();
    }

    /// <summary>
    /// Returns embedding vectors for multiple inputs, one request per text.
    /// Ollama does not support true batch embedding — sending multiple inputs in one
    /// request concatenates them and exceeds the context limit. Azure OpenAI does support
    /// batching, but sequential calls are safe for both backends.
    /// Results are in the same order as <paramref name="texts"/>.
    /// </summary>
    public async Task<float[][]> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (texts.Count == 0) return [];
        var results = new float[texts.Count][];
        for (var i = 0; i < texts.Count; i++)
            results[i] = await EmbedAsync(texts[i], ct);
        return results;
    }
}
