namespace SoWImprover.Services;

/// <summary>
/// Thin abstraction over LLM chat completion, enabling testability.
/// All prompt construction and response post-processing stays in the calling service.
/// </summary>
public interface IChatService
{
    /// <summary>Sends a single user-message prompt and returns the raw text response.</summary>
    /// <param name="think">Whether to enable model reasoning. Use true for tasks
    /// requiring judgement (improvement, definition building), false for mechanical
    /// tasks (matching, redaction, explanation).</param>
    Task<string> CompleteAsync(string prompt, int maxTokens, CancellationToken ct = default, bool think = false);
}
