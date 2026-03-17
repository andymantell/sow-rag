using Bunit;
using SoWImprover.Components.Shared;
using SoWImprover.Models;

namespace SoWImprover.Tests.Components;

public class ResultsPanelSummaryTests : BunitContext
{
    private static ImprovementResult MakeResult()
    {
        var section = new SectionResult
        {
            OriginalTitle = "Test Section",
            OriginalContent = "Original body text.",
            ImprovedContent = "Improved body text.",
            BaselineContent = "Baseline body text.",
            MatchedSection = "Matched",
        };
        return new ImprovementResult { Sections = [section] };
    }

    [Fact]
    public void SummaryBanner_RendersWhenSummaryProvided()
    {
        var result = MakeResult();

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false)
            .Add(x => x.EvaluationSummary, "Overall scores look good."));

        Assert.Contains("Evaluation Summary", cut.Markup);
        Assert.Contains("Overall scores look good.", cut.Markup);
        Assert.Contains("govuk-notification-banner", cut.Markup);
    }

    [Fact]
    public void SummaryBanner_HiddenWhenNoSummaryAndNotLoading()
    {
        var result = MakeResult();

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false)
            .Add(x => x.EvaluationSummary, (string?)null)
            .Add(x => x.SummaryLoading, false));

        Assert.DoesNotContain("Evaluation Summary", cut.Markup);
    }

    [Fact]
    public void SummaryBanner_ShowsSpinnerWhenLoading()
    {
        var result = MakeResult();

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false)
            .Add(x => x.SummaryLoading, true));

        Assert.Contains("app-badge-spinner", cut.Markup);
        Assert.Contains("Updating summary", cut.Markup);
    }

    [Fact]
    public void SummaryBanner_ShowsPartialIndicator()
    {
        var result = MakeResult();

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false)
            .Add(x => x.EvaluationSummary, "Partial results so far.")
            .Add(x => x.SummaryIsPartial, true)
            .Add(x => x.SectionsEvaluated, 2)
            .Add(x => x.TotalEvaluatingSections, 5));

        Assert.Contains("2 of 5 sections evaluated", cut.Markup);
        Assert.Contains("app-summary-partial", cut.Markup);
    }

    [Fact]
    public void SummaryBanner_NoPartialIndicator_WhenComplete()
    {
        var result = MakeResult();

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false)
            .Add(x => x.EvaluationSummary, "All done.")
            .Add(x => x.SummaryIsPartial, false)
            .Add(x => x.SectionsEvaluated, 5)
            .Add(x => x.TotalEvaluatingSections, 5));

        Assert.DoesNotContain("app-summary-partial", cut.Markup);
        Assert.DoesNotContain("sections evaluated", cut.Markup);
    }
}
