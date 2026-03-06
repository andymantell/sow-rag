using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using SoWImprover.Data;
using SoWImprover.Models;

namespace SoWImprover.E2E;

public class ResultsPageTests : IClassFixture<PlaywrightFixture>, IAsyncLifetime
{
    private readonly PlaywrightFixture _fixture;
    private IPage _page = null!;
    private Guid _documentId;

    public ResultsPageTests(PlaywrightFixture fixture) => _fixture = fixture;

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
    public async Task ResultsPage_ShowsSectionHeadings()
    {
        await _page.GotoAsync($"{_fixture.BaseUrl}/results/{_documentId}");
        await _page.WaitForSelectorAsync(".diff-section-heading");

        var headings = _page.Locator(".diff-section-heading");
        // Each section appears twice (left + right columns)
        await Expect(headings.First).ToBeVisibleAsync();
        Assert.True(await headings.CountAsync() >= 2);
    }

    [Fact]
    public async Task ResultsPage_ShowsOriginalAndImproved()
    {
        await _page.GotoAsync($"{_fixture.BaseUrl}/results/{_documentId}");
        await _page.WaitForSelectorAsync(".diff-section-row");

        // Original content in the left column
        var leftCol = _page.Locator(".diff-col").First;
        await Expect(leftCol.Locator("text=This is the original intro.")).ToBeVisibleAsync();

        // Improved content in the right column
        var rightCol = _page.Locator(".diff-section-row").First.Locator(".diff-col").Nth(1);
        await Expect(rightCol.Locator("text=This is the improved intro.")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ResultsPage_ShowsUnrecognisedBanner()
    {
        await _page.GotoAsync($"{_fixture.BaseUrl}/results/{_documentId}");
        await _page.WaitForSelectorAsync(".diff-section-row");

        await Expect(_page.Locator("text=Section not recognised")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ResultsPage_ExcludeSection_ShowsExcludedNotice()
    {
        await _page.GotoAsync($"{_fixture.BaseUrl}/results/{_documentId}");
        await _page.WaitForSelectorAsync(".diff-section-row");

        // Click the first "Exclude section" button
        await _page.Locator("button:has-text('Exclude section')").First.ClickAsync();

        await Expect(_page.Locator("text=Section excluded from output")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ResultsPage_IncludeSection_RemovesExcludedNotice()
    {
        await _page.GotoAsync($"{_fixture.BaseUrl}/results/{_documentId}");
        await _page.WaitForSelectorAsync(".diff-section-row");

        // Exclude then re-include
        await _page.Locator("button:has-text('Exclude section')").First.ClickAsync();
        await Expect(_page.Locator("text=Section excluded from output")).ToBeVisibleAsync();

        await _page.Locator("button:has-text('Include section')").First.ClickAsync();
        await Expect(_page.Locator("text=Section excluded from output")).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task ResultsPage_HasDownloadButton()
    {
        await _page.GotoAsync($"{_fixture.BaseUrl}/results/{_documentId}");
        await _page.WaitForSelectorAsync(".diff-section-row");

        await Expect(_page.Locator("button:has-text('Download improved document')")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ResultsPage_HasBackLink()
    {
        await _page.GotoAsync($"{_fixture.BaseUrl}/results/{_documentId}");
        await _page.WaitForSelectorAsync(".govuk-back-link");

        var backLink = _page.Locator("a.govuk-back-link");
        await Expect(backLink).ToBeVisibleAsync();
        await Expect(backLink).ToHaveAttributeAsync("href", "/");
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
            FileName = "e2e-test.pdf",
            OriginalText = "Introduction\nThis is the original intro.\n\nScope of Work\nOriginal scope content.",
            UploadedAt = DateTime.UtcNow,
            Sections =
            [
                new SectionEntity
                {
                    Id = sectionAId,
                    SectionIndex = 0,
                    OriginalTitle = "Introduction",
                    OriginalContent = "This is the original intro.",
                    ImprovedContent = "This is the improved intro.",
                    MatchedSection = "Scope of Work",
                    Explanation = "- Improved clarity",
                    Unrecognised = false,
                    Versions =
                    [
                        new SectionVersionEntity
                        {
                            Id = Guid.NewGuid(),
                            SectionId = sectionAId,
                            VersionNumber = 1,
                            Content = "This is the improved intro.",
                            CreatedAt = DateTime.UtcNow
                        }
                    ]
                },
                new SectionEntity
                {
                    Id = sectionBId,
                    SectionIndex = 1,
                    OriginalTitle = "Scope of Work",
                    OriginalContent = "Original scope content.",
                    ImprovedContent = null,
                    Unrecognised = true
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

    private static IPageAssertions Expect(IPage page) =>
        Assertions.Expect(page);
}
