using SoWImprover.Services;

namespace SoWImprover.Tests.Services;

public class EvaluationSummaryServiceTests
{
    [Fact]
    public void BuildPrompt_IncludesSectionTitlesAndScores()
    {
        var sections = new List<SectionSummaryInput>
        {
            new()
            {
                Title = "Scope of Work",
                OriginalContent = "Original scope text",
                RagImprovedContent = "Improved scope text",
                OriginalQualityScore = 2,
                RagQualityScore = 4,
                BaselineFaithfulnessScore = 0.8,
                RagFaithfulnessScore = 0.9,
                ContextPrecisionScore = 0.7
            }
        };

        var prompt = EvaluationSummaryService.BuildPrompt(sections, totalSectionCount: 3);

        Assert.Contains("Scope of Work", prompt);
        Assert.Contains("Original scope text", prompt);
        Assert.Contains("Improved scope text", prompt);
        Assert.Contains("2", prompt); // original quality
        Assert.Contains("4", prompt); // rag quality
        Assert.Contains("0.7", prompt); // context precision
    }

    [Fact]
    public void BuildPrompt_IndicatesPartialWhenNotAllSectionsComplete()
    {
        var sections = new List<SectionSummaryInput>
        {
            new() { Title = "Introduction", OriginalContent = "x", RagImprovedContent = "y", RagQualityScore = 3 }
        };

        var prompt = EvaluationSummaryService.BuildPrompt(sections, totalSectionCount: 5);

        Assert.Contains("1 of 5", prompt);
    }

    [Fact]
    public void BuildPrompt_DoesNotIndicatePartialWhenAllSectionsComplete()
    {
        var sections = new List<SectionSummaryInput>
        {
            new() { Title = "Introduction", OriginalContent = "x", RagImprovedContent = "y", RagQualityScore = 3 }
        };

        var prompt = EvaluationSummaryService.BuildPrompt(sections, totalSectionCount: 1);

        Assert.DoesNotContain("1 of 1", prompt);
    }

    [Fact]
    public void BuildPrompt_TruncatesLongContent()
    {
        var longText = new string('A', 3000);
        var sections = new List<SectionSummaryInput>
        {
            new() { Title = "Long Section", OriginalContent = longText, RagImprovedContent = longText, RagQualityScore = 3 }
        };

        var prompt = EvaluationSummaryService.BuildPrompt(sections, totalSectionCount: 1);

        Assert.DoesNotContain(longText, prompt);
        Assert.Contains("[truncated]", prompt);
        Assert.Contains(new string('A', 2000), prompt);
    }

    [Fact]
    public void BuildPrompt_DoesNotTruncateShortContent()
    {
        var shortText = new string('B', 500);
        var sections = new List<SectionSummaryInput>
        {
            new() { Title = "Short Section", OriginalContent = shortText, RagImprovedContent = shortText, RagQualityScore = 3 }
        };

        var prompt = EvaluationSummaryService.BuildPrompt(sections, totalSectionCount: 1);

        Assert.Contains(shortText, prompt);
        Assert.DoesNotContain("[truncated]", prompt);
    }

    [Fact]
    public void BuildPrompt_HandlesEmptySectionList()
    {
        var sections = new List<SectionSummaryInput>();

        var prompt = EvaluationSummaryService.BuildPrompt(sections, totalSectionCount: 5);

        Assert.Contains("0 of 5", prompt);
    }

    [Fact]
    public void BuildPrompt_IncludesNoiseSensitivityGuidance()
    {
        var sections = new List<SectionSummaryInput>
        {
            new() { Title = "Test", OriginalContent = "x", RagImprovedContent = "y", RagQualityScore = 3 }
        };

        var prompt = EvaluationSummaryService.BuildPrompt(sections, totalSectionCount: 1);

        Assert.Contains("LOWER is better", prompt);
        Assert.Contains("0.0 = ideal", prompt);
        Assert.Contains("1.0 = worst", prompt);
        Assert.Contains("Do not describe high noise sensitivity as positive", prompt);
    }

    [Fact]
    public void BuildPrompt_IncludesScoreAccuracyRule()
    {
        var sections = new List<SectionSummaryInput>
        {
            new() { Title = "Test", OriginalContent = "x", RagImprovedContent = "y", RagQualityScore = 3 }
        };

        var prompt = EvaluationSummaryService.BuildPrompt(sections, totalSectionCount: 1);

        Assert.Contains("EXACTLY match the numbers", prompt);
        Assert.Contains("Do not round, infer, or fabricate", prompt);
    }

    [Fact]
    public void BuildPrompt_IncludesNullScoresAsNotAvailable()
    {
        var sections = new List<SectionSummaryInput>
        {
            new()
            {
                Title = "Delivery",
                OriginalContent = "text",
                RagImprovedContent = "improved",
                RagQualityScore = 4,
                ContextPrecisionScore = null,
                NoiseSensitivityScore = null
            }
        };

        var prompt = EvaluationSummaryService.BuildPrompt(sections, totalSectionCount: 1);

        Assert.Contains("Delivery", prompt);
        Assert.Contains("N/A", prompt);
    }
}
