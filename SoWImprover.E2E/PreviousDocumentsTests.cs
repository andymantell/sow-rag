using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using SoWImprover.Data;
using SoWImprover.Models;

namespace SoWImprover.E2E;

public class PreviousDocumentsTests : IClassFixture<PlaywrightFixture>, IAsyncLifetime
{
    private readonly PlaywrightFixture _fixture;
    private IPage _page = null!;
    private Guid _documentId;

    public PreviousDocumentsTests(PlaywrightFixture fixture) => _fixture = fixture;

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

    [Fact]
    public async Task PreviousDocuments_ShowsViewResultsLink()
    {
        await _page.GotoAsync(_fixture.BaseUrl);

        await Expect(_page.Locator("text=Previous documents")).ToBeVisibleAsync();
        await Expect(_page.Locator("text=prev-doc-test.pdf")).ToBeVisibleAsync();
        await Expect(_page.Locator("text=View results")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task PreviousDocuments_ViewResultsNavigatesToResultsPage()
    {
        await _page.GotoAsync(_fixture.BaseUrl);

        await _page.Locator("a:has-text('View results')").First.ClickAsync();

        await _page.WaitForURLAsync("**/results/**", new() { Timeout = 10000 });
        Assert.Contains($"/results/{_documentId}", _page.Url);
        await _page.WaitForSelectorAsync(".diff-section-row");
    }

    private async Task<Guid> SeedDocumentAsync()
    {
        var factory = _fixture.Services.GetRequiredService<IDbContextFactory<SoWDbContext>>();
        await using var db = await factory.CreateDbContextAsync();

        var docId = Guid.NewGuid();
        var sectionId = Guid.NewGuid();

        db.Documents.Add(new DocumentEntity
        {
            Id = docId,
            FileName = "prev-doc-test.pdf",
            OriginalText = "Test content",
            UploadedAt = DateTime.UtcNow,
            Sections =
            [
                new SectionEntity
                {
                    Id = sectionId,
                    SectionIndex = 0,
                    OriginalTitle = "Introduction",
                    OriginalContent = "Original text.",
                    ImprovedContent = "Improved text.",
                    MatchedSection = "Scope of Work",
                    Explanation = "- Made it better",
                    Versions =
                    [
                        new SectionVersionEntity
                        {
                            Id = Guid.NewGuid(),
                            SectionId = sectionId,
                            VersionNumber = 1,
                            Content = "Improved text.",
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
