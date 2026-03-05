using System.Text.RegularExpressions;
using Markdig;
using Microsoft.AspNetCore.Components;

namespace SoWImprover.Services;

/// <summary>
/// Centralises markdown-to-HTML rendering so all components use the same pipeline.
/// Post-processes the HTML to inject GOV.UK Frontend classes on standard elements,
/// so the CDN stylesheet handles all typography without custom CSS duplication.
/// </summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder()
            .UsePipeTables()
            .UseTaskLists()
            .UseAutoLinks()
            .UseEmphasisExtras()
            .DisableHtml()
            .Build();

    /// <summary>Renders <paramref name="markdown"/> to an HTML <see cref="MarkupString"/>.</summary>
    public static MarkupString ToMarkupString(string markdown)
    {
        var html = Markdown.ToHtml(markdown ?? string.Empty, Pipeline);
        html = AddGovUkClasses(html);
        return (MarkupString)html;
    }

    /// <summary>
    /// Renders a short inline markdown snippet (e.g. a single bullet point) to HTML,
    /// stripping the outer &lt;p&gt; wrapper that Markdig adds around plain text.
    /// Suitable for use inside &lt;li&gt; elements where block-level wrapping is unwanted.
    /// </summary>
    public static MarkupString ToInlineMarkupString(string markdown)
    {
        var html = AddGovUkClasses(Markdown.ToHtml(markdown ?? string.Empty, Pipeline)).Trim();
        const string openP  = "<p class=\"govuk-body\">";
        const string closeP = "</p>";
        if (html.StartsWith(openP) && html.EndsWith(closeP))
            html = html[openP.Length..^closeP.Length].Trim();
        return (MarkupString)html;
    }

    /// <summary>
    /// Injects GOV.UK Frontend classes into the plain HTML emitted by Markdig.
    /// Markdig outputs clean, attribute-free tags (except th/td which may carry
    /// a style attribute for column alignment), so simple replacements are safe
    /// for most tags and targeted regexes handle the exceptions.
    /// </summary>
    private static string AddGovUkClasses(string html)
    {
        // ── Headings ──────────────────────────────────────────────────────────
        // Markdig never adds attributes to heading tags, so plain Replace is safe.
        html = html
            .Replace("<h1>", "<h1 class=\"govuk-heading-xl\">")
            .Replace("<h2>", "<h2 class=\"govuk-heading-l\">")
            .Replace("<h3>", "<h3 class=\"govuk-heading-m\">")
            .Replace("<h4>", "<h4 class=\"govuk-heading-s\">")
            .Replace("<h5>", "<h5 class=\"govuk-heading-s\">")
            .Replace("<h6>", "<h6 class=\"govuk-heading-s\">");

        // ── Body text ─────────────────────────────────────────────────────────
        html = html.Replace("<p>", "<p class=\"govuk-body\">");

        // ── Lists ─────────────────────────────────────────────────────────────
        html = html
            .Replace("<ul>", "<ul class=\"govuk-list govuk-list--bullet\">")
            .Replace("<ol>", "<ol class=\"govuk-list govuk-list--number\">");

        // ── Tables ────────────────────────────────────────────────────────────
        html = html
            .Replace("<table>",  "<table class=\"govuk-table\">")
            .Replace("<thead>",  "<thead class=\"govuk-table__head\">")
            .Replace("<tbody>",  "<tbody class=\"govuk-table__body\">")
            .Replace("<tr>",     "<tr class=\"govuk-table__row\">");

        // th/td may carry a style attribute when the pipe table has column alignment,
        // so use a regex that preserves any existing attributes.
        html = Regex.Replace(html, @"<th(\s|>)",
            m => $"<th class=\"govuk-table__header\"{m.Groups[1].Value}");
        html = Regex.Replace(html, @"<td(\s|>)",
            m => $"<td class=\"govuk-table__cell\"{m.Groups[1].Value}");

        return html;
    }
}
