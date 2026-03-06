using OpenAI.Chat;

namespace SoWImprover.Services;

/// <summary>
/// Production implementation of <see cref="IChatService"/> that delegates to
/// <see cref="FoundryClientFactory"/> for the underlying <see cref="ChatClient"/>.
/// </summary>
public class ChatService(FoundryClientFactory factory) : IChatService
{
    public async Task<string> CompleteAsync(string prompt, int maxTokens, CancellationToken ct = default)
    {
        var client = await factory.GetChatClientAsync(ct);
        var opts = new ChatCompletionOptions { MaxOutputTokenCount = maxTokens };
        var completion = await client.CompleteChatAsync(
            [new UserChatMessage(prompt)], opts, cancellationToken: ct);
        var content = completion.Value.Content;
        return content.Count > 0 ? content[0].Text ?? "" : "";
    }
}
