using Microsoft.Playwright;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SoWImprover.E2E;

public class UploadFlowTests : IClassFixture<PlaywrightFixture>, IAsyncLifetime
{
    private readonly PlaywrightFixture _fixture;
    private IPage _page = null!;
    private string? _testPdfPath;

    public UploadFlowTests(PlaywrightFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _page = await _fixture.Browser.NewPageAsync();

        // Generate a minimal test PDF
        _testPdfPath = Path.Combine(Path.GetTempPath(), $"e2e-test-{Guid.NewGuid():N}.pdf");
        var pdfBytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Content().Text("Test SoW document content");
            });
        }).GeneratePdf();
        await File.WriteAllBytesAsync(_testPdfPath, pdfBytes);
    }

    public async Task DisposeAsync()
    {
        await _page.CloseAsync();
        if (_testPdfPath is not null && File.Exists(_testPdfPath))
            File.Delete(_testPdfPath);
    }

    [Fact]
    public async Task Upload_NavigatesToResultsPage()
    {
        await _page.GotoAsync(_fixture.BaseUrl);
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await _page.Locator("#file-input").SetInputFilesAsync(_testPdfPath!);
        await _page.WaitForTimeoutAsync(500); // Blazor processes file change over SignalR

        await _page.Locator("button:has-text('Improve document')").ClickAsync();

        // Should navigate to results page
        await _page.WaitForURLAsync("**/results/**", new() { Timeout = 30000 });
        Assert.Contains("/results/", _page.Url);
    }

    [Fact]
    public async Task Upload_ResultsPageShowsSections()
    {
        await _page.GotoAsync(_fixture.BaseUrl);
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await _page.Locator("#file-input").SetInputFilesAsync(_testPdfPath!);
        await _page.WaitForTimeoutAsync(500);
        await _page.Locator("button:has-text('Improve document')").ClickAsync();

        await _page.WaitForURLAsync("**/results/**", new() { Timeout = 30000 });
        await _page.WaitForSelectorAsync(".diff-section-row");

        var sections = _page.Locator(".diff-section-row");
        Assert.True(await sections.CountAsync() >= 1);
    }

    [Fact]
    public async Task Upload_AppearsInPreviousDocuments()
    {
        await _page.GotoAsync(_fixture.BaseUrl);
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await _page.Locator("#file-input").SetInputFilesAsync(_testPdfPath!);
        await _page.WaitForTimeoutAsync(500);
        await _page.Locator("button:has-text('Improve document')").ClickAsync();

        await _page.WaitForURLAsync("**/results/**", new() { Timeout = 30000 });

        // Navigate back to home and wait for Blazor to render
        await _page.GotoAsync(_fixture.BaseUrl);
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Should now show the previous documents table
        await Expect(_page.Locator("text=Previous documents")).ToBeVisibleAsync(
            new() { Timeout = 10000 });
        await Expect(_page.Locator("text=View results").First).ToBeVisibleAsync();
    }

    private static ILocatorAssertions Expect(ILocator locator) =>
        Assertions.Expect(locator);
}
