using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SoWImprover.Models;
using SoWImprover.Services;

namespace SoWImprover.Tests.Integration;

/// <summary>
/// Integration test verifying the full definition-building pipeline
/// (analyse documents → synthesise sections → set GoodDefinition ready)
/// with a stubbed IChatService so no real LLM is needed.
/// </summary>
public class DefinitionPipelineTests
{
    [Fact]
    public async Task FullPipeline_BuildsDefinitionAndSetsReady()
    {
        // Arrange
        var chat = Substitute.For<IChatService>();
        chat.CompleteAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("Stubbed LLM analysis/synthesis content.");

        var builder = new DefinitionBuilder(chat, NullLogger<DefinitionBuilder>.Instance);

        var embedding = Substitute.For<IEmbeddingService>();
        embedding.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new float[] { 0.1f, 0.2f, 0.3f });
        embedding.EmbedBatchAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var texts = ci.ArgAt<IReadOnlyList<string>>(0);
                return texts.Select(_ => new float[] { 0.1f, 0.2f, 0.3f }).ToArray();
            });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Docs:ChunkSize"] = "50",
                ["Docs:ChunkOverlap"] = "5",
                ["Docs:TopKChunks"] = "3",
            })
            .Build();

        var definition = new GoodDefinition();
        var progress = new List<string>();

        // Act — simulate what DefinitionGeneratorService does
        var documents = new List<(string FileName, string Text)>
        {
            ("good-sow-1.pdf", "This is a sample statement of work with scope, deliverables, and timelines."),
            ("good-sow-2.pdf", "Another example SoW covering requirements, roles, and acceptance criteria.")
        };

        var sections = await builder.BuildDefinitionAsync(
            documents, msg => progress.Add(msg));

        // Build chunks manually (simulating DocumentLoader)
        var loader = new DocumentLoader(config, NullLogger<DocumentLoader>.Instance);
        var allChunks = new List<DocumentChunk>();
        foreach (var (fileName, text) in documents)
            allChunks.AddRange(loader.ChunkText(text, fileName));

        var vectors = await embedding.EmbedBatchAsync(
            allChunks.Select(c => c.Text).ToArray());

        var retriever = new EmbeddingRetriever(allChunks, vectors, embedding, 3);
        definition.SetReady(sections, retriever, retriever.DocumentCount, retriever.ChunkCount);

        // Assert
        Assert.True(definition.IsReady);
        Assert.Equal(15, definition.Sections.Count);
        Assert.NotNull(definition.Retriever);
        Assert.Equal(2, definition.DocumentCount);
        Assert.True(definition.ChunkCount > 0);
        Assert.False(string.IsNullOrWhiteSpace(definition.MarkdownContent));

        // Progress was reported
        Assert.True(progress.Count > 0);
    }

    [Fact]
    public async Task FullPipeline_ImproveAsync_WithStubbed_ProducesResults()
    {
        // Arrange — set up a full pipeline with stubbed chat + embeddings
        var callCount = 0;
        var chat = Substitute.For<IChatService>();
        chat.CompleteAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                callCount++;
                var prompt = ci.ArgAt<string>(0);
                // Section matching call
                if (prompt.Contains("classifying sections"))
                    return "{\"Introduction\": \"Scope of Work\"}";
                // Improvement or explanation
                if (prompt.Contains("expert editor"))
                    return "Improved professional content.";
                return "- Clear improvements made.";
            });

        var embedding = Substitute.For<IEmbeddingService>();
        embedding.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new float[] { 0.5f, 0.5f });

        var chunks = new List<DocumentChunk>
        {
            new() { SourceFile = "ref.pdf", Text = "Reference content", ChunkIndex = 0 }
        };
        var vectors = new float[][] { [0.5f, 0.5f] };
        var retriever = new EmbeddingRetriever(chunks, vectors, embedding, 1);

        var definition = new GoodDefinition();
        definition.SetReady(
            [new DefinedSection("Scope of Work", "Define scope clearly.")],
            retriever, 1, 1);

        var service = new SoWImproverService(chat,
            NullLogger<SoWImproverService>.Instance);

        // Act
        var result = await service.ImproveAsync("Introduction text here.", definition);

        // Assert
        Assert.Single(result.Sections);
        Assert.False(result.Sections[0].Unrecognised);
        Assert.Equal("Improved professional content.", result.Sections[0].ImprovedContent);
    }
}
