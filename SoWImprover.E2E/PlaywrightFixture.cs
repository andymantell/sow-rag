using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;
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

    /// <summary>Whether GoodDefinition starts as ready. Override in subclass for not-ready tests.</summary>
    protected virtual bool DefinitionReady => true;

    /// <summary>Feature flag overrides. Override in subclass to test different flag states.</summary>
    protected virtual Dictionary<string, string?> FeatureFlags => new()
    {
        ["FeatureManagement:EditingFeatures"] = "true",
        ["FeatureManagement:Explanations"] = "true",
        ["FeatureManagement:Evaluation"] = "false"
    };

    public async Task InitializeAsync()
    {
        var solutionDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var projectDir = Path.Combine(solutionDir, "SoWImprover");
        var wwwrootDir = Path.Combine(projectDir, "wwwroot");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = projectDir,
            EnvironmentName = "Production" // avoid UseStaticWebAssets in Development
        });

        builder.WebHost.UseUrls("http://127.0.0.1:0");

        // Feature flags — override in subclass via FeatureFlags property
        builder.Configuration.AddInMemoryCollection(FeatureFlags);

        ConfigureServices(builder.Services);

        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        var app = builder.Build();

        using (var db = app.Services.GetRequiredService<IDbContextFactory<SoWDbContext>>().CreateDbContext())
            db.Database.EnsureCreated();

        // Serve wwwroot (CSS, images, JS libraries)
        app.UseStaticFiles();

        // Serve collocated .razor.js files from the project directory
        // (Components/Pages/Results.razor.js, Components/Shared/ResultsPanel.razor.js)
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(projectDir),
            RequestPath = ""
        });

        app.UseAntiforgery();
        app.MapRazorComponents<SoWImprover.Components.App>()
            .AddInteractiveServerRenderMode();

        await app.StartAsync();
        _app = app;

        var server = app.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>();
        BaseUrl = addresses!.Addresses.First();

        _playwright = await Playwright.CreateAsync();
        var headed = Environment.GetEnvironmentVariable("PLAYWRIGHT_HEADED") == "1";
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = !headed,
            SlowMo = headed ? 500 : 0
        });
    }

    protected virtual void ConfigureServices(IServiceCollection services)
    {
        // Stub IChatService with context-aware responses
        var chat = Substitute.For<IChatService>();
        chat.CompleteAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(ci =>
            {
                var prompt = ci.ArgAt<string>(0);
                if (prompt.Contains("classifying sections"))
                    return "{\"Introduction\": null, \"SCOPE OF WORK\": \"Scope of Work\"}";
                if (prompt.Contains("redaction tool", StringComparison.OrdinalIgnoreCase))
                    return prompt[(prompt.LastIndexOf("TEXT TO REDACT:", StringComparison.Ordinal) + 15)..].Trim();
                if (prompt.Contains("rewrite", StringComparison.OrdinalIgnoreCase))
                    return "Improved section content from E2E stub.";
                return "- Improved clarity and structure";
            });
        services.AddSingleton(chat);

        // Stub IEmbeddingService
        var embedding = Substitute.For<IEmbeddingService>();
        embedding.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new float[] { 1, 0, 0 });
        services.AddSingleton(embedding);

        // Stub DocumentLoader (virtual ExtractTextAsync returns test text)
        var loaderConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Docs:ChunkSize"] = "500",
                ["Docs:ChunkOverlap"] = "50"
            })
            .Build();
        var loader = Substitute.For<DocumentLoader>(
            loaderConfig,
            Substitute.For<ILogger<DocumentLoader>>());
        loader.ExtractTextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(
                "INTRODUCTION\nThis is a test introduction for the uploaded document.\n\n" +
                "SCOPE OF WORK\nThis section describes the scope."));
        services.AddSingleton(loader);

        // GoodDefinition
        var definition = new GoodDefinition();
        if (DefinitionReady)
        {
            var retriever = new EmbeddingRetriever([], [], embedding, 1);
            definition.SetReady(
                [new DefinedSection("Scope of Work", "Test guidance.")],
                retriever, 0, 0);
        }
        services.AddSingleton(definition);

        services.AddSingleton<FoundryClientFactory>();
        services.AddSingleton<DefinitionBuilder>();
        services.AddSingleton<SoWImproverService>();
        services.AddSingleton<EvaluationService>();

        // Stub IEvaluationSummaryService (Results.razor injects it for summary generation)
        var summaryService = Substitute.For<IEvaluationSummaryService>();
        summaryService.GenerateSummaryAsync(
                Arg.Any<List<SectionSummaryInput>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("Stub evaluation summary.");
        services.AddSingleton(summaryService);

        // Feature flags (Results.razor injects IFeatureManager)
        services.AddFeatureManagement();

        // In-memory SQLite
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        connection.Open();
        services.AddDbContextFactory<SoWDbContext>(opts => opts.UseSqlite(connection));

        services.AddRazorComponents().AddInteractiveServerComponents();
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

/// <summary>
/// Fixture variant where GoodDefinition starts in a not-ready state.
/// </summary>
public class DefinitionNotReadyFixture : PlaywrightFixture
{
    protected override bool DefinitionReady => false;
}

/// <summary>
/// Fixture variant where EditingFeatures and Explanations flags are off (production default).
/// </summary>
public class FeaturesOffFixture : PlaywrightFixture
{
    protected override Dictionary<string, string?> FeatureFlags => new()
    {
        ["FeatureManagement:EditingFeatures"] = "false",
        ["FeatureManagement:Explanations"] = "false",
        ["FeatureManagement:Evaluation"] = "false"
    };
}
