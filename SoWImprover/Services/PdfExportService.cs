using System.Text.RegularExpressions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SoWImprover.Models;

namespace SoWImprover.Services;

/// <summary>
/// Generates a PDF containing the improved SoW document content.
/// Sections are rendered with their original titles and improved body text.
/// Unrecognised sections use the original content unchanged.
/// </summary>
public static partial class PdfExportService
{
    private const float FontSize = 10f;
    private const float HeadingSize = 13f;
    private const float CellPadding = 4f;

    [GeneratedRegex(@"^(#{1,6})\s+(.+)$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^(\s*)([-•*]|\d+[.)]) (.+)$")]
    private static partial Regex BulletRegex();

    [GeneratedRegex(@"(\*\*\*(.+?)\*\*\*|\*\*(.+?)\*\*|\*(.+?)\*|__(.+?)__|_(.+?)_)")]
    private static partial Regex InlineMarkdownRegex();

    [GeneratedRegex(@"^[\s\-:|]+$")]
    private static partial Regex SeparatorRowRegex();

    public static byte[] Generate(ImprovementResult result, IReadOnlySet<int>? suppressedSections = null)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(50);
                page.MarginVertical(40);
                page.DefaultTextStyle(x => x.FontSize(FontSize).FontFamily("Arial"));

                page.Content().Column(col =>
                {
                    col.Spacing(6);

                    for (var i = 0; i < result.Sections.Count; i++)
                    {
                        if (suppressedSections?.Contains(i) == true) continue;
                        var section = result.Sections[i];
                        col.Item().Text(section.OriginalTitle)
                            .Bold().FontSize(HeadingSize);

                        var body = section.ImprovedContent ?? section.OriginalContent;

                        RenderMarkdownBlocks(col, body);
                        col.Item().PaddingBottom(6);
                    }
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            });
        }).GeneratePdf();
    }

    /// <summary>
    /// Splits markdown content into blocks (paragraphs, tables, lists)
    /// and renders each with the appropriate QuestPDF element.
    /// </summary>
    private static void RenderMarkdownBlocks(ColumnDescriptor col, string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return;

        var lines = markdown.Split('\n');
        var blockLines = new List<string>();
        var inTable = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var isTableLine = line.TrimStart().StartsWith('|');

            if (isTableLine && !inTable)
            {
                // Flush any pending paragraph/list block
                FlushTextBlock(col, blockLines);
                blockLines.Clear();
                inTable = true;
            }
            else if (!isTableLine && inTable)
            {
                // Flush table block
                RenderTable(col, blockLines);
                blockLines.Clear();
                inTable = false;
            }

            // Skip blank lines between paragraphs but collect everything else
            if (!string.IsNullOrWhiteSpace(line) || inTable)
                blockLines.Add(line);
            else if (blockLines.Count > 0)
            {
                FlushTextBlock(col, blockLines);
                blockLines.Clear();
            }
        }

        // Flush remaining
        if (blockLines.Count > 0)
        {
            if (inTable)
                RenderTable(col, blockLines);
            else
                FlushTextBlock(col, blockLines);
        }
    }

    private static void FlushTextBlock(ColumnDescriptor col, List<string> lines)
    {
        if (lines.Count == 0) return;

        foreach (var rawLine in lines)
        {
            // Headings
            var headingMatch = HeadingRegex().Match(rawLine);
            if (headingMatch.Success)
            {
                col.Item().DefaultTextStyle(x => x.Bold().FontSize(HeadingSize - 1))
                    .Text(text => RenderInlineMarkdown(text, headingMatch.Groups[2].Value));
                continue;
            }

            // Bullet / list items
            var bulletMatch = BulletRegex().Match(rawLine);
            if (bulletMatch.Success)
            {
                col.Item().PaddingLeft(10).Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(FontSize));
                    text.Span("• ");
                    RenderInlineMarkdown(text, bulletMatch.Groups[3].Value);
                });
                continue;
            }

            // Regular paragraph line
            var trimmed = rawLine.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                col.Item().Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(FontSize));
                    RenderInlineMarkdown(text, trimmed);
                });
            }
        }
    }

    /// <summary>
    /// Parses inline markdown (bold, italic, bold+italic) and renders
    /// styled spans into a QuestPDF text descriptor.
    /// </summary>
    private static void RenderInlineMarkdown(TextDescriptor text, string markdown)
    {
        var lastIndex = 0;

        foreach (Match m in InlineMarkdownRegex().Matches(markdown))
        {
            // Plain text before this match
            if (m.Index > lastIndex)
                text.Span(markdown[lastIndex..m.Index]);

            if (m.Groups[2].Success)       // ***bold italic***
                text.Span(m.Groups[2].Value).Bold().Italic();
            else if (m.Groups[3].Success)  // **bold**
                text.Span(m.Groups[3].Value).Bold();
            else if (m.Groups[4].Success)  // *italic*
                text.Span(m.Groups[4].Value).Italic();
            else if (m.Groups[5].Success)  // __bold__
                text.Span(m.Groups[5].Value).Bold();
            else if (m.Groups[6].Success)  // _italic_
                text.Span(m.Groups[6].Value).Italic();

            lastIndex = m.Index + m.Length;
        }

        // Trailing plain text
        if (lastIndex < markdown.Length)
            text.Span(markdown[lastIndex..]);
    }

    /// <summary>
    /// Parses markdown pipe table lines and renders a QuestPDF table.
    /// Expects: header row, separator row (---|---), then data rows.
    /// </summary>
    private static void RenderTable(ColumnDescriptor col, List<string> lines)
    {
        if (lines.Count < 2) return;

        var rows = lines
            .Where(l => !IsSeparatorRow(l))
            .Select(ParseTableRow)
            .Where(r => r.Length > 0)
            .ToList();

        if (rows.Count == 0) return;

        var columnCount = rows.Max(r => r.Length);

        col.Item().Table(table =>
        {
            table.ColumnsDefinition(cd =>
            {
                for (var c = 0; c < columnCount; c++)
                    cd.RelativeColumn();
            });

            for (var r = 0; r < rows.Count; r++)
            {
                var cells = rows[r];
                var isHeader = r == 0;

                for (var c = 0; c < columnCount; c++)
                {
                    var cellText = c < cells.Length ? cells[c] : "";

                    var cell = table.Cell()
                        .Row((uint)(r + 1))
                        .Column((uint)(c + 1))
                        .Border(0.5f)
                        .BorderColor(Colors.Grey.Medium)
                        .Padding(CellPadding);

                    if (isHeader)
                        cell.DefaultTextStyle(x => x.FontSize(FontSize - 1).Bold())
                            .Text(text => RenderInlineMarkdown(text, cellText));
                    else
                        cell.DefaultTextStyle(x => x.FontSize(FontSize - 1))
                            .Text(text => RenderInlineMarkdown(text, cellText));
                }
            }
        });
    }

    private static bool IsSeparatorRow(string line)
    {
        var trimmed = line.Trim().Trim('|');
        return SeparatorRowRegex().IsMatch(trimmed) && trimmed.Contains('-');
    }

    private static string[] ParseTableRow(string line)
    {
        line = line.Trim();
        if (line.StartsWith('|')) line = line[1..];
        if (line.EndsWith('|')) line = line[..^1];
        return line.Split('|').Select(c => c.Trim()).ToArray();
    }

}
