namespace SoWImprover.Services;

/// <summary>
/// Shared helpers for cleaning LLM output, used by both
/// <see cref="SoWImproverService"/> and <see cref="DefinitionBuilder"/>.
/// </summary>
internal static class LlmOutputHelper
{
    /// <summary>
    /// Strips a wrapping markdown code fence (``` ... ```) from LLM output.
    /// Only removes the opening and closing fence lines, not fences inside the content.
    /// </summary>
    public static string StripCodeFence(string text)
    {
        text = text.Trim();
        if (!text.StartsWith("```")) return text;
        var firstNewline = text.IndexOf('\n');
        if (firstNewline < 0) return text;
        text = text[(firstNewline + 1)..].TrimEnd();
        var lastNewline = text.LastIndexOf('\n');
        if (lastNewline >= 0 && text[(lastNewline + 1)..].TrimEnd('`', ' ').Length == 0)
            text = text[..lastNewline];
        return text.Trim();
    }
}
