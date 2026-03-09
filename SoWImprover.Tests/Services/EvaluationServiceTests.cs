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
}
