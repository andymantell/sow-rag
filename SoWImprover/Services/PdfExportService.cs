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
public static class PdfExportService
{
    private const float FontSize = 10f;
    private const float HeadingSize = 13f;
    private const float CellPadding = 4f;

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

                        var body = section.Unrecognised
                            ? section.OriginalContent
                            : section.ImprovedContent ?? section.OriginalContent;

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
        var text = StripMarkdown(string.Join("\n", lines));
        if (!string.IsNullOrWhiteSpace(text))
            col.Item().Text(text).FontSize(FontSize);
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
                        cell.Text(StripInlineMarkdown(cellText)).FontSize(FontSize - 1).Bold();
                    else
                        cell.Text(StripInlineMarkdown(cellText)).FontSize(FontSize - 1);
                }
            }
        });
    }

    private static bool IsSeparatorRow(string line)
    {
        var trimmed = line.Trim().Trim('|');
        return Regex.IsMatch(trimmed, @"^[\s\-:|]+$") && trimmed.Contains('-');
    }

    private static string[] ParseTableRow(string line)
    {
        line = line.Trim();
        if (line.StartsWith('|')) line = line[1..];
        if (line.EndsWith('|')) line = line[..^1];
        return line.Split('|').Select(c => c.Trim()).ToArray();
    }

    private static string StripMarkdown(string text)
    {
        text = Regex.Replace(text, @"^#{1,6}\s+", "", RegexOptions.Multiline);
        text = StripInlineMarkdown(text);
        text = Regex.Replace(text, @"^[-•]\s+", "• ", RegexOptions.Multiline);
        return text.Trim();
    }

    private static string StripInlineMarkdown(string text)
    {
        text = text.Replace("**", "").Replace("__", "");
        // Only strip * used as emphasis, not bullet markers
        text = Regex.Replace(text, @"(?<=\S)\*|\*(?=\S)", "");
        return text.Trim();
    }
}
