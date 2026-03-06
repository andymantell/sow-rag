using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SoWImprover.Services;

namespace SoWImprover.Tests.Services;

public class DefinitionBuilderTests
{
    private readonly IChatService _chat = Substitute.For<IChatService>();
    private readonly DefinitionBuilder _builder;

    public DefinitionBuilderTests()
    {
        _builder = new DefinitionBuilder(_chat, NullLogger<DefinitionBuilder>.Instance);
    }

    [Fact]
    public async Task BuildDefinitionAsync_AnalysesThenSynthesises()
    {
        // Every call returns a simple response
        _chat.CompleteAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("Analysis or synthesis result.");

        var documents = new List<(string FileName, string Text)>
        {
            ("doc1.pdf", "Sample SoW content for document one.")
        };
        var progress = new List<string>();

        var sections = await _builder.BuildDefinitionAsync(
            documents, msg => progress.Add(msg));

        // Should produce 15 canonical sections with expected names
        Assert.Equal(15, sections.Count);
        Assert.All(sections, s => Assert.False(string.IsNullOrWhiteSpace(s.Content)));
        Assert.Contains(sections, s => s.Name == "Scope of Work");
        Assert.Contains(sections, s => s.Name == "Deliverables");
        Assert.Contains(sections, s => s.Name == "Acceptance Criteria");

        // Should have reported progress for analysis + synthesis
        Assert.Contains(progress, p => p.Contains("Analysing document"));
        Assert.Contains(progress, p => p.Contains("Writing definition"));
    }

    [Fact]
    public async Task BuildDefinitionAsync_CodeFencesStrippedFromResponses()
    {
        _chat.CompleteAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("```markdown\nClean content.\n```");

        var documents = new List<(string FileName, string Text)>
        {
            ("doc1.pdf", "Content.")
        };

        var sections = await _builder.BuildDefinitionAsync(
            documents, _ => { });

        Assert.All(sections, s =>
        {
            Assert.DoesNotContain("```", s.Content);
            Assert.Equal("Clean content.", s.Content);
        });
    }

    [Fact]
    public async Task BuildDefinitionAsync_MultipleDocuments_AllAnalysed()
    {
        var analysisCallCount = 0;
        _chat.CompleteAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                analysisCallCount++;
                return $"Response {analysisCallCount}";
            });

        var documents = new List<(string FileName, string Text)>
        {
            ("doc1.pdf", "Content one."),
            ("doc2.pdf", "Content two."),
            ("doc3.pdf", "Content three.")
        };

        var sections = await _builder.BuildDefinitionAsync(documents, _ => { });

        // 3 analysis calls + 15 synthesis calls = 18 total
        Assert.Equal(18, analysisCallCount);
        Assert.Equal(15, sections.Count);
    }
}
