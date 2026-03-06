using Microsoft.Playwright;

namespace SoWImprover.E2E;

public class HomePageTests : IClassFixture<PlaywrightFixture>, IAsyncLifetime
{
    private readonly PlaywrightFixture _fixture;
    private IPage _page = null!;

    public HomePageTests(PlaywrightFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _page = await _fixture.Browser.NewPageAsync();
    }

    public async Task DisposeAsync()
    {
        await _page.CloseAsync();
    }

    /// Verifies the home page loads and has the correct page title.
    [Fact]
    public async Task HomePage_LoadsSuccessfully()
    {
        await _page.GotoAsync(_fixture.BaseUrl);

        await Expect(_page).ToHaveTitleAsync("Improve a statement of work");
    }

    /// Verifies the upload form renders with a file input, label, and submit button.
    [Fact]
    public async Task HomePage_ShowsUploadForm()
    {
        await _page.GotoAsync(_fixture.BaseUrl);

        await Expect(_page.Locator("text=Upload SoW PDF")).ToBeVisibleAsync();
        await Expect(_page.Locator("button:has-text('Improve document')")).ToBeVisibleAsync();
        await Expect(_page.Locator("#file-input")).ToBeAttachedAsync();
    }

    /// Verifies the definition sidebar appears when the definition is ready.
    [Fact]
    public async Task HomePage_ShowsDefinitionSidebar()
    {
        await _page.GotoAsync(_fixture.BaseUrl);

        // Definition should be ready (stubbed) — should show "Definition of good"
        await Expect(_page.Locator("text=Definition of good")).ToBeVisibleAsync();
    }

    /// Verifies that submitting the form without selecting a file shows a
    /// GOV.UK-style validation error directing the user to upload a PDF.
    [Fact]
    public async Task HomePage_SubmitWithoutFile_ShowsError()
    {
        await _page.GotoAsync(_fixture.BaseUrl);
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await _page.Locator("button:has-text('Improve document')").ClickAsync();

        await Expect(_page.Locator(".govuk-error-summary >> text=Select a PDF to upload")).ToBeVisibleAsync();
    }

    /// Verifies the "Previous documents" table is hidden when no documents
    /// have been uploaded yet.
    [Fact]
    public async Task HomePage_NoPreviousDocuments_HidesTable()
    {
        await _page.GotoAsync(_fixture.BaseUrl);

        await Expect(_page.Locator("text=Previous documents")).Not.ToBeVisibleAsync();
    }

    /// Verifies that uploading a non-PDF file (text file renamed to .txt)
    /// shows a file-type validation error and does not navigate away.
    [Fact]
    public async Task HomePage_UploadNonPdf_ShowsFileTypeError()
    {
        await _page.GotoAsync(_fixture.BaseUrl);
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Create a plain text file (not a valid PDF)
        var fakePath = Path.Combine(Path.GetTempPath(), $"e2e-fake-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(fakePath, "This is not a PDF.");
        try
        {
            await _page.Locator("#file-input").SetInputFilesAsync(fakePath);
            await _page.WaitForTimeoutAsync(500);
            await _page.Locator("button:has-text('Improve document')").ClickAsync();

            await Expect(_page.Locator("text=must be a PDF").First).ToBeVisibleAsync(
                new() { Timeout = 10000 });
            Assert.Equal(_fixture.BaseUrl + "/", _page.Url);
        }
        finally
        {
            File.Delete(fakePath);
        }
    }

    private static ILocatorAssertions Expect(ILocator locator) =>
        Assertions.Expect(locator);

    private static IPageAssertions Expect(IPage page) =>
        Assertions.Expect(page);
}
