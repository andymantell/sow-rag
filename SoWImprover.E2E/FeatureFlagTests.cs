using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using SoWImprover.Data;
using SoWImprover.Models;

namespace SoWImprover.E2E;

/// <summary>
/// Verifies that editing, explanation, and download features are hidden when
/// the EditingFeatures and Explanations feature flags are off (production default).
/// </summary>
public class FeatureFlagTests : IClassFixture<FeaturesOffFixture>, IAsyncLifetime
{
    private readonly FeaturesOffFixture _fixture;
    private IPage _page = null!;
    private Guid _documentId;

    public FeatureFlagTests(FeaturesOffFixture fixture) => _fixture = fixture;

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

    /// Verifies that the Edit button is not rendered when EditingFeatures is off.
    [Fact]
    public async Task FeaturesOff_EditButtonHidden()
    {
        await _page.GotoAsync($"{_fixture.BaseUrl}/results/{_documentId}");
        await _page.WaitForSelectorAsync(".diff-section-row");

        await Expect(_page.Locator("button:has-text('Edit')")).ToHaveCountAsync(0);
    }

    /// Verifies that the Exclude section button is not rendered when EditingFeatures is off.
    [Fact]
    public async Task FeaturesOff_ExcludeButtonHidden()
    {
        await _page.GotoAsync($"{_fixture.BaseUrl}/results/{_documentId}");
        await _page.WaitForSelectorAsync(".diff-section-row");

        await Expect(_page.Locator("button:has-text('Exclude section')")).ToHaveCountAsync(0);
    }

    /// Verifies that the Download button is not rendered when EditingFeatures is off.
    [Fact]
    public async Task FeaturesOff_DownloadButtonHidden()
    {
        await _page.GotoAsync($"{_fixture.BaseUrl}/results/{_documentId}");
        await _page.WaitForSelectorAsync(".diff-section-row");

        await Expect(_page.Locator("button:has-text('Download improved document')")).ToHaveCountAsync(0);
    }

    /// Verifies that the "What changed" explanation is not rendered when Explanations is off.
    [Fact]
    public async Task FeaturesOff_WhatChangedHidden()
    {
        await _page.GotoAsync($"{_fixture.BaseUrl}/results/{_documentId}");
        await _page.WaitForSelectorAsync(".diff-section-row");

        await Expect(_page.Locator("text=What changed")).ToHaveCountAsync(0);
    }

    /// Verifies that section content still renders when flags are off (read-only mode works).
    [Fact]
    public async Task FeaturesOff_SectionContentStillVisible()
    {
        await _page.GotoAsync($"{_fixture.BaseUrl}/results/{_documentId}");
        await _page.WaitForSelectorAsync(".diff-section-row");

        // Original, baseline, and improved content should still render
        await Expect(_page.Locator("text=Original intro content.")).ToBeVisibleAsync();
        await Expect(_page.Locator("text=Baseline intro content.")).ToBeVisibleAsync();
        await Expect(_page.Locator("text=Improved intro content.")).ToBeVisibleAsync();
    }

    /// Verifies that the back link is still present when flags are off.
    [Fact]
    public async Task FeaturesOff_BackLinkPresent()
    {
        await _page.GotoAsync($"{_fixture.BaseUrl}/results/{_documentId}");
        await _page.WaitForSelectorAsync(".diff-section-row");

        await Expect(_page.Locator("a.govuk-back-link")).ToBeVisibleAsync();
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
            FileName = "flags-test.pdf",
            OriginalText = "Test",
            UploadedAt = DateTime.UtcNow,
            Sections =
            [
                new SectionEntity
                {
                    Id = sectionId,
                    SectionIndex = 0,
                    OriginalTitle = "Introduction",
                    OriginalContent = "Original intro content.",
                    ImprovedContent = "Improved intro content.",
                    BaselineContent = "Baseline intro content.",
                    MatchedSection = "Scope of Work",
                    Explanation = "- Improved clarity",
                    Unrecognised = false,
                    Versions =
                    [
                        new SectionVersionEntity
                        {
                            Id = Guid.NewGuid(),
                            SectionId = sectionId,
                            VersionNumber = 1,
                            Content = "Improved intro content.",
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
