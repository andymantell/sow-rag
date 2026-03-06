using NSubstitute;
using SoWImprover.Models;
using SoWImprover.Services;

namespace SoWImprover.Tests.Unit;

public class GoodDefinitionTests
{
    [Fact]
    public void NewInstance_IsNotReady()
    {
        var definition = new GoodDefinition();

        Assert.False(definition.IsReady);
        Assert.Empty(definition.Sections);
        Assert.Equal("", definition.MarkdownContent);
        Assert.Null(definition.Retriever);
        Assert.Equal(0, definition.DocumentCount);
        Assert.Equal(0, definition.ChunkCount);
    }

    [Fact]
    public void SetReady_SetsAllProperties()
    {
        var definition = new GoodDefinition();
        var embedding = Substitute.For<IEmbeddingService>();
        var retriever = new EmbeddingRetriever([], [], embedding, 1);

        definition.SetReady(
            [new DefinedSection("Scope", "Guidance."), new DefinedSection("Budget", "Budget guidance.")],
            retriever, 3, 10);

        Assert.True(definition.IsReady);
        Assert.Equal(2, definition.Sections.Count);
        Assert.Equal("Scope", definition.Sections[0].Name);
        Assert.Equal("Budget", definition.Sections[1].Name);
        Assert.Same(retriever, definition.Retriever);
        Assert.Equal(3, definition.DocumentCount);
        Assert.Equal(10, definition.ChunkCount);
    }

    [Fact]
    public void SetReady_GeneratesMarkdownContent()
    {
        var definition = new GoodDefinition();
        var embedding = Substitute.For<IEmbeddingService>();
        var retriever = new EmbeddingRetriever([], [], embedding, 1);

        definition.SetReady(
            [new DefinedSection("Scope of Work", "Define scope clearly.")],
            retriever, 0, 0);

        Assert.Contains("## Scope of Work", definition.MarkdownContent);
        Assert.Contains("Define scope clearly.", definition.MarkdownContent);
    }

    [Fact]
    public void SetProgress_UpdatesMessage()
    {
        var definition = new GoodDefinition();

        definition.SetProgress("Loading corpus…");

        Assert.Equal("Loading corpus…", definition.ProgressMessage);
    }

    [Fact]
    public void SetProgress_FiresOnChanged()
    {
        var definition = new GoodDefinition();
        var fired = false;
        definition.OnChanged += () => fired = true;

        definition.SetProgress("test");

        Assert.True(fired);
    }

    [Fact]
    public void SetReady_FiresOnChanged()
    {
        var definition = new GoodDefinition();
        var fired = false;
        definition.OnChanged += () => fired = true;
        var embedding = Substitute.For<IEmbeddingService>();
        var retriever = new EmbeddingRetriever([], [], embedding, 1);

        definition.SetReady([], retriever, 0, 0);

        Assert.True(fired);
    }
}
