using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using SoWImprover.Data;
using SoWImprover.Models;

namespace SoWImprover.E2E;

public class PdfDownloadTests : IClassFixture<PlaywrightFixture>, IAsyncLifetime
{
    private readonly PlaywrightFixture _fixture;
    private IPage _page = null!;
    private Guid _documentId;

    public PdfDownloadTests(PlaywrightFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _page = await _fixture.Browser.NewPageAsync();
        _documentId = await SeedDocumentAsync();
    }

    public async Task DisposeAsync()
    {
        await _page.CloseAsync();
        await CleanupDocumentAsync();
    }

    /// Verifies that clicking "Download improved document" triggers a file
    /// download with the correct filename ("improved-sow.pdf") and that the
    /// downloaded file is a valid PDF (starts with %PDF magic bytes).
    [Fact]
    public async Task DownloadButton_TriggersPdfDownload()
    {
        await _page.GotoAsync($"{_fixture.BaseUrl}/results/{_documentId}");
        await _page.WaitForSelectorAsync(".diff-section-row");

        // Wait for the download event when clicking the button
        var download = await _page.RunAndWaitForDownloadAsync(async () =>
        {
            await _page.Locator("button:has-text('Download improved document')").ClickAsync();
        }, new() { Timeout = 30000 });

        Assert.Equal("improved-sow.pdf", download.SuggestedFilename);

        // Verify it's a valid PDF
        var path = await download.PathAsync();
        var bytes = await File.ReadAllBytesAsync(path!);
        Assert.True(bytes.Length > 0);
        Assert.Equal((byte)'%', bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'D', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);
    }

    /// Verifies that excluding a section before downloading produces a smaller
    /// PDF than downloading with all sections included (proving the excluded
    /// section's content is actually omitted).
    [Fact]
    public async Task DownloadAfterExclude_ProducesSmallerPdf()
    {
        await _page.GotoAsync($"{_fixture.BaseUrl}/results/{_documentId}");
        await _page.WaitForSelectorAsync(".diff-section-row");

        // Download with all sections
        var fullDownload = await _page.RunAndWaitForDownloadAsync(async () =>
        {
            await _page.Locator("button:has-text('Download improved document')").ClickAsync();
        }, new() { Timeout = 30000 });
        var fullPath = await fullDownload.PathAsync();
        var fullSize = new FileInfo(fullPath!).Length;

        // Exclude a section
        await _page.Locator("button:has-text('Exclude section')").First.ClickAsync();
        await Expect(_page.Locator("text=Section excluded from output")).ToBeVisibleAsync();

        // Download again
        var excludedDownload = await _page.RunAndWaitForDownloadAsync(async () =>
        {
            await _page.Locator("button:has-text('Download improved document')").ClickAsync();
        }, new() { Timeout = 30000 });
        var excludedPath = await excludedDownload.PathAsync();
        var excludedSize = new FileInfo(excludedPath!).Length;

        Assert.True(excludedSize < fullSize,
            $"Expected excluded PDF ({excludedSize} bytes) to be smaller than full PDF ({fullSize} bytes)");
    }

    private async Task<Guid> SeedDocumentAsync()
    {
        var factory = _fixture.Services.GetRequiredService<IDbContextFactory<SoWDbContext>>();
        await using var db = await factory.CreateDbContextAsync();

        var docId = Guid.NewGuid();
        var sectionAId = Guid.NewGuid();
        var sectionBId = Guid.NewGuid();

        db.Documents.Add(new DocumentEntity
        {
            Id = docId,
            FileName = "download-test.pdf",
            OriginalText = "Test",
            UploadedAt = DateTime.UtcNow,
            Sections =
            [
                new SectionEntity
                {
                    Id = sectionAId,
                    SectionIndex = 0,
                    OriginalTitle = "Introduction",
                    OriginalContent = "Original introduction.",
                    ImprovedContent = "Improved content for PDF export section one.",
                    MatchedSection = "Scope of Work",
                    Versions =
                    [
                        new SectionVersionEntity
                        {
                            Id = Guid.NewGuid(),
                            SectionId = sectionAId,
                            VersionNumber = 1,
                            Content = "Improved content for PDF export section one.",
                            CreatedAt = DateTime.UtcNow
                        }
                    ]
                },
                new SectionEntity
                {
                    Id = sectionBId,
                    SectionIndex = 1,
                    OriginalTitle = "Scope of Work",
                    OriginalContent = "Original scope content that is fairly lengthy to ensure the PDF size differs.",
                    ImprovedContent = "Improved scope content that is fairly lengthy to ensure the PDF file size measurably differs when excluded.",
                    MatchedSection = "Scope of Work",
                    Versions =
                    [
                        new SectionVersionEntity
                        {
                            Id = Guid.NewGuid(),
                            SectionId = sectionBId,
                            VersionNumber = 1,
                            Content = "Improved scope content that is fairly lengthy to ensure the PDF file size measurably differs when excluded.",
                            CreatedAt = DateTime.UtcNow
                        }
                    ]
                }
            ]
        });
        await db.SaveChangesAsync();
        return docId;
    }

    private async Task CleanupDocumentAsync()
    {
        var factory = _fixture.Services.GetRequiredService<IDbContextFactory<SoWDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var doc = await db.Documents.FindAsync(_documentId);
        if (doc is not null)
        {
            db.Documents.Remove(doc);
            await db.SaveChangesAsync();
        }
    }

    private static ILocatorAssertions Expect(ILocator locator) =>
        Assertions.Expect(locator);
}
