using System.Diagnostics;
using OpenAI.Chat;

namespace SoWImprover.Services;

/// <summary>
/// Production implementation of <see cref="IChatService"/> that delegates to
/// <see cref="FoundryClientFactory"/> for the underlying <see cref="ChatClient"/>.
/// Logs the duration and output length for every LLM call.
/// </summary>
public class ChatService(FoundryClientFactory factory, ILogger<ChatService> logger) : IChatService
{
    // Maps maxTokens values to human-readable call types for logging.
    // Each call site uses a distinct constant, so this is a reliable identifier.
    private static readonly Dictionary<int, string> CallTypes = new()
    {
        [4096] = "Improvement",
        [2048] = "Definition",
        [1024] = "Redaction/Summary",
        [600] = "SectionMatching",
        [300] = "Explanation"
    };

    public async Task<string> CompleteAsync(string prompt, int maxTokens, CancellationToken ct = default, bool think = false)
    {
        var callType = CallTypes.GetValueOrDefault(maxTokens, $"LLM({maxTokens})");
        logger.LogInformation("LLM call started: {CallType} (maxTokens={MaxTokens}, think={Think})",
            callType, maxTokens, think);

        var sw = Stopwatch.StartNew();
        var client = await factory.GetChatClientAsync(ct);
        var opts = new ChatCompletionOptions
        {
            MaxOutputTokenCount = maxTokens,
            // Qwen3.5 only supports boolean thinking (on/off), not granular levels.
            // Ollama maps reasoning_effort "none" → think=false, "high" → think=true.
#pragma warning disable OPENAI001 // Experimental API
            ReasoningEffortLevel = think
                ? ChatReasoningEffortLevel.High
                : ChatReasoningEffortLevel.None
#pragma warning restore OPENAI001
        };
        var completion = await client.CompleteChatAsync(
            [new UserChatMessage(prompt)], opts, cancellationToken: ct);
        sw.Stop();

        var content = completion.Value.Content;
        var text = content.Count > 0 ? content[0].Text ?? "" : "";
        // Strip any <think>...</think> block the model may still emit
        text = StripThinkingBlock(text);

        logger.LogInformation(
            "LLM call completed: {CallType} — {Duration:F1}s, {OutputChars} chars",
            callType, sw.Elapsed.TotalSeconds, text.Length);

        return text;
    }

    private static string StripThinkingBlock(string text)
    {
        var start = text.IndexOf("<think>", StringComparison.Ordinal);
        if (start < 0) return text;
        var end = text.IndexOf("</think>", start, StringComparison.Ordinal);
        if (end < 0) return text;
        return text.Remove(start, end - start + "</think>".Length).TrimStart();
    }
}
