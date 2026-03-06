using Microsoft.Playwright;

namespace SoWImprover.E2E;

public class DefinitionLoadingTests : IClassFixture<DefinitionNotReadyFixture>, IAsyncLifetime
{
    private readonly DefinitionNotReadyFixture _fixture;
    private IPage _page = null!;

    public DefinitionLoadingTests(DefinitionNotReadyFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _page = await _fixture.Browser.NewPageAsync();
    }

    public async Task DisposeAsync()
    {
        await _page.CloseAsync();
    }

    /// Verifies that when the definition of good is still loading, the home
    /// page shows a spinner and "Starting" status text.
    [Fact]
    public async Task DefinitionNotReady_ShowsSpinner()
    {
        await _page.GotoAsync(_fixture.BaseUrl);

        await Expect(_page.Locator(".app-spinner")).ToBeVisibleAsync();
        await Expect(_page.Locator("text=Starting")).ToBeVisibleAsync();
    }

    /// Verifies that submitting the form while the definition is still loading
    /// shows a validation error telling the user to wait.
    [Fact]
    public async Task DefinitionNotReady_SubmitShowsStillLoadingError()
    {
        await _page.GotoAsync(_fixture.BaseUrl);
        // Wait for Blazor circuit to connect so form submit handler is active
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await _page.Locator("button:has-text('Improve document')").ClickAsync();

        await Expect(_page.Locator("text=still loading")).ToBeVisibleAsync(
            new() { Timeout = 10000 });
    }

    private static ILocatorAssertions Expect(ILocator locator) =>
        Assertions.Expect(locator);
}
