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
        var result = await _service.ImproveAsync("Some body text.", definition);

        Assert.Single(result.Sections);
        Assert.True(result.Sections[0].Unrecognised);
        Assert.Null(result.Sections[0].ImprovedContent);
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
                    2 => "Improved content here.",                 // improvement
                    3 => "- Better structure\n- Clearer language", // explanation
                    _ => ""
                };
            });

        var definition = CreateReadyDefinition();
        var result = await _service.ImproveAsync("Some body text.", definition);

        Assert.Single(result.Sections);
        Assert.False(result.Sections[0].Unrecognised);
        Assert.Equal("Improved content here.", result.Sections[0].ImprovedContent);
        Assert.Contains("Better structure", result.Sections[0].Explanation);
    }

    [Fact]
    public async Task ImproveAsync_MalformedMatchingJson_TreatsAllAsUnrecognised()
    {
        _chat.CompleteAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("not valid json at all");

        var definition = CreateReadyDefinition();
        var result = await _service.ImproveAsync("Some body text.", definition);

        Assert.Single(result.Sections);
        Assert.True(result.Sections[0].Unrecognised);
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
                    2 => "```markdown\nImproved content.\n```",
                    3 => "- Change one",
                    _ => ""
                };
            });

        var definition = CreateReadyDefinition();
        var result = await _service.ImproveAsync("Some body text.", definition);

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
                    2 => "# Hallucinated Heading\n\nActual content here.",
                    3 => "- Change",
                    _ => ""
                };
            });

        var definition = CreateReadyDefinition();
        var result = await _service.ImproveAsync("Some body text.", definition);

        Assert.Equal("Actual content here.", result.Sections[0].ImprovedContent);
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
}
