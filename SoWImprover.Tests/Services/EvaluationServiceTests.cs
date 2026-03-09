using SoWImprover.Services;

namespace SoWImprover.Tests.Services;

public class EvaluationServiceTests
{
    [Fact]
    public void BuildInputJson_SerializesSectionsCorrectly()
    {
        var sections = new List<EvaluationService.SectionInput>
        {
            new()
            {
                Original = "original text",
                Baseline = "baseline text",
                RagImproved = "rag text",
                RetrievedContexts = ["chunk1", "chunk2"],
                DefinitionOfGood = "quality standards"
            }
        };

        var json = EvaluationService.BuildInputJson(
            "http://localhost:5272/v1", "phi-4", sections);

        Assert.Contains("\"endpoint\"", json);
        Assert.Contains("\"model_id\"", json);
        Assert.Contains("\"original\"", json);
        Assert.Contains("original text", json);
        Assert.Contains("chunk1", json);
    }

    [Fact]
    public void ParseOutputJson_ParsesScoresCorrectly()
    {
        var json = """
        {
          "sections": [
            {
              "baseline_quality": 3,
              "rag_quality": 4,
              "baseline_faithfulness": 0.9,
              "rag_faithfulness": 0.85,
              "context_precision": 0.9
            }
          ]
        }
        """;

        var results = EvaluationService.ParseOutputJson(json);

        Assert.Single(results);
        Assert.Equal(3, results[0].BaselineQualityScore);
        Assert.Equal(4, results[0].RagQualityScore);
        Assert.Equal(0.9, results[0].BaselineFaithfulnessScore);
        Assert.Equal(0.85, results[0].RagFaithfulnessScore);
        Assert.Equal(0.9, results[0].ContextPrecisionScore);
    }

    [Fact]
    public void ParseSingleResult_ParsesJsonlLine()
    {
        var line = """{"index": 2, "baseline_quality": 3, "rag_quality": 4, "baseline_faithfulness": 0.9, "rag_faithfulness": 0.85, "context_precision": 0.9}""";

        var result = EvaluationService.ParseSingleResult(line);

        Assert.NotNull(result);
        Assert.Equal(2, result.Value.Index);
        Assert.Equal(3, result.Value.Scores.BaselineQualityScore);
        Assert.Equal(4, result.Value.Scores.RagQualityScore);
        Assert.Equal(0.9, result.Value.Scores.BaselineFaithfulnessScore);
        Assert.Equal(0.85, result.Value.Scores.RagFaithfulnessScore);
        Assert.Equal(0.9, result.Value.Scores.ContextPrecisionScore);
    }

    [Fact]
    public void ParseSingleResult_HandlesNullScores()
    {
        var line = """{"index": 0, "baseline_quality": null, "rag_quality": 2, "baseline_faithfulness": null, "rag_faithfulness": null, "context_precision": null}""";

        var result = EvaluationService.ParseSingleResult(line);

        Assert.NotNull(result);
        Assert.Equal(0, result.Value.Index);
        Assert.Null(result.Value.Scores.BaselineQualityScore);
        Assert.Equal(2, result.Value.Scores.RagQualityScore);
    }

    [Fact]
    public void ParseSingleResult_ReturnsNullForInvalidJson()
    {
        Assert.Null(EvaluationService.ParseSingleResult("not json"));
        Assert.Null(EvaluationService.ParseSingleResult("""{"no_index": true}"""));
    }

    [Fact]
    public void ParseOutputJson_HandlesNullScores()
    {
        var json = """
        {
          "sections": [
            {
              "baseline_quality": null,
              "rag_quality": 2,
              "baseline_faithfulness": null,
              "rag_faithfulness": null,
              "context_precision": null
            }
          ]
        }
        """;

        var results = EvaluationService.ParseOutputJson(json);

        Assert.Single(results);
        Assert.Null(results[0].BaselineQualityScore);
        Assert.Equal(2, results[0].RagQualityScore);
        Assert.Null(results[0].BaselineFaithfulnessScore);
        Assert.Null(results[0].ContextPrecisionScore);
    }

    [Fact]
    public void ParseSingleResult_IncludesAllScoreFields()
    {
        var line = """{"index": 0, "original_quality": 2, "baseline_quality": 3, "rag_quality": 4, "baseline_faithfulness": 0.8, "rag_faithfulness": 0.9, "context_precision": 0.7, "context_recall": 0.85, "baseline_factual_correctness": 0.92, "rag_factual_correctness": 0.88, "baseline_response_relevancy": 0.75, "rag_response_relevancy": 0.82, "noise_sensitivity": 0.15}""";

        var result = EvaluationService.ParseSingleResult(line);

        Assert.NotNull(result);
        Assert.Equal(2, result.Value.Scores.OriginalQualityScore);
        Assert.Equal(3, result.Value.Scores.BaselineQualityScore);
        Assert.Equal(4, result.Value.Scores.RagQualityScore);
        Assert.Equal(0.8, result.Value.Scores.BaselineFaithfulnessScore);
        Assert.Equal(0.9, result.Value.Scores.RagFaithfulnessScore);
        Assert.Equal(0.7, result.Value.Scores.ContextPrecisionScore);
        Assert.Equal(0.85, result.Value.Scores.ContextRecallScore);
        Assert.Equal(0.92, result.Value.Scores.BaselineFactualCorrectnessScore);
        Assert.Equal(0.88, result.Value.Scores.RagFactualCorrectnessScore);
        Assert.Equal(0.75, result.Value.Scores.BaselineResponseRelevancyScore);
        Assert.Equal(0.82, result.Value.Scores.RagResponseRelevancyScore);
        Assert.Equal(0.15, result.Value.Scores.NoiseSensitivityScore);
    }

    [Fact]
    public void ParseSingleResult_ReturnsNullForNegativeIndex()
    {
        var line = """{"index": -1, "rag_quality": 4}""";

        Assert.Null(EvaluationService.ParseSingleResult(line));
    }

    [Fact]
    public void ParseSingleResult_ReturnsNullForEmptyObject()
    {
        Assert.Null(EvaluationService.ParseSingleResult("{}"));
    }

    [Fact]
    public void ParseOutputJson_IncludesOriginalQualityScore()
    {
        var json = """
        {
          "sections": [
            {
              "original_quality": 2,
              "baseline_quality": 3,
              "rag_quality": 4,
              "baseline_faithfulness": 0.8,
              "rag_faithfulness": 0.9,
              "context_precision": 0.7
            }
          ]
        }
        """;

        var results = EvaluationService.ParseOutputJson(json);

        Assert.Single(results);
        Assert.Equal(2, results[0].OriginalQualityScore);
    }

    [Fact]
    public void ParseOutputJson_HandlesMultipleSections()
    {
        var json = """
        {
          "sections": [
            { "original_quality": 1, "rag_quality": 3, "baseline_faithfulness": 0.5, "rag_faithfulness": 0.6 },
            { "original_quality": 4, "rag_quality": 5, "baseline_faithfulness": 0.9, "rag_faithfulness": 0.95 }
          ]
        }
        """;

        var results = EvaluationService.ParseOutputJson(json);

        Assert.Equal(2, results.Count);
        Assert.Equal(1, results[0].OriginalQualityScore);
        Assert.Equal(4, results[1].OriginalQualityScore);
        Assert.Equal(5, results[1].RagQualityScore);
    }

    [Fact]
    public void BuildInputJson_IncludesAllFieldsInSnakeCase()
    {
        var sections = new List<EvaluationService.SectionInput>
        {
            new()
            {
                Original = "orig",
                Baseline = "base",
                RagImproved = "rag",
                RetrievedContexts = ["ctx1"],
                DefinitionOfGood = "def"
            }
        };

        var json = EvaluationService.BuildInputJson("http://localhost:11434/v1", "mistral:7b", sections);

        Assert.Contains("\"model_id\"", json);
        Assert.Contains("mistral:7b", json);
        Assert.Contains("\"rag_improved\"", json);
        Assert.Contains("\"retrieved_contexts\"", json);
        Assert.Contains("\"definition_of_good\"", json);
    }

    [Fact]
    public void BuildInputJson_IncludesEmbeddingModelId()
    {
        var sections = new List<EvaluationService.SectionInput> { new() };

        var json = EvaluationService.BuildInputJson(
            "http://localhost:11434/v1", "mistral:7b", sections, "nomic-embed-text");

        Assert.Contains("\"embedding_model_id\"", json);
        Assert.Contains("nomic-embed-text", json);
    }

    [Fact]
    public void BuildInputJson_NullEmbeddingModelId_OmittedFromJson()
    {
        var sections = new List<EvaluationService.SectionInput> { new() };

        var json = EvaluationService.BuildInputJson(
            "http://localhost:11434/v1", "mistral:7b", sections, null);

        // With WhenWritingNull, null values are omitted
        Assert.DoesNotContain("\"embedding_model_id\"", json);
    }

    [Fact]
    public void ParseSingleResult_HandlesIntegerFaithfulness()
    {
        // Python may emit 1 instead of 1.0 for perfect scores
        var line = """{"index": 0, "baseline_faithfulness": 1, "rag_faithfulness": 0}""";

        var result = EvaluationService.ParseSingleResult(line);

        Assert.NotNull(result);
        Assert.Equal(1.0, result.Value.Scores.BaselineFaithfulnessScore);
        Assert.Equal(0.0, result.Value.Scores.RagFaithfulnessScore);
    }

    [Fact]
    public void ParseSingleResult_MissingOptionalScoresAreNull()
    {
        // Only index and one score — others should be null
        var line = """{"index": 5, "rag_quality": 3}""";

        var result = EvaluationService.ParseSingleResult(line);

        Assert.NotNull(result);
        Assert.Equal(5, result.Value.Index);
        Assert.Equal(3, result.Value.Scores.RagQualityScore);
        Assert.Null(result.Value.Scores.OriginalQualityScore);
        Assert.Null(result.Value.Scores.BaselineQualityScore);
        Assert.Null(result.Value.Scores.BaselineFaithfulnessScore);
        Assert.Null(result.Value.Scores.RagFaithfulnessScore);
        Assert.Null(result.Value.Scores.ContextPrecisionScore);
    }
}
