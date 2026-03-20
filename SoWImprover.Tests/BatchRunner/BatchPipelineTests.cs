using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SoWImprover.BatchRunner;
using SoWImprover.Data;
using SoWImprover.Models;
using SoWImprover.Services;

namespace SoWImprover.Tests.BatchRunner;

public class BatchPipelineTests
{
    [Fact]
    public async Task ProcessDocumentAsync_PersistsDocumentAndSections()
    {
        // Arrange — in-memory SQLite
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        connection.Open();
        var services = new ServiceCollection();
        services.AddDbContextFactory<SoWDbContext>(opts => opts.UseSqlite(connection));
        var sp = services.BuildServiceProvider();
        var dbFactory = sp.GetRequiredService<IDbContextFactory<SoWDbContext>>();
        await using (var db = await dbFactory.CreateDbContextAsync())
            await db.Database.EnsureCreatedAsync();

        // DocumentLoader needs IConfiguration + ILogger — create minimal fakes
        var loaderConfig = new ConfigurationBuilder().Build();
        var loader = Substitute.ForPartsOf<DocumentLoader>(
            loaderConfig, Substitute.For<ILogger<DocumentLoader>>());
        loader.ExtractTextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("# Introduction\n\nSome text here.");

        // SoWImproverService needs IChatService + ILogger
        var improver = Substitute.ForPartsOf<SoWImproverService>(
            Substitute.For<IChatService>(), Substitute.For<ILogger<SoWImproverService>>());
        var result = new ImprovementResult
        {
            Sections =
            [
                new SectionResult
                {
                    OriginalTitle = "Introduction",
                    OriginalContent = "Some text here.",
                    BaselineContent = "Improved baseline.",
                    ImprovedContent = "Improved with RAG.",
                    MatchedSection = "Introduction/Background",
                    Explanation = "Added structure.",
                    RetrievedContexts = ["chunk1", "chunk2"],
                    RetrievedScores = [0.72f, 0.55f],
                    DefinitionOfGoodText = "Good intro definition."
                }
            ],
            ChunksUsed = []
        };
        improver.ImproveAsync(
            Arg.Any<string>(), Arg.Any<GoodDefinition>(),
            Arg.Any<IProgress<string>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(result);

        var log = new ConsoleLogger(TextWriter.Null);
        var pipelineConfig = new ConfigurationBuilder().Build();
        var pipeline = new BatchPipeline(loader, improver, dbFactory, pipelineConfig, log);

        // Act
        var (docEntity, sectionResults) = await pipeline.ProcessDocumentAsync(
            "test.pdf", new GoodDefinition(), CancellationToken.None);

        // Assert — check returned entity
        Assert.Equal("test.pdf", docEntity.FileName);

        // Assert — check database
        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var doc = await verifyDb.Documents
            .Include(d => d.Sections)
                .ThenInclude(s => s.Versions)
            .FirstAsync();
        Assert.Equal("test.pdf", doc.FileName);
        Assert.Single(doc.Sections);

        var section = doc.Sections[0];
        Assert.Equal("Introduction/Background", section.MatchedSection);
        Assert.Equal("Improved with RAG.", section.ImprovedContent);
        Assert.Equal("Improved baseline.", section.BaselineContent);
        Assert.Equal("Added structure.", section.Explanation);
        Assert.Equal("[\"chunk1\",\"chunk2\"]", section.RetrievedContextsJson);
        Assert.Equal("Good intro definition.", section.DefinitionOfGoodText);

        // Version history
        Assert.Single(section.Versions);
        Assert.Equal(1, section.Versions[0].VersionNumber);
        Assert.Equal("Improved with RAG.", section.Versions[0].Content);
    }

    [Fact]
    public async Task ProcessDocumentAsync_WhenExtractionFails_ThrowsForCaller()
    {
        // Arrange
        var loaderConfig = new ConfigurationBuilder().Build();
        var loader = Substitute.ForPartsOf<DocumentLoader>(
            loaderConfig, Substitute.For<ILogger<DocumentLoader>>());
        loader.ExtractTextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("PDF corrupt"));

        var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        connection.Open();
        var services = new ServiceCollection();
        services.AddDbContextFactory<SoWDbContext>(opts => opts.UseSqlite(connection));
        var sp = services.BuildServiceProvider();
        var dbFactory = sp.GetRequiredService<IDbContextFactory<SoWDbContext>>();

        var log = new ConsoleLogger(TextWriter.Null);
        var pipelineConfig = new ConfigurationBuilder().Build();
        var pipeline = new BatchPipeline(
            loader,
            Substitute.ForPartsOf<SoWImproverService>(
                Substitute.For<IChatService>(), Substitute.For<ILogger<SoWImproverService>>()),
            dbFactory, pipelineConfig, log);

        // Act & Assert — pipeline throws, caller (Program.cs) catches and continues
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pipeline.ProcessDocumentAsync("bad.pdf", new GoodDefinition(), CancellationToken.None));
    }

    [Fact]
    public async Task ProcessDocumentAsync_UnrecognisedSection_PersistsWithNullFields()
    {
        // Arrange
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        connection.Open();
        var services = new ServiceCollection();
        services.AddDbContextFactory<SoWDbContext>(opts => opts.UseSqlite(connection));
        var sp = services.BuildServiceProvider();
        var dbFactory = sp.GetRequiredService<IDbContextFactory<SoWDbContext>>();
        await using (var db = await dbFactory.CreateDbContextAsync())
            await db.Database.EnsureCreatedAsync();

        var loaderConfig = new ConfigurationBuilder().Build();
        var loader = Substitute.ForPartsOf<DocumentLoader>(
            loaderConfig, Substitute.For<ILogger<DocumentLoader>>());
        loader.ExtractTextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("Some plain text.");

        var improver = Substitute.ForPartsOf<SoWImproverService>(
            Substitute.For<IChatService>(), Substitute.For<ILogger<SoWImproverService>>());
        var result = new ImprovementResult
        {
            Sections =
            [
                new SectionResult
                {
                    OriginalTitle = "Random Heading",
                    OriginalContent = "Body text.",
                    Unrecognised = true
                }
            ],
            ChunksUsed = []
        };
        improver.ImproveAsync(
            Arg.Any<string>(), Arg.Any<GoodDefinition>(),
            Arg.Any<IProgress<string>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(result);

        var log = new ConsoleLogger(TextWriter.Null);
        var pipelineConfig = new ConfigurationBuilder().Build();
        var pipeline = new BatchPipeline(loader, improver, dbFactory, pipelineConfig, log);

        // Act
        await pipeline.ProcessDocumentAsync("unrecognised.pdf", new GoodDefinition(), CancellationToken.None);

        // Assert
        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var doc = await verifyDb.Documents.Include(d => d.Sections).FirstAsync();
        Assert.Single(doc.Sections);

        var section = doc.Sections[0];
        Assert.True(section.Unrecognised);
        Assert.Null(section.MatchedSection);
        Assert.Null(section.RetrievedContextsJson);
        // Version should contain original content since ImprovedContent is null
        Assert.Equal("Body text.", (await verifyDb.Set<SectionVersionEntity>().FirstAsync()).Content);
    }
}
