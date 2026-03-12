using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SoWImprover.Models;
using SoWImprover.Services;

namespace SoWImprover.Tests.Services;

public class SoWImproverServiceTests
{
    private readonly IChatService _chat = Substitute.For<IChatService>();
    private readonly SoWImproverService _service;

    public SoWImproverServiceTests()
    {
        _service = new SoWImproverService(_chat, NullLogger<SoWImproverService>.Instance);
    }

    [Fact]
    public async Task ImproveAsync_UnrecognisedSections_PassedThrough()
    {
        // Matching response: all sections map to null (unrecognised)
        _chat.CompleteAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("{\"Introduction\": null}");

        var definition = CreateReadyDefinition();
        var result = await _service.ImproveAsync("The supplier shall provide all necessary resources and personnel to deliver the services described in this statement of work.", definition);

        Assert.Single(result.Sections);
        Assert.True(result.Sections[0].Unrecognised);
        Assert.Null(result.Sections[0].ImprovedContent);
        Assert.Equal("Introduction", result.Sections[0].OriginalTitle);
        Assert.Equal("The supplier shall provide all necessary resources and personnel to deliver the services described in this statement of work.", result.Sections[0].OriginalContent);
    }

    [Fact]
    public async Task ImproveAsync_MatchedSection_GetsImprovedAndExplained()
    {
        var callCount = 0;
        _chat.CompleteAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                callCount++;
                return callCount switch
                {
                    1 => "{\"Introduction\": \"Scope of Work\"}", // matching
                    2 => "Baseline.",                              // baseline (no RAG)
                    3 => "Improved content here.",                 // improvement
                    4 => "- Better structure\n- Clearer language", // explanation
                    _ => ""
                };
            });

        var definition = CreateReadyDefinition();
        var result = await _service.ImproveAsync("The supplier shall provide all necessary resources and personnel to deliver the services described in this statement of work.", definition);

        Assert.Single(result.Sections);
        Assert.False(result.Sections[0].Unrecognised);
        Assert.Equal("Improved content here.", result.Sections[0].ImprovedContent);
        Assert.Equal("Scope of Work", result.Sections[0].MatchedSection);
        Assert.Contains("Better structure", result.Sections[0].Explanation);
    }

    [Fact]
    public async Task ImproveAsync_MatchedSection_GeneratesBaselineWithoutRag()
    {
        var callCount = 0;
        _chat.CompleteAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                callCount++;
                return callCount switch
                {
                    1 => "{\"Introduction\": \"Scope of Work\"}", // matching
                    2 => "Baseline content here.",                  // baseline (no RAG)
                    3 => "RAG improved content here.",              // improvement (with RAG)
                    4 => "- Better structure",                      // explanation
                    _ => ""
                };
            });

        var definition = CreateReadyDefinition();
        var result = await _service.ImproveAsync("The supplier shall provide all necessary resources and personnel to deliver the services described in this statement of work.", definition);

        Assert.Single(result.Sections);
        Assert.Equal("Baseline content here.", result.Sections[0].BaselineContent);
        Assert.Equal("RAG improved content here.", result.Sections[0].ImprovedContent);
    }

    [Fact]
    public async Task ImproveAsync_MalformedMatchingJson_TreatsAllAsUnrecognised()
    {
        _chat.CompleteAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("not valid json at all");

        var definition = CreateReadyDefinition();
        var result = await _service.ImproveAsync("The supplier shall provide all necessary resources and personnel to deliver the services described in this statement of work.", definition);

        Assert.Single(result.Sections);
        Assert.True(result.Sections[0].Unrecognised);
        Assert.Equal("Introduction", result.Sections[0].OriginalTitle);
        Assert.Equal("The supplier shall provide all necessary resources and personnel to deliver the services described in this statement of work.", result.Sections[0].OriginalContent);
    }

    [Fact]
    public async Task ImproveAsync_CodeFenceInResponse_Stripped()
    {
        var callCount = 0;
        _chat.CompleteAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                callCount++;
                return callCount switch
                {
                    1 => "```json\n{\"Introduction\": \"Scope of Work\"}\n```",
                    2 => "Baseline.",
                    3 => "```markdown\nImproved content.\n```",
                    4 => "- Change one",
                    _ => ""
                };
            });

        var definition = CreateReadyDefinition();
        var result = await _service.ImproveAsync("The supplier shall provide all necessary resources and personnel to deliver the services described in this statement of work.", definition);

        Assert.Equal("Improved content.", result.Sections[0].ImprovedContent);
    }

    [Fact]
    public async Task ImproveAsync_LeadingHeadingInImprovement_Stripped()
    {
        var callCount = 0;
        _chat.CompleteAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                callCount++;
                return callCount switch
                {
                    1 => "{\"Introduction\": \"Scope of Work\"}",
                    2 => "Baseline.",
                    3 => "# Hallucinated Heading\n\nActual content here.",
                    4 => "- Change",
                    _ => ""
                };
            });

        var definition = CreateReadyDefinition();
        var result = await _service.ImproveAsync("The supplier shall provide all necessary resources and personnel to deliver the services described in this statement of work.", definition);

        Assert.Equal("Actual content here.", result.Sections[0].ImprovedContent);
    }

    [Fact]
    public async Task ImproveAsync_MultipleSections_MatchedAndUnrecognised()
    {
        var callCount = 0;
        _chat.CompleteAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                callCount++;
                return callCount switch
                {
                    1 => "{\"SCOPE OF WORK\": \"Scope of Work\", \"RANDOM HEADING\": null}",
                    2 => "Baseline.",
                    3 => "Improved scope content.",
                    4 => "- Better clarity",
                    _ => ""
                };
            });

        var definition = CreateReadyDefinition();
        var text = "SCOPE OF WORK\nThe contractor shall deliver all required outputs and milestones as specified in this agreement within the agreed timescales and budget constraints.\n\nRANDOM HEADING\nSome unknown content that is not long enough to trigger improvement.";
        var result = await _service.ImproveAsync(text, definition);

        Assert.Equal(2, result.Sections.Count);

        // First section: matched and improved
        Assert.Equal("SCOPE OF WORK", result.Sections[0].OriginalTitle);
        Assert.False(result.Sections[0].Unrecognised);
        Assert.Equal("Improved scope content.", result.Sections[0].ImprovedContent);
        Assert.Equal("Scope of Work", result.Sections[0].MatchedSection);
        Assert.Contains("Better clarity", result.Sections[0].Explanation);

        // Second section: unrecognised, passed through
        Assert.Equal("RANDOM HEADING", result.Sections[1].OriginalTitle);
        Assert.True(result.Sections[1].Unrecognised);
        Assert.Null(result.Sections[1].ImprovedContent);
        Assert.Equal("Some unknown content that is not long enough to trigger improvement.", result.Sections[1].OriginalContent);
    }

    [Fact]
    public async Task ImproveAsync_ReportsProgress()
    {
        var callCount = 0;
        _chat.CompleteAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                callCount++;
                return callCount switch
                {
                    1 => "{\"Introduction\": \"Scope of Work\"}",
                    2 => "Baseline.",
                    3 => "Improved.",
                    4 => "- Change",
                    _ => ""
                };
            });

        var definition = CreateReadyDefinition();
        var progressMessages = new List<string>();
        var progress = new SyncProgress<string>(msg => progressMessages.Add(msg));

        await _service.ImproveAsync("The supplier shall provide all necessary resources and personnel to deliver the services described in this statement of work.", definition, progress);

        Assert.Contains(progressMessages, p => p.Contains("Matching sections"));
        Assert.Contains(progressMessages, p => p.Contains("Improving"));
    }

    [Fact]
    public async Task ImproveAsync_PopulatesChunksUsed()
    {
        var callCount = 0;
        _chat.CompleteAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                callCount++;
                return callCount switch
                {
                    1 => "{\"Introduction\": \"Scope of Work\"}",
                    2 => "Baseline.",
                    3 => "Improved.",
                    4 => "- Change",
                    _ => ""
                };
            });

        var definition = CreateReadyDefinition();
        var result = await _service.ImproveAsync("The supplier shall provide all necessary resources and personnel to deliver the services described in this statement of work.", definition);

        Assert.NotEmpty(result.ChunksUsed);
        Assert.Equal("example.pdf", result.ChunksUsed[0].SourceFile);
        Assert.False(string.IsNullOrWhiteSpace(result.ChunksUsed[0].Snippet));
    }

    [Fact]
    public async Task ImproveAsync_LongChunkText_TruncatesSnippet()
    {
        var callCount = 0;
        _chat.CompleteAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                callCount++;
                return callCount switch
                {
                    1 => "{\"Introduction\": \"Scope of Work\"}",
                    2 => "Baseline.",
                    3 => "Improved.",
                    4 => "- Change",
                    _ => ""
                };
            });

        var definition = CreateReadyDefinitionWithLongChunk();
        var result = await _service.ImproveAsync("The supplier shall provide all necessary resources and personnel to deliver the services described in this statement of work.", definition);

        Assert.NotEmpty(result.ChunksUsed);
        Assert.True(result.ChunksUsed[0].Snippet.Length <= 201); // 200 chars + "…"
        Assert.EndsWith("…", result.ChunksUsed[0].Snippet);
    }

    private static GoodDefinition CreateReadyDefinitionWithLongChunk()
    {
        var embedding = Substitute.For<IEmbeddingService>();
        embedding.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new float[] { 1, 0, 0 });

        var longText = new string('A', 300);
        var chunks = new List<DocumentChunk>
        {
            new() { SourceFile = "example.pdf", Text = longText, ChunkIndex = 0 }
        };
        var vectors = new float[][] { [1, 0, 0] };
        var retriever = new EmbeddingRetriever(chunks, vectors, embedding, topK: 1);

        var definition = new GoodDefinition();
        definition.SetReady(
            [new DefinedSection("Scope of Work", "Define the scope clearly.")],
            retriever, 1, 1);
        return definition;
    }

    private static GoodDefinition CreateReadyDefinition()
    {
        var embedding = Substitute.For<IEmbeddingService>();
        embedding.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new float[] { 1, 0, 0 });

        var chunks = new List<DocumentChunk>
        {
            new() { SourceFile = "example.pdf", Text = "example chunk", ChunkIndex = 0 }
        };
        var vectors = new float[][] { [1, 0, 0] };
        var retriever = new EmbeddingRetriever(chunks, vectors, embedding, topK: 1);

        var definition = new GoodDefinition();
        definition.SetReady(
            [new DefinedSection("Scope of Work", "Define the scope clearly.")],
            retriever, 1, 1);
        return definition;
    }

    /// <summary>
    /// IProgress that invokes the callback synchronously, unlike
    /// <see cref="Progress{T}"/> which posts via SynchronizationContext.
    /// </summary>
    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
