namespace SoWImprover.Services;

/// <summary>
/// Abstraction over embedding generation, enabling testability.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>Returns the embedding vector for a single text input.</summary>
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);

    /// <summary>Returns embedding vectors for multiple inputs (sequential calls).</summary>
    Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}
