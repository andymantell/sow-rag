namespace SoWImprover.Services;

public record SectionSummaryInput
{
    public string Title { get; init; } = "";
    public string OriginalContent { get; init; } = "";
    public string RagImprovedContent { get; init; } = "";
    public int? OriginalQualityScore { get; init; }
    public int? BaselineQualityScore { get; init; }
    public int? RagQualityScore { get; init; }
    public double? BaselineFaithfulnessScore { get; init; }
    public double? RagFaithfulnessScore { get; init; }
    public double? ContextPrecisionScore { get; init; }
    public double? ContextRecallScore { get; init; }
    public double? BaselineFactualCorrectnessScore { get; init; }
    public double? RagFactualCorrectnessScore { get; init; }
    public double? BaselineResponseRelevancyScore { get; init; }
    public double? RagResponseRelevancyScore { get; init; }
    public double? NoiseSensitivityScore { get; init; }
}

public interface IEvaluationSummaryService
{
    Task<string> GenerateSummaryAsync(
        List<SectionSummaryInput> completedSections,
        int totalSectionCount,
        CancellationToken ct);
}
