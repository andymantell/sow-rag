using Microsoft.AspNetCore.Components;

namespace SoWImprover.Services;

/// <summary>
/// Centralises markdown-to-HTML rendering so all components use the same pipeline configuration.
/// </summary>
public static class MarkdownRenderer
{
    /// <summary>Renders <paramref name="markdown"/> to an HTML <see cref="MarkupString"/>.</summary>
    public static MarkupString ToMarkupString(string markdown) =>
        (MarkupString)Markdig.Markdown.ToHtml(markdown ?? string.Empty);
}
