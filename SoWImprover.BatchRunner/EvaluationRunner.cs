using Microsoft.EntityFrameworkCore;
using SoWImprover.Data;
using SoWImprover.Models;
using SoWImprover.Services;

namespace SoWImprover.BatchRunner;

public class EvaluationRunner(
    EvaluationService evaluator,
    IEvaluationSummaryService summaryService,
    IDbContextFactory<SoWDbContext> dbFactory,
    ConsoleLogger log)
{
    public async Task EvaluateDocumentAsync(
        DocumentEntity entity,
        IReadOnlyList<SectionResult> sectionResults,
        CancellationToken ct)
    {
        // Build evaluation inputs — only recognised sections with baseline content
        var evaluable = new List<(int EntitySectionIndex, SectionResult Result)>();
        for (var i = 0; i < entity.Sections.Count; i++)
        {
            var sec = entity.Sections[i];
            var result = sectionResults[i];
            if (!sec.Unrecognised && sec.BaselineContent is not null)
                evaluable.Add((i, result));
        }

        if (evaluable.Count == 0)
        {
            log.Log("No sections to evaluate.");
            return;
        }

        var inputs = evaluable.Select(e => new EvaluationService.SectionInput
        {
            Original = e.Result.OriginalContent,
            Baseline = e.Result.BaselineContent!,
            RagImproved = e.Result.ImprovedContent!,
            RetrievedContexts = e.Result.RetrievedContexts ?? [],
            DefinitionOfGood = e.Result.DefinitionOfGoodText ?? ""
        }).ToList();

        log.Log("Running evaluation...");
        var completedSections = new HashSet<int>();
        var metricsReceived = 0;
        await foreach (var (streamIdx, scores) in evaluator.EvaluateStreamingAsync(inputs, ct))
        {
            var (entityIdx, result) = evaluable[streamIdx];
            var sec = entity.Sections[entityIdx];
            metricsReceived++;

            // Merge scores into section result (in-memory, for export)
            // Use null-coalescing — EvaluateStreamingAsync yields partial updates
            result.OriginalQualityScore = scores.OriginalQualityScore ?? result.OriginalQualityScore;
            result.BaselineQualityScore = scores.BaselineQualityScore ?? result.BaselineQualityScore;
            result.RagQualityScore = scores.RagQualityScore ?? result.RagQualityScore;
            result.BaselineFaithfulnessScore = scores.BaselineFaithfulnessScore ?? result.BaselineFaithfulnessScore;
            result.RagFaithfulnessScore = scores.RagFaithfulnessScore ?? result.RagFaithfulnessScore;
            result.BaselineFactualCorrectnessScore = scores.BaselineFactualCorrectnessScore ?? result.BaselineFactualCorrectnessScore;
            result.RagFactualCorrectnessScore = scores.RagFactualCorrectnessScore ?? result.RagFactualCorrectnessScore;
            result.BaselineResponseRelevancyScore = scores.BaselineResponseRelevancyScore ?? result.BaselineResponseRelevancyScore;
            result.RagResponseRelevancyScore = scores.RagResponseRelevancyScore ?? result.RagResponseRelevancyScore;
            result.ContextPrecisionScore = scores.ContextPrecisionScore ?? result.ContextPrecisionScore;
            result.ContextRecallScore = scores.ContextRecallScore ?? result.ContextRecallScore;
            result.NoiseSensitivityScore = scores.NoiseSensitivityScore ?? result.NoiseSensitivityScore;

            // Persist scores to DB
            await PersistScoresAsync(sec, scores, ct);

            // Check if this section now has all metrics — log once when complete
            if (!completedSections.Contains(streamIdx) && result.NoiseSensitivityScore.HasValue)
            {
                completedSections.Add(streamIdx);
                var sectionName = sec.MatchedSection ?? sec.OriginalTitle;
                log.Log($"  [{completedSections.Count}/{evaluable.Count}] {sectionName}", indent: 1);
                log.Log($"    Original: {result.OriginalQualityScore} | Baseline: {result.BaselineQualityScore} | RAG: {result.RagQualityScore}", indent: 2);
                log.Log($"    Faithfulness — baseline: {result.BaselineFaithfulnessScore:F2} | RAG: {result.RagFaithfulnessScore:F2}", indent: 2);
                log.Log($"    Factual correctness — baseline: {result.BaselineFactualCorrectnessScore:F2} | RAG: {result.RagFactualCorrectnessScore:F2}", indent: 2);
                log.Log($"    Response relevancy — baseline: {result.BaselineResponseRelevancyScore:F2} | RAG: {result.RagResponseRelevancyScore:F2}", indent: 2);
                log.Log($"    Context — precision: {result.ContextPrecisionScore:F2} | recall: {result.ContextRecallScore:F2} | noise: {result.NoiseSensitivityScore:F2}", indent: 2);
            }
            else if (!completedSections.Contains(streamIdx))
            {
                // Brief progress for partial updates
                log.Log($"  {sec.MatchedSection ?? sec.OriginalTitle}: metric {metricsReceived} received...", indent: 1);
            }
        }

        log.Log("Evaluation complete.");

        // Generate summary
        log.Log("Generating summary...");
        var summaryInputs = evaluable
            .Where(e => e.Result.RagQualityScore.HasValue)
            .Select(e =>
            {
                var sec = entity.Sections[e.EntitySectionIndex];
                var r = e.Result;
                return new SectionSummaryInput
                {
                    Title = sec.OriginalTitle,
                    OriginalContent = r.OriginalContent,
                    RagImprovedContent = r.ImprovedContent ?? "",
                    OriginalQualityScore = r.OriginalQualityScore,
                    BaselineQualityScore = r.BaselineQualityScore,
                    RagQualityScore = r.RagQualityScore,
                    BaselineFaithfulnessScore = r.BaselineFaithfulnessScore,
                    RagFaithfulnessScore = r.RagFaithfulnessScore,
                    BaselineFactualCorrectnessScore = r.BaselineFactualCorrectnessScore,
                    RagFactualCorrectnessScore = r.RagFactualCorrectnessScore,
                    BaselineResponseRelevancyScore = r.BaselineResponseRelevancyScore,
                    RagResponseRelevancyScore = r.RagResponseRelevancyScore,
                    ContextPrecisionScore = r.ContextPrecisionScore,
                    ContextRecallScore = r.ContextRecallScore,
                    NoiseSensitivityScore = r.NoiseSensitivityScore
                };
            }).ToList();

        var summary = await summaryService.GenerateSummaryAsync(summaryInputs, evaluable.Count, ct);
        log.Log($"Summary: {(string.IsNullOrWhiteSpace(summary) ? "(empty)" : "done")}");

        // Persist summary
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Attach(entity);
        entity.EvaluationSummary = summary;
        db.Entry(entity).Property(d => d.EvaluationSummary).IsModified = true;
        await db.SaveChangesAsync(ct);
    }

    private async Task PersistScoresAsync(
        SectionEntity sec, EvaluationService.SectionScores scores, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Attach(sec);
        sec.OriginalQualityScore = scores.OriginalQualityScore;
        sec.BaselineQualityScore = scores.BaselineQualityScore;
        sec.RagQualityScore = scores.RagQualityScore;
        sec.BaselineFaithfulnessScore = scores.BaselineFaithfulnessScore;
        sec.RagFaithfulnessScore = scores.RagFaithfulnessScore;
        sec.BaselineFactualCorrectnessScore = scores.BaselineFactualCorrectnessScore;
        sec.RagFactualCorrectnessScore = scores.RagFactualCorrectnessScore;
        sec.BaselineResponseRelevancyScore = scores.BaselineResponseRelevancyScore;
        sec.RagResponseRelevancyScore = scores.RagResponseRelevancyScore;
        sec.ContextPrecisionScore = scores.ContextPrecisionScore;
        sec.ContextRecallScore = scores.ContextRecallScore;
        sec.NoiseSensitivityScore = scores.NoiseSensitivityScore;

        foreach (var prop in new[]
        {
            nameof(SectionEntity.OriginalQualityScore),
            nameof(SectionEntity.BaselineQualityScore),
            nameof(SectionEntity.RagQualityScore),
            nameof(SectionEntity.BaselineFaithfulnessScore),
            nameof(SectionEntity.RagFaithfulnessScore),
            nameof(SectionEntity.BaselineFactualCorrectnessScore),
            nameof(SectionEntity.RagFactualCorrectnessScore),
            nameof(SectionEntity.BaselineResponseRelevancyScore),
            nameof(SectionEntity.RagResponseRelevancyScore),
            nameof(SectionEntity.ContextPrecisionScore),
            nameof(SectionEntity.ContextRecallScore),
            nameof(SectionEntity.NoiseSensitivityScore),
        })
        {
            db.Entry(sec).Property(prop).IsModified = true;
        }

        await db.SaveChangesAsync(ct);
    }
}
