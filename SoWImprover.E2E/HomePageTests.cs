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

    [Fact]
    public async Task HomePage_LoadsSuccessfully()
    {
        await _page.GotoAsync(_fixture.BaseUrl);

        await Expect(_page).ToHaveTitleAsync("Improve a statement of work");
    }

    [Fact]
    public async Task HomePage_ShowsUploadForm()
    {
        await _page.GotoAsync(_fixture.BaseUrl);

        await Expect(_page.Locator("text=Upload SoW PDF")).ToBeVisibleAsync();
        await Expect(_page.Locator("button:has-text('Improve document')")).ToBeVisibleAsync();
        await Expect(_page.Locator("#file-input")).ToBeAttachedAsync();
    }

    [Fact]
    public async Task HomePage_ShowsDefinitionSidebar()
    {
        await _page.GotoAsync(_fixture.BaseUrl);

        // Definition should be ready (stubbed) — should show "Definition of good"
        await Expect(_page.Locator("text=Definition of good")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task HomePage_SubmitWithoutFile_ShowsError()
    {
        await _page.GotoAsync(_fixture.BaseUrl);

        await _page.Locator("button:has-text('Improve document')").ClickAsync();

        await Expect(_page.Locator(".govuk-error-summary >> text=Select a PDF to upload")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task HomePage_NoPreviousDocuments_HidesTable()
    {
        await _page.GotoAsync(_fixture.BaseUrl);

        await Expect(_page.Locator("text=Previous documents")).Not.ToBeVisibleAsync();
    }

    private static ILocatorAssertions Expect(ILocator locator) =>
        Assertions.Expect(locator);

    private static IPageAssertions Expect(IPage page) =>
        Assertions.Expect(page);
}
