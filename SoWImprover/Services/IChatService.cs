namespace SoWImprover.Services;

/// <summary>
/// Thin abstraction over LLM chat completion, enabling testability.
/// All prompt construction and response post-processing stays in the calling service.
/// </summary>
public interface IChatService
{
    /// <summary>Sends a single user-message prompt and returns the raw text response.</summary>
    Task<string> CompleteAsync(string prompt, int maxTokens, CancellationToken ct = default);
}
