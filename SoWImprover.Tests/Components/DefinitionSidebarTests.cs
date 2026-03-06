using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SoWImprover.Components.Shared;
using SoWImprover.Models;
using SoWImprover.Services;

namespace SoWImprover.Tests.Components;

public class DefinitionSidebarTests : BunitContext
{
    [Fact]
    public void NotReady_ShowsLoadingSpinner()
    {
        var definition = new GoodDefinition();
        Services.AddSingleton(definition);

        var cut = Render<DefinitionSidebar>();

        Assert.Contains("app-spinner", cut.Markup);
        Assert.Contains("Starting...", cut.Markup);
    }

    [Fact]
    public void NotReady_ShowsProgressMessage()
    {
        var definition = new GoodDefinition();
        definition.SetProgress("Loading corpus…");
        Services.AddSingleton(definition);

        var cut = Render<DefinitionSidebar>();

        Assert.Contains("Loading corpus", cut.Markup);
    }

    [Fact]
    public void Ready_ShowsDefinitionContent()
    {
        var definition = new GoodDefinition();
        var embedding = Substitute.For<IEmbeddingService>();
        var retriever = new EmbeddingRetriever([], [], embedding, 1);
        definition.SetReady(
            [new DefinedSection("Test Section", "This is guidance content.")],
            retriever, 0, 0);
        Services.AddSingleton(definition);

        var cut = Render<DefinitionSidebar>();

        Assert.Contains("Definition of good", cut.Markup);
        Assert.Contains("This is guidance content.", cut.Markup);
        Assert.DoesNotContain("app-spinner", cut.Markup);
    }
}
