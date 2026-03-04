using Markdig;
using Microsoft.AspNetCore.Components;

namespace SoWImprover.Services;

/// <summary>
/// Centralises markdown-to-HTML rendering so all components use the same pipeline configuration.
/// Uses GFM-compatible extensions (pipe tables, task lists, autolinks, strikethrough) to match
/// the behaviour of the previous marked.js frontend with <c>gfm: true</c>.
/// </summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder()
            .UsePipeTables()
            .UseTaskLists()
            .UseAutoLinks()
            .UseEmphasisExtras()
            .Build();

    /// <summary>Renders <paramref name="markdown"/> to an HTML <see cref="MarkupString"/>.</summary>
    public static MarkupString ToMarkupString(string markdown) =>
        (MarkupString)Markdown.ToHtml(markdown ?? string.Empty, Pipeline);
}
