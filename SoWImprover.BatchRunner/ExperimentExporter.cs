using System.Text.Json;
using System.Text.Json.Serialization;
using SoWImprover.Models;

namespace SoWImprover.BatchRunner;

/// <summary>
/// Exports batch experiment results to a structured JSON file for analysis.
/// </summary>
public static class ExperimentExporter
{
    private static readonly JsonSerializerOptions SerialiserOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Builds a JSON string from the batch experiment results.
    /// </summary>
    public static string BuildJson(
        IReadOnlyList<(DocumentEntity Entity, ImprovementResult Result)> results,
        string corpusFolder,
        IReadOnlyList<string> corpusDocuments,
        int totalChunks,
        string chatModel,
        string embeddingModel)
    {
        var export = new ExperimentExport
        {
            ExportedAt = DateTime.UtcNow,
            Corpus = new CorpusInfo
            {
                Folder = corpusFolder,
                Documents = corpusDocuments.ToList(),
                TotalChunks = totalChunks,
                EmbeddingModel = embeddingModel,
                ChatModel = chatModel
            },
            TestDocuments = results.Select(r => BuildDocument(r.Entity, r.Result)).ToList()
        };

        return JsonSerializer.Serialize(export, SerialiserOptions);
    }

    /// <summary>
    /// Writes a JSON string to the specified file path.
    /// </summary>
    public static async Task WriteAsync(string path, string json, CancellationToken ct = default)
    {
        await File.WriteAllTextAsync(path, json, ct);
    }

    private static TestDocumentExport BuildDocument(DocumentEntity entity, ImprovementResult result)
    {
        var sections = result.Sections.Select(BuildSection).ToList();
        var evaluatedCount = sections.Count(s => s.Scores?.RagQualityScore is not null);

        return new TestDocumentExport
        {
            FileName = entity.FileName,
            SectionCount = sections.Count,
            EvaluatedSectionCount = evaluatedCount,
            EvaluationSummary = entity.EvaluationSummary,
            Sections = sections
        };
    }

    private static SectionExport BuildSection(SectionResult sec)
    {
        var hasScores = sec.OriginalQualityScore.HasValue
                     || sec.BaselineQualityScore.HasValue
                     || sec.RagQualityScore.HasValue;

        return new SectionExport
        {
            SectionName = sec.OriginalTitle,
            MatchedCanonicalSection = sec.MatchedSection,
            Unrecognised = sec.Unrecognised,
            Scores = hasScores ? new ScoresExport
            {
                OriginalQualityScore = sec.OriginalQualityScore,
                BaselineQualityScore = sec.BaselineQualityScore,
                RagQualityScore = sec.RagQualityScore,
                BaselineFaithfulnessScore = sec.BaselineFaithfulnessScore,
                RagFaithfulnessScore = sec.RagFaithfulnessScore,
                BaselineFactualCorrectnessScore = sec.BaselineFactualCorrectnessScore,
                RagFactualCorrectnessScore = sec.RagFactualCorrectnessScore,
                BaselineResponseRelevancyScore = sec.BaselineResponseRelevancyScore,
                RagResponseRelevancyScore = sec.RagResponseRelevancyScore,
                ContextPrecisionScore = sec.ContextPrecisionScore,
                ContextRecallScore = sec.ContextRecallScore,
                NoiseSensitivityScore = sec.NoiseSensitivityScore
            } : null,
            RetrievedChunkCount = sec.RetrievedContexts?.Count ?? 0,
            RetrievedScores = sec.RetrievedScores,
            RetrievedContexts = sec.RetrievedContexts,
            OriginalContent = sec.OriginalContent,
            BaselineContent = sec.BaselineContent,
            RagContent = sec.ImprovedContent
        };
    }

    // --- DTOs for serialisation ---

    private sealed class ExperimentExport
    {
        public DateTime ExportedAt { get; init; }
        public CorpusInfo Corpus { get; init; } = null!;
        public List<TestDocumentExport> TestDocuments { get; init; } = [];
    }

    private sealed class CorpusInfo
    {
        public string Folder { get; init; } = "";
        public List<string> Documents { get; init; } = [];
        public int TotalChunks { get; init; }
        public string EmbeddingModel { get; init; } = "";
        public string ChatModel { get; init; } = "";
    }

    private sealed class TestDocumentExport
    {
        public string FileName { get; init; } = "";
        public int SectionCount { get; init; }
        public int EvaluatedSectionCount { get; init; }
        public string? EvaluationSummary { get; init; }
        public List<SectionExport> Sections { get; init; } = [];
    }

    private sealed class SectionExport
    {
        public string SectionName { get; init; } = "";
        public string? MatchedCanonicalSection { get; init; }
        public bool Unrecognised { get; init; }
        public ScoresExport? Scores { get; init; }
        public int RetrievedChunkCount { get; init; }
        public List<float>? RetrievedScores { get; init; }
        public List<string>? RetrievedContexts { get; init; }
        public string OriginalContent { get; init; } = "";
        public string? BaselineContent { get; init; }
        public string? RagContent { get; init; }
    }

    private sealed class ScoresExport
    {
        public int? OriginalQualityScore { get; init; }
        public int? BaselineQualityScore { get; init; }
        public int? RagQualityScore { get; init; }
        public double? BaselineFaithfulnessScore { get; init; }
        public double? RagFaithfulnessScore { get; init; }
        public double? BaselineFactualCorrectnessScore { get; init; }
        public double? RagFactualCorrectnessScore { get; init; }
        public double? BaselineResponseRelevancyScore { get; init; }
        public double? RagResponseRelevancyScore { get; init; }
        public double? ContextPrecisionScore { get; init; }
        public double? ContextRecallScore { get; init; }
        public double? NoiseSensitivityScore { get; init; }
    }
}
