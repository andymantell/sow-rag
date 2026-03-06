using QuestPDF.Infrastructure;
using SoWImprover.Models;
using SoWImprover.Services;

namespace SoWImprover.Tests.Unit;

public class PdfExportServiceTests
{
    static PdfExportServiceTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static ImprovementResult CreateResult(params (string title, string content, bool unrecognised)[] sections)
    {
        return new ImprovementResult
        {
            Sections = sections.Select(s => new SectionResult
            {
                OriginalTitle = s.title,
                OriginalContent = s.content,
                ImprovedContent = s.unrecognised ? null : $"Improved: {s.content}",
                Unrecognised = s.unrecognised
            }).ToList()
        };
    }

    [Fact]
    public void Generate_SimpleMarkdown_ProducesValidPdf()
    {
        var result = CreateResult(("Introduction", "This is the intro.", false));

        var pdf = PdfExportService.Generate(result);

        Assert.NotNull(pdf);
        Assert.True(pdf.Length > 0);
        // PDF magic bytes
        Assert.Equal((byte)'%', pdf[0]);
        Assert.Equal((byte)'P', pdf[1]);
        Assert.Equal((byte)'D', pdf[2]);
        Assert.Equal((byte)'F', pdf[3]);
    }

    [Fact]
    public void Generate_BoldAndItalic_DoesNotThrow()
    {
        var result = new ImprovementResult
        {
            Sections =
            [
                new SectionResult
                {
                    OriginalTitle = "Formatting",
                    OriginalContent = "plain",
                    ImprovedContent = "**bold** and *italic* and ***both***"
                }
            ]
        };

        var pdf = PdfExportService.Generate(result);
        Assert.True(pdf.Length > 0);
    }

    [Fact]
    public void Generate_TableContent_DoesNotThrow()
    {
        var result = new ImprovementResult
        {
            Sections =
            [
                new SectionResult
                {
                    OriginalTitle = "Table Section",
                    OriginalContent = "plain",
                    ImprovedContent = "| Header 1 | Header 2 |\n|---|---|\n| Cell 1 | Cell 2 |"
                }
            ]
        };

        var pdf = PdfExportService.Generate(result);
        Assert.True(pdf.Length > 0);
    }

    [Fact]
    public void Generate_SuppressedSections_ExcludedFromOutput()
    {
        var result = CreateResult(
            ("Section A", "Content A", false),
            ("Section B", "Content B", false));

        var suppressed = new HashSet<int> { 1 };

        var fullPdf = PdfExportService.Generate(result);
        var partialPdf = PdfExportService.Generate(result, suppressed);

        // The PDF with a suppressed section should be smaller
        Assert.True(partialPdf.Length < fullPdf.Length);
    }

    [Fact]
    public void Generate_UnrecognisedSection_UsesOriginalContent()
    {
        var result = CreateResult(("Unknown", "Original body text.", true));

        // Should not throw — unrecognised sections use OriginalContent
        var pdf = PdfExportService.Generate(result);
        Assert.True(pdf.Length > 0);
    }

    [Fact]
    public void Generate_UnrecognisedSectionWithEdit_UsesImprovedContent()
    {
        // Simulate a user editing an unrecognised section — ImprovedContent gets set
        var withEdit = new ImprovementResult
        {
            Sections =
            [
                new SectionResult
                {
                    OriginalTitle = "Unknown",
                    OriginalContent = "Short.",
                    ImprovedContent = "This section has been manually edited by the user with much longer content that differs from the original.",
                    Unrecognised = true
                }
            ]
        };

        var withoutEdit = new ImprovementResult
        {
            Sections =
            [
                new SectionResult
                {
                    OriginalTitle = "Unknown",
                    OriginalContent = "Short.",
                    ImprovedContent = null,
                    Unrecognised = true
                }
            ]
        };

        var pdfWithEdit = PdfExportService.Generate(withEdit);
        var pdfWithoutEdit = PdfExportService.Generate(withoutEdit);

        // The edited version should be larger because it has more content
        Assert.True(pdfWithEdit.Length > pdfWithoutEdit.Length,
            "PDF should include the edited content, not the original");
    }

    [Fact]
    public void Generate_EmptyContent_DoesNotThrow()
    {
        var result = new ImprovementResult
        {
            Sections =
            [
                new SectionResult
                {
                    OriginalTitle = "Empty",
                    OriginalContent = "",
                    ImprovedContent = ""
                }
            ]
        };

        var pdf = PdfExportService.Generate(result);
        Assert.True(pdf.Length > 0);
    }

    [Fact]
    public void Generate_BulletList_DoesNotThrow()
    {
        var result = new ImprovementResult
        {
            Sections =
            [
                new SectionResult
                {
                    OriginalTitle = "Lists",
                    OriginalContent = "plain",
                    ImprovedContent = "- Item one\n- Item two\n- Item three"
                }
            ]
        };

        var pdf = PdfExportService.Generate(result);
        Assert.True(pdf.Length > 0);
    }
}
