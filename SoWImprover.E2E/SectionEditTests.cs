using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using SoWImprover.Data;
using SoWImprover.Models;

namespace SoWImprover.E2E;

public class SectionEditTests : IClassFixture<PlaywrightFixture>, IAsyncLifetime
{
    private readonly PlaywrightFixture _fixture;
    private IPage _page = null!;
    private Guid _documentId;

    public SectionEditTests(PlaywrightFixture fixture) => _fixture = fixture;

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
    public async Task EditButton_OpensEditor()
    {
        await _page.GotoAsync($"{_fixture.BaseUrl}/results/{_documentId}");
        await _page.WaitForSelectorAsync(".diff-section-row");

        // Wait for the editor module to load (Edit button is disabled until then)
        await _page.Locator("button:has-text('Edit'):enabled").First.WaitForAsync(
            new() { Timeout = 15000 });

        await _page.Locator("button:has-text('Edit'):enabled").First.ClickAsync();

        // Editor toolbar and container should appear
        await Expect(_page.Locator(".app-editor-toolbar")).ToBeVisibleAsync();
        await Expect(_page.Locator(".app-editor-container")).ToBeVisibleAsync();

        // Save and Cancel buttons should appear
        await Expect(_page.Locator("button:has-text('Save')")).ToBeVisibleAsync();
        await Expect(_page.Locator("button:has-text('Cancel')")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task EditAndSave_UpdatesContent()
    {
        await _page.GotoAsync($"{_fixture.BaseUrl}/results/{_documentId}");
        await _page.WaitForSelectorAsync(".diff-section-row");

        // Wait for editor module to load
        await _page.Locator("button:has-text('Edit'):enabled").First.WaitForAsync(
            new() { Timeout = 15000 });

        await _page.Locator("button:has-text('Edit'):enabled").First.ClickAsync();

        // Wait for the Tiptap editor to initialise
        await _page.WaitForSelectorAsync(".tiptap", new() { Timeout = 15000 });

        // Type into the editor
        var editor = _page.Locator(".tiptap").First;
        await editor.ClickAsync();
        await _page.Keyboard.PressAsync("Control+a");
        await _page.Keyboard.TypeAsync("Edited content from E2E test");

        // Save
        await _page.Locator("button:has-text('Save')").ClickAsync();

        // Editor should close
        await Expect(_page.Locator(".app-editor-toolbar")).Not.ToBeVisibleAsync();

        // New content should appear on the page
        await Expect(_page.Locator("text=Edited content from E2E test")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task EditAndCancel_KeepsOriginalContent()
    {
        await _page.GotoAsync($"{_fixture.BaseUrl}/results/{_documentId}");
        await _page.WaitForSelectorAsync(".diff-section-row");

        await _page.Locator("button:has-text('Edit'):enabled").First.WaitForAsync(
            new() { Timeout = 15000 });
        await _page.Locator("button:has-text('Edit'):enabled").First.ClickAsync();
        await _page.WaitForSelectorAsync(".tiptap", new() { Timeout = 15000 });

        // Cancel without changes (no confirm dialog since nothing changed)
        await _page.Locator("button:has-text('Cancel')").ClickAsync();

        // Editor should close, original content should remain
        await Expect(_page.Locator(".app-editor-toolbar")).Not.ToBeVisibleAsync();
        await Expect(_page.Locator("text=Improved text for editing.")).ToBeVisibleAsync();
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
            FileName = "edit-test.pdf",
            OriginalText = "Test",
            UploadedAt = DateTime.UtcNow,
            Sections =
            [
                new SectionEntity
                {
                    Id = sectionId,
                    SectionIndex = 0,
                    OriginalTitle = "Introduction",
                    OriginalContent = "Original text.",
                    ImprovedContent = "Improved text for editing.",
                    MatchedSection = "Scope of Work",
                    Explanation = "- Improved clarity",
                    Versions =
                    [
                        new SectionVersionEntity
                        {
                            Id = Guid.NewGuid(),
                            SectionId = sectionId,
                            VersionNumber = 1,
                            Content = "Improved text for editing.",
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
