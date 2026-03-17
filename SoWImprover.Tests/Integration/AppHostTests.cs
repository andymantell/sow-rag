using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using SoWImprover.Data;
using SoWImprover.Models;
using SoWImprover.Services;

namespace SoWImprover.Tests.Integration;

/// <summary>
/// Verifies the app can start and serve the home page.
/// Replaces external dependencies (LLM, Python, corpus) with stubs.
/// </summary>
public class AppHostTests : IClassFixture<AppHostTests.SoWWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AppHostTests(SoWWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HomePage_Returns200()
    {
        var response = await _client.GetAsync("/");
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task HomePage_ContainsExpectedContent()
    {
        var response = await _client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Improve a statement of work", html);
    }

    public class SoWWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Avoid UseStaticWebAssets() which fails with Git Bash path mismatches
            builder.UseEnvironment("Production");
            builder.ConfigureServices(services =>
            {
                // Remove the real BackgroundService so it doesn't try to load corpus
                var hostedServiceDescriptor = services.FirstOrDefault(
                    d => d.ImplementationType == typeof(DefinitionGeneratorService));
                if (hostedServiceDescriptor is not null)
                    services.Remove(hostedServiceDescriptor);

                // Replace FoundryClientFactory with a stub (chat/embedding services depend on it)
                RemoveService<FoundryClientFactory>(services);
                var stubConfig = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
                services.AddSingleton(new FoundryClientFactory(
                    stubConfig,
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<FoundryClientFactory>.Instance));

                // Stub IChatService
                var chat = Substitute.For<IChatService>();
                chat.CompleteAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                    .Returns("Stubbed response.");
                RemoveService<IChatService>(services);
                services.AddSingleton(chat);

                // Stub IEmbeddingService
                var embedding = Substitute.For<IEmbeddingService>();
                RemoveService<IEmbeddingService>(services);
                services.AddSingleton(embedding);

                // Stub IEvaluationSummaryService (no evaluation endpoint in test config)
                var summaryService = Substitute.For<IEvaluationSummaryService>();
                summaryService.GenerateSummaryAsync(
                    Arg.Any<List<SectionSummaryInput>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                    .Returns("");
                RemoveService<IEvaluationSummaryService>(services);
                services.AddSingleton(summaryService);

                // Replace DB with in-memory SQLite
                RemoveService<IDbContextFactory<SoWDbContext>>(services);
                var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
                connection.Open();
                services.AddDbContextFactory<SoWDbContext>(opts => opts.UseSqlite(connection));

                // Ensure DB schema exists
                var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SoWDbContext>>();
                using var db = factory.CreateDbContext();
                db.Database.EnsureCreated();

                // Pre-populate GoodDefinition as ready
                RemoveService<GoodDefinition>(services);
                var definition = new GoodDefinition();
                var retriever = new EmbeddingRetriever([], [], embedding, 1);
                definition.SetReady(
                    [new DefinedSection("Scope of Work", "Test guidance.")],
                    retriever, 0, 0);
                services.AddSingleton(definition);
            });
        }

        private static void RemoveService<T>(IServiceCollection services)
        {
            var descriptors = services.Where(d => d.ServiceType == typeof(T)).ToList();
            foreach (var d in descriptors) services.Remove(d);
        }
    }
}
