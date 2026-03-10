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
    public void ScoreBadges_QualityTooltip_ExplainsContext()
    {
        var result = MakeResult(s => s.OriginalQualityScore = 3);

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false));

        Assert.Contains("starting point", cut.Markup);
        Assert.Contains("role=\"tooltip\"", cut.Markup);
    }

    [Fact]
    public void ScoreBadges_FaithfulnessTooltip_ExplainsHallucination()
    {
        var result = MakeResult(s => s.BaselineFaithfulnessScore = 0.9);

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false));

        Assert.Contains("hallucinated", cut.Markup);
    }

    [Fact]
    public void ScoreBadges_ContextPrecisionTooltip_ExplainsRetrieval()
    {
        var result = MakeResult(s => s.ContextPrecisionScore = 0.7);

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false));

        Assert.Contains("retriever", cut.Markup);
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

    // ── Inline badge loading state ──────────────────────────────

    [Fact]
    public void EvalLoading_ShowsInlineSpinnersWhenEvaluating()
    {
        var result = MakeResult();
        var evaluating = new HashSet<int> { 0 };

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false)
            .Add(x => x.EvaluatingSections, evaluating));

        // All badges should appear with inline spinners
        Assert.Contains("app-badge-spinner", cut.Markup);
        Assert.Contains("app-score-badge", cut.Markup);
    }

    [Fact]
    public void EvalLoading_NoSpinnersWhenNotEvaluating()
    {
        var result = MakeResult();

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false)
            .Add(x => x.EvaluatingSections, new HashSet<int>()));

        Assert.DoesNotContain("app-badge-spinner", cut.Markup);
    }

    [Fact]
    public void EvalLoading_BadgesHaveAriaLiveForUpdates()
    {
        var result = MakeResult();
        var evaluating = new HashSet<int> { 0 };

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false)
            .Add(x => x.EvaluatingSections, evaluating));

        Assert.Contains("aria-live=\"polite\"", cut.Markup);
    }

    [Fact]
    public void EvalLoading_AllThreeColumnsShowBadges()
    {
        var result = MakeResult();
        var evaluating = new HashSet<int> { 0 };

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false)
            .Add(x => x.EvaluatingSections, evaluating));

        // Should have badge containers in all 3 columns
        var markup = cut.Markup;
        var count = CountOccurrences(markup, "app-score-badges");
        Assert.Equal(3, count);
    }

    [Fact]
    public void EvalLoading_PartialScores_ShowValuesAndSpinners()
    {
        var result = MakeResult(s =>
        {
            s.RagQualityScore = 4;
            s.BaselineQualityScore = 3;
            s.OriginalQualityScore = 2;
        });
        var evaluating = new HashSet<int> { 0 };

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false)
            .Add(x => x.EvaluatingSections, evaluating));

        // Scores that arrived should show values
        Assert.Contains("4/5", cut.Markup);
        Assert.Contains("3/5", cut.Markup);
        Assert.Contains("2/5", cut.Markup);
        // Scores not yet arrived should show spinners
        Assert.Contains("app-badge-spinner", cut.Markup);
    }

    // ── Error cross when scoring fails ─────────────────────────

    [Fact]
    public void EvalError_ShowsErrorCrossWhenScoreIsNull()
    {
        // Simulate: evaluation complete (not in evaluating set) but some scores null
        var result = MakeResult(s =>
        {
            s.RagQualityScore = 4;
            // Other scores left null = failed
        });

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false)
            .Add(x => x.EvaluatingSections, new HashSet<int>()));

        Assert.Contains("app-badge-error", cut.Markup);
        Assert.Contains("Scoring failed", cut.Markup);
    }

    [Fact]
    public void EvalError_ShowsErrorCrossesWhenAttemptedButNoScores()
    {
        // Simulate: evaluation was attempted (in attempted set) but streaming
        // ended before any scores arrived (not in evaluating set, no scores)
        var result = MakeResult();
        var attempted = new HashSet<int> { 0 };

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false)
            .Add(x => x.EvaluatingSections, new HashSet<int>())
            .Add(x => x.EvaluationAttemptedSections, attempted));

        // Badges should render (because attempted) with error crosses (because no scores)
        Assert.Contains("app-score-badge", cut.Markup);
        Assert.Contains("app-badge-error", cut.Markup);
        Assert.Contains("Scoring failed", cut.Markup);
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

    // ── New metric badges ──────────────────────────────────────

    [Fact]
    public void ScoreBadges_FactualCorrectness_RendersInBaselineColumn()
    {
        var result = MakeResult(s => s.BaselineFactualCorrectnessScore = 0.92);

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false));

        Assert.Contains("Factual correctness:", cut.Markup);
        Assert.Contains("0.92", cut.Markup);
    }

    [Fact]
    public void ScoreBadges_FactualCorrectness_RendersInRagColumn()
    {
        var result = MakeResult(s => s.RagFactualCorrectnessScore = 0.88);

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false));

        Assert.Contains("Factual correctness:", cut.Markup);
        Assert.Contains("0.88", cut.Markup);
    }

    [Fact]
    public void ScoreBadges_FactualCorrectnessTooltip_ExplainsClaims()
    {
        var result = MakeResult(s => s.RagFactualCorrectnessScore = 0.9);

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false));

        Assert.Contains("atomic claims", cut.Markup);
    }

    [Fact]
    public void ScoreBadges_ContextRecall_RendersInRagColumn()
    {
        var result = MakeResult(s => s.ContextRecallScore = 0.85);

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false));

        Assert.Contains("Context recall:", cut.Markup);
        Assert.Contains("0.85", cut.Markup);
    }

    [Fact]
    public void ScoreBadges_ContextRecallTooltip_ExplainsRetrieval()
    {
        var result = MakeResult(s => s.ContextRecallScore = 0.7);

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false));

        Assert.Contains("retriever missed", cut.Markup);
    }

    [Fact]
    public void ScoreBadges_ResponseRelevancy_RendersInBothColumns()
    {
        var result = MakeResult(s =>
        {
            s.BaselineResponseRelevancyScore = 0.75;
            s.RagResponseRelevancyScore = 0.82;
        });

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false));

        Assert.Contains("Relevancy:", cut.Markup);
        Assert.Contains("0.75", cut.Markup);
        Assert.Contains("0.82", cut.Markup);
    }

    [Fact]
    public void ScoreBadges_ResponseRelevancyTooltip_ExplainsTask()
    {
        var result = MakeResult(s => s.RagResponseRelevancyScore = 0.8);

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false));

        Assert.Contains("on-task", cut.Markup);
    }

    [Fact]
    public void ScoreBadges_NoiseSensitivity_RendersInRagColumn()
    {
        var result = MakeResult(s => s.NoiseSensitivityScore = 0.15);

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false));

        Assert.Contains("Noise sensitivity:", cut.Markup);
        Assert.Contains("0.15", cut.Markup);
    }

    [Fact]
    public void ScoreBadges_NoiseSensitivityTooltip_ExplainsLowerIsBetter()
    {
        var result = MakeResult(s => s.NoiseSensitivityScore = 0.3);

        var cut = Render<ResultsPanel>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.ShowEditingFeatures, false)
            .Add(x => x.ShowExplanations, false));

        Assert.Contains("Lower is better", cut.Markup);
        Assert.Contains("noise", cut.Markup);
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
