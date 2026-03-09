using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SoWImprover.Components.Shared;
using SoWImprover.Models;

namespace SoWImprover.Tests.Components;

public class ResultsPanelTests : BunitContext
{
    private static ImprovementResult MakeResult(
        Action<SectionResult>? configure = null, bool unrecognised = false)
    {
        var section = new SectionResult
        {
            OriginalTitle = "Test Section",
            OriginalContent = "Original body text.",
            ImprovedContent = "Improved body text.",
            BaselineContent = "Baseline body text.",
            MatchedSection = "Matched",
            Unrecognised = unrecognised,
        };
        configure?.Invoke(section);
        return new ImprovementResult { Sections = [section] };
    }

    // ── Score badge rendering ────────────────────────────────────

    [Fact]
    public void ScoreBadges_OriginalQuality_RendersWhenPresent()
    {
        var result = MakeResult(s => s.OriginalQualityScore = 3);

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false));

        Assert.Contains("Quality:", cut.Markup);
        Assert.Contains("3/5", cut.Markup);
    }

    [Fact]
    public void ScoreBadges_BaselineQualityAndFaithfulness_RenderInMiddleColumn()
    {
        var result = MakeResult(s =>
        {
            s.BaselineQualityScore = 4;
            s.BaselineFaithfulnessScore = 0.85;
        });

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false));

        Assert.Contains("4/5", cut.Markup);
        Assert.Contains("Faithfulness:", cut.Markup);
        Assert.Contains("0.85", cut.Markup);
    }

    [Fact]
    public void ScoreBadges_RagAllScores_RenderInRightColumn()
    {
        var result = MakeResult(s =>
        {
            s.RagQualityScore = 5;
            s.RagFaithfulnessScore = 0.92;
            s.ContextPrecisionScore = 0.78;
        });

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false));

        Assert.Contains("5/5", cut.Markup);
        Assert.Contains("0.92", cut.Markup);
        Assert.Contains("Context precision:", cut.Markup);
        Assert.Contains("0.78", cut.Markup);
    }

    [Fact]
    public void ScoreBadges_NoScores_NoBadgesRendered()
    {
        var result = MakeResult();

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false));

        Assert.DoesNotContain("app-score-badge", cut.Markup);
    }

    // ── Tooltip rendering ────────────────────────────────────────

    [Fact]
    public void ScoreBadges_QualityTooltip_ExplainsRubric()
    {
        var result = MakeResult(s => s.OriginalQualityScore = 3);

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false));

        Assert.Contains("rubric", cut.Markup);
        Assert.Contains("role=\"tooltip\"", cut.Markup);
    }

    [Fact]
    public void ScoreBadges_FaithfulnessTooltip_ExplainsFidelity()
    {
        var result = MakeResult(s => s.BaselineFaithfulnessScore = 0.9);

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false));

        Assert.Contains("faithful to the original", cut.Markup);
    }

    [Fact]
    public void ScoreBadges_ContextPrecisionTooltip_ExplainsRetrieval()
    {
        var result = MakeResult(s => s.ContextPrecisionScore = 0.7);

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false));

        Assert.Contains("retrieved reference chunks", cut.Markup);
    }

    [Fact]
    public void ScoreBadges_InfoButton_HasAriaLabel()
    {
        var result = MakeResult(s => s.RagQualityScore = 4);

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false));

        Assert.Contains("aria-label=\"About quality score\"", cut.Markup);
        Assert.Contains("app-score-info-btn", cut.Markup);
    }

    // ── Evaluation spinner ───────────────────────────────────────

    [Fact]
    public void EvalSpinner_ShowsOuroborosWhenEvaluating()
    {
        var result = MakeResult();
        var evaluating = new HashSet<int> { 0 };

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false)
            .Add(x => x.EvaluatingSections, evaluating)
            .Add(x => x.EvaluationMessage, "Scoring section 1 of 3…"));

        Assert.Contains("app-ouroboros", cut.Markup);
        Assert.Contains("ouroboros.png", cut.Markup);
        Assert.Contains("Scoring section 1 of 3", cut.Markup);
    }

    [Fact]
    public void EvalSpinner_DefaultMessage_WhenNoEvaluationMessage()
    {
        var result = MakeResult();
        var evaluating = new HashSet<int> { 0 };

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false)
            .Add(x => x.EvaluatingSections, evaluating));

        Assert.Contains("Evaluating", cut.Markup);
    }

    [Fact]
    public void EvalSpinner_NotShown_WhenNotEvaluating()
    {
        var result = MakeResult();

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false)
            .Add(x => x.EvaluatingSections, new HashSet<int>()));

        Assert.DoesNotContain("app-ouroboros", cut.Markup);
        Assert.DoesNotContain("app-eval-spinner", cut.Markup);
    }

    [Fact]
    public void EvalSpinner_OuroborosImage_HasAccessibilityAttributes()
    {
        var result = MakeResult();
        var evaluating = new HashSet<int> { 0 };

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false)
            .Add(x => x.EvaluatingSections, evaluating));

        Assert.Contains("aria-hidden=\"true\"", cut.Markup);
        Assert.Contains("aria-live=\"polite\"", cut.Markup);
    }

    // ── Spinner shown on all three columns ───────────────────────

    [Fact]
    public void EvalSpinner_AppearsInAllThreeColumns()
    {
        var result = MakeResult();
        var evaluating = new HashSet<int> { 0 };

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false)
            .Add(x => x.EvaluatingSections, evaluating));

        // Count occurrences of the ouroboros image — should be 3 (one per column)
        var markup = cut.Markup;
        var count = CountOccurrences(markup, "app-ouroboros");
        Assert.Equal(3, count);
    }

    // ── Unrecognised sections don't show scores ──────────────────

    [Fact]
    public void UnrecognisedSection_NoScoreBadges()
    {
        var result = MakeResult(s =>
        {
            s.ImprovedContent = null;
            s.BaselineContent = null;
        }, unrecognised: true);

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false));

        Assert.DoesNotContain("app-score-badge", cut.Markup);
    }

    // ── Suppressed sections don't show scores ────────────────────

    [Fact]
    public void SuppressedSection_NoScoreBadges()
    {
        var result = MakeResult(s => s.OriginalQualityScore = 3);
        var suppressed = new HashSet<int> { 0 };

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false)
            .Add(x => x.SuppressedSections, suppressed));

        Assert.DoesNotContain("app-score-badge", cut.Markup);
    }

    // ── Score formatting ─────────────────────────────────────────

    [Fact]
    public void FaithfulnessScore_FormattedToTwoDecimalPlaces()
    {
        var result = MakeResult(s => s.RagFaithfulnessScore = 0.9);

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false));

        Assert.Contains("0.90", cut.Markup);
    }

    [Fact]
    public void ContextPrecisionScore_FormattedToTwoDecimalPlaces()
    {
        var result = MakeResult(s => s.ContextPrecisionScore = 1.0);

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false));

        Assert.Contains("1.00", cut.Markup);
    }

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var idx = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) != -1)
        {
            count++;
            idx += pattern.Length;
        }
        return count;
    }
}
