using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using NSubstitute;
using SoWImprover.Data;
using SoWImprover.Models;
using SoWImprover.Services;

namespace SoWImprover.E2E;

/// <summary>
/// Starts the app on a real Kestrel port with stubbed services,
/// and provides a shared Playwright browser instance for all tests.
/// </summary>
public class PlaywrightFixture : IAsyncLifetime
{
    private WebApplication? _app;
    private IPlaywright? _playwright;

    public IBrowser Browser { get; private set; } = null!;
    public string BaseUrl { get; private set; } = "";
    public IServiceProvider Services => _app!.Services;

    public async Task InitializeAsync()
    {
        // Locate the SoWImprover project directory for static web assets
        var solutionDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var contentRoot = Path.Combine(solutionDir, "SoWImprover");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = contentRoot,
            EnvironmentName = "Development" // needed for UseStaticWebAssets
        });

        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.WebHost.UseStaticWebAssets();

        // Stub IChatService
        var chat = Substitute.For<IChatService>();
        chat.CompleteAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("Stubbed response.");

        // Stub IEmbeddingService
        var embedding = Substitute.For<IEmbeddingService>();
        embedding.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new float[] { 1, 0, 0 });

        // Pre-populated GoodDefinition
        var definition = new GoodDefinition();
        var retriever = new EmbeddingRetriever([], [], embedding, 1);
        definition.SetReady(
            [new DefinedSection("Scope of Work", "Test guidance.")],
            retriever, 0, 0);

        // In-memory SQLite with persistent connection
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        connection.Open();

        builder.Services.AddSingleton(definition);
        builder.Services.AddSingleton<IChatService>(chat);
        builder.Services.AddSingleton<IEmbeddingService>(embedding);
        builder.Services.AddSingleton<FoundryClientFactory>();
        builder.Services.AddSingleton<DocumentLoader>();
        builder.Services.AddSingleton<DefinitionBuilder>();
        builder.Services.AddSingleton<SoWImproverService>();
        builder.Services.AddDbContextFactory<SoWDbContext>(opts => opts.UseSqlite(connection));
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();

        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        var app = builder.Build();

        using (var db = app.Services.GetRequiredService<IDbContextFactory<SoWDbContext>>().CreateDbContext())
            db.Database.EnsureCreated();

        app.UseStaticFiles();
        app.UseAntiforgery();
        app.MapRazorComponents<SoWImprover.Components.App>()
            .AddInteractiveServerRenderMode();

        await app.StartAsync();
        _app = app;

        var server = app.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>();
        BaseUrl = addresses!.Addresses.First();

        // Start Playwright
        _playwright = await Playwright.CreateAsync();
        var headed = Environment.GetEnvironmentVariable("PLAYWRIGHT_HEADED") == "1";
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = !headed,
            SlowMo = headed ? 500 : 0
        });
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null) await Browser.CloseAsync();
        _playwright?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
