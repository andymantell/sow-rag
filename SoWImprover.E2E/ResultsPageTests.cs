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

    /// Verifies section headings render in the diff view (each section has a
    /// heading in both the original and improved columns).
    [Fact]
    public async Task ResultsPage_ShowsSectionHeadings()
    {
        await _page.GotoAsync($"{_fixture.BaseUrl}/results/{_documentId}");
        await _page.WaitForSelectorAsync(".diff-section-heading");

        var headings = _page.Locator(".diff-section-heading");
        // 2 sections × 2 columns (left + right) = at least 4 headings
        await Expect(headings.First).ToBeVisibleAsync();
        Assert.True(await headings.CountAsync() >= 4);
    }

    /// Verifies the side-by-side diff layout: original content appears in the
    /// left column and improved content in the right column.
    [Fact]
    public async Task ResultsPage_ShowsOriginalAndImproved()
    {
        await _page.GotoAsync($"{_fixture.BaseUrl}/results/{_documentId}");
        await _page.WaitForSelectorAsync(".diff-section-row");

        // Original content in the left column
        var leftCol = _page.Locator(".diff-col").First;
        await Expect(leftCol.Locator("text=This is the original intro.")).ToBeVisibleAsync();

        // Improved content in the right column (3rd column: Original, Baseline, RAG-improved)
        var rightCol = _page.Locator(".diff-section-row").First.Locator(".diff-col").Nth(2);
        await Expect(rightCol.Locator("text=This is the improved intro.")).ToBeVisibleAsync();
    }

    /// Verifies that sections which could not be matched to the definition of
    /// good display a "Section not recognised" warning banner.
    [Fact]
    public async Task ResultsPage_ShowsUnrecognisedBanner()
    {
        await _page.GotoAsync($"{_fixture.BaseUrl}/results/{_documentId}");
        await _page.WaitForSelectorAsync(".diff-section-row");

        await Expect(_page.Locator("text=Section not recognised").First).ToBeVisibleAsync();
    }

    /// Verifies that clicking "Exclude section" hides the section content and
    /// shows a "Section excluded from output" notice.
    [Fact]
    public async Task ResultsPage_ExcludeSection_ShowsExcludedNotice()
    {
        await _page.GotoAsync($"{_fixture.BaseUrl}/results/{_documentId}");
        await _page.WaitForSelectorAsync(".diff-section-row");

        // Click the first "Exclude section" button
        await _page.Locator("button:has-text('Exclude section')").First.ClickAsync();

        await Expect(_page.Locator("text=Section excluded from output").First).ToBeVisibleAsync();
    }

    /// Verifies that re-including a previously excluded section removes the
    /// excluded notice and restores the section content.
    [Fact]
    public async Task ResultsPage_IncludeSection_RemovesExcludedNotice()
    {
        await _page.GotoAsync($"{_fixture.BaseUrl}/results/{_documentId}");
        await _page.WaitForSelectorAsync(".diff-section-row");

        // Exclude then re-include
        await _page.Locator("button:has-text('Exclude section')").First.ClickAsync();
        await Expect(_page.Locator("text=Section excluded from output").First).ToBeVisibleAsync();

        await _page.Locator("button:has-text('Include section')").First.ClickAsync();
        await Expect(_page.Locator("text=Section excluded from output")).ToHaveCountAsync(0);

        // Section content should be restored
        await Expect(_page.Locator("text=This is the improved intro.")).ToBeVisibleAsync();
    }

    /// Verifies the "Download improved document" button is present on the
    /// results page.
    [Fact]
    public async Task ResultsPage_HasDownloadButton()
    {
        await _page.GotoAsync($"{_fixture.BaseUrl}/results/{_documentId}");
        await _page.WaitForSelectorAsync(".diff-section-row");

        await Expect(_page.Locator("button:has-text('Download improved document')")).ToBeVisibleAsync();
    }

    /// Verifies the back link is present and points to the home page.
    [Fact]
    public async Task ResultsPage_HasBackLink()
    {
        await _page.GotoAsync($"{_fixture.BaseUrl}/results/{_documentId}");
        await _page.WaitForSelectorAsync(".govuk-back-link");

        var backLink = _page.Locator("a.govuk-back-link");
        await Expect(backLink).ToBeVisibleAsync();
        await Expect(backLink).ToHaveAttributeAsync("href", "/");
    }

    /// Verifies that clicking the back link actually navigates to the home page.
    [Fact]
    public async Task ResultsPage_BackLinkNavigatesToHome()
    {
        await _page.GotoAsync($"{_fixture.BaseUrl}/results/{_documentId}");
        await _page.WaitForSelectorAsync(".govuk-back-link");

        await _page.Locator("a.govuk-back-link").ClickAsync();

        await _page.WaitForURLAsync(_fixture.BaseUrl + "/", new() { Timeout = 10000 });
    }

    /// Verifies that improved sections show a "What changed" explanation
    /// banner with the matched section name and bullet-point explanations.
    [Fact]
    public async Task ResultsPage_ShowsWhatChangedExplanation()
    {
        await _page.GotoAsync($"{_fixture.BaseUrl}/results/{_documentId}");
        await _page.WaitForSelectorAsync(".diff-section-row");

        await Expect(_page.Locator("text=What changed")).ToBeVisibleAsync();
        await Expect(_page.Locator("text=Improved clarity")).ToBeVisibleAsync();
        // Matched section name should appear in the explanation banner
        await Expect(_page.Locator("text=Scope of Work").First).ToBeVisibleAsync();
    }

    /// Verifies that navigating to a non-existent document ID redirects to
    /// the home page instead of showing an empty results page.
    [Fact]
    public async Task ResultsPage_InvalidDocumentId_RedirectsToHome()
    {
        var fakeId = Guid.NewGuid();
        await _page.GotoAsync($"{_fixture.BaseUrl}/results/{fakeId}");

        await _page.WaitForURLAsync(_fixture.BaseUrl + "/", new() { Timeout = 10000 });
    }

    /// Verifies that excluding a section persists after a full page reload,
    /// proving the suppression state was written to the database.
    [Fact]
    public async Task ResultsPage_ExcludeSection_PersistsAfterReload()
    {
        await _page.GotoAsync($"{_fixture.BaseUrl}/results/{_documentId}");
        await _page.WaitForSelectorAsync(".diff-section-row");

        // Exclude the first section
        await _page.Locator("button:has-text('Exclude section')").First.ClickAsync();
        await Expect(_page.Locator("text=Section excluded from output").First).ToBeVisibleAsync();

        // Full page reload
        await _page.ReloadAsync();
        await _page.WaitForSelectorAsync(".diff-section-row");

        // Section should still be excluded
        await Expect(_page.Locator("text=Section excluded from output").First).ToBeVisibleAsync();
        await Expect(_page.Locator("button:has-text('Include section')")).ToBeVisibleAsync();
    }

    /// Verifies that both seeded sections (improved + unrecognised) render
    /// as separate diff rows, not just the first one.
    [Fact]
    public async Task ResultsPage_ShowsAllSections()
    {
        await _page.GotoAsync($"{_fixture.BaseUrl}/results/{_documentId}");
        await _page.WaitForSelectorAsync(".diff-section-row");

        var rows = _page.Locator(".diff-section-row");
        Assert.Equal(2, await rows.CountAsync());

        // Both section titles should appear
        await Expect(_page.Locator(".diff-section-heading:has-text('Introduction')").First).ToBeVisibleAsync();
        await Expect(_page.Locator(".diff-section-heading:has-text('Scope of Work')").First).ToBeVisibleAsync();
    }

    // ── Baseline column (3-column layout) ──────────────────────

    /// Verifies the baseline column (middle) renders baseline content.
    [Fact]
    public async Task ResultsPage_ShowsBaselineContent()
    {
        await _page.GotoAsync($"{_fixture.BaseUrl}/results/{_documentId}");
        await _page.WaitForSelectorAsync(".diff-section-row");

        // Column headers
        await Expect(_page.Locator("text=Improved (no RAG)")).ToBeVisibleAsync();
        await Expect(_page.Locator("text=Improved (with RAG)")).ToBeVisibleAsync();

        // Baseline content in the middle column (index 1)
        var middleCol = _page.Locator(".diff-section-row").First.Locator(".diff-col").Nth(1);
        await Expect(middleCol.Locator("text=This is the baseline intro.")).ToBeVisibleAsync();
    }

    // ── Score badges from pre-seeded data ────────────────────────

    /// Verifies that score badges render when scores are pre-populated in the DB.
    [Fact]
    public async Task ResultsPage_ShowsScoreBadges()
    {
        await _page.GotoAsync($"{_fixture.BaseUrl}/results/{_documentId}");
        await _page.WaitForSelectorAsync(".diff-section-row");

        // Quality scores across all three columns
        await Expect(_page.Locator("text=2/5").First).ToBeVisibleAsync();   // original
        await Expect(_page.Locator("text=3/5").First).ToBeVisibleAsync();   // baseline
        await Expect(_page.Locator("text=4/5").First).ToBeVisibleAsync();   // RAG

        // Decimal scores
        await Expect(_page.Locator("text=0.92").First).ToBeVisibleAsync();  // RAG faithfulness
        await Expect(_page.Locator("text=0.78").First).ToBeVisibleAsync();  // context precision
    }

    /// Verifies that score badge tooltips are present and accessible.
    [Fact]
    public async Task ResultsPage_ScoreBadges_HaveTooltips()
    {
        await _page.GotoAsync($"{_fixture.BaseUrl}/results/{_documentId}");
        await _page.WaitForSelectorAsync(".diff-section-row");

        // Info buttons with aria-labels
        await Expect(_page.Locator("[aria-label='About quality score']").First).ToBeVisibleAsync();
        await Expect(_page.Locator("[aria-label='About faithfulness score']").First).ToBeVisibleAsync();

        // Tooltip content (hidden by default but in DOM)
        await Expect(_page.Locator("[role='tooltip']").First).ToBeAttachedAsync();
    }

    /// Verifies that unrecognised sections do not show score badges.
    [Fact]
    public async Task ResultsPage_UnrecognisedSection_NoScoreBadges()
    {
        await _page.GotoAsync($"{_fixture.BaseUrl}/results/{_documentId}");
        await _page.WaitForSelectorAsync(".diff-section-row");

        // The second section row is unrecognised — it should have no score badges
        var unrecognisedRow = _page.Locator(".diff-section-row").Nth(1);
        await Expect(unrecognisedRow.Locator(".app-score-badges")).ToHaveCountAsync(0);
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
                    BaselineContent = "This is the baseline intro.",
                    MatchedSection = "Scope of Work",
                    Explanation = "- Improved clarity",
                    Unrecognised = false,
                    OriginalQualityScore = 2,
                    BaselineQualityScore = 3,
                    RagQualityScore = 4,
                    BaselineFaithfulnessScore = 0.85,
                    RagFaithfulnessScore = 0.92,
                    ContextPrecisionScore = 0.78,
                    ContextRecallScore = 0.65,
                    BaselineFactualCorrectnessScore = 0.80,
                    RagFactualCorrectnessScore = 0.88,
                    BaselineResponseRelevancyScore = 0.75,
                    RagResponseRelevancyScore = 0.82,
                    NoiseSensitivityScore = 0.15,
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
