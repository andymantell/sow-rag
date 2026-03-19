using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SoWImprover.BatchRunner;
using SoWImprover.Data;
using SoWImprover.Models;
using SoWImprover.Services;

namespace SoWImprover.Tests.BatchRunner;

public class EvaluationRunnerTests
{
    [Fact]
    public async Task EvaluateDocumentAsync_PersistsScoresAndSummary()
    {
        // Arrange — in-memory SQLite with a pre-persisted document
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        connection.Open();
        var services = new ServiceCollection();
        services.AddDbContextFactory<SoWDbContext>(opts => opts.UseSqlite(connection));
        var sp = services.BuildServiceProvider();
        var dbFactory = sp.GetRequiredService<IDbContextFactory<SoWDbContext>>();
        await using (var setupDb = await dbFactory.CreateDbContextAsync())
            await setupDb.Database.EnsureCreatedAsync();

        // Pre-persist a document with one section
        var docId = Guid.NewGuid();
        await using (var seedDb = await dbFactory.CreateDbContextAsync())
        {
            var seedDoc = new DocumentEntity
            {
                Id = docId, FileName = "test.pdf",
                OriginalText = "original", UploadedAt = DateTime.UtcNow
            };
            seedDoc.Sections.Add(new SectionEntity
            {
                DocumentId = docId, SectionIndex = 0,
                OriginalTitle = "Scope", OriginalContent = "Original scope",
                BaselineContent = "Baseline scope", ImprovedContent = "RAG scope",
                MatchedSection = "Scope of Work",
                RetrievedContextsJson = "[\"ctx1\"]",
                DefinitionOfGoodText = "Good scope"
            });
            seedDb.Documents.Add(seedDoc);
            await seedDb.SaveChangesAsync();
        }

        // Load entity for the test
        DocumentEntity entity;
        await using (var loadDb = await dbFactory.CreateDbContextAsync())
            entity = await loadDb.Documents.Include(d => d.Sections).FirstAsync(d => d.Id == docId);

        // Stub evaluation service — EvaluationService has primary constructor requiring
        // (IConfiguration, ILogger<EvaluationService>, GpuMemoryManager)
        var config = new ConfigurationBuilder().Build();
        var gpuMemory = Substitute.ForPartsOf<GpuMemoryManager>(
            config, Substitute.For<ILogger<GpuMemoryManager>>());
        var evaluator = Substitute.ForPartsOf<EvaluationService>(
            config, Substitute.For<ILogger<EvaluationService>>(), gpuMemory);
        var scores = new EvaluationService.SectionScores
        {
            RagQualityScore = 4,
            BaselineQualityScore = 3,
            NoiseSensitivityScore = 0.1
        };
        evaluator.EvaluateStreamingAsync(
                Arg.Any<List<EvaluationService.SectionInput>>(), Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable((0, scores)));

        var summaryService = Substitute.For<IEvaluationSummaryService>();
        summaryService.GenerateSummaryAsync(
                Arg.Any<List<SectionSummaryInput>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("Test summary");

        var sectionResults = new List<SectionResult>
        {
            new()
            {
                OriginalContent = "Original scope",
                BaselineContent = "Baseline scope",
                ImprovedContent = "RAG scope",
                MatchedSection = "Scope of Work",
                RetrievedContexts = ["ctx1"],
                RetrievedScores = [0.7f],
                DefinitionOfGoodText = "Good scope"
            }
        };

        var log = new ConsoleLogger(TextWriter.Null);
        var runner = new EvaluationRunner(evaluator, summaryService, dbFactory, log);

        // Act
        await runner.EvaluateDocumentAsync(entity, sectionResults, CancellationToken.None);

        // Assert
        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var doc = await verifyDb.Documents.Include(d => d.Sections).FirstAsync();
        Assert.Equal("Test summary", doc.EvaluationSummary);
        Assert.Equal(4, doc.Sections[0].RagQualityScore);
        Assert.Equal(3, doc.Sections[0].BaselineQualityScore);
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(params T[] items)
    {
        foreach (var item in items)
        {
            yield return item;
            await Task.CompletedTask;
        }
    }
}
