using System.Text.Json;
using SoWImprover.BatchRunner;
using SoWImprover.Models;

namespace SoWImprover.Tests.BatchRunner;

public class ExperimentExporterTests
{
    [Fact]
    public void BuildJson_ProducesValidJsonWithExpectedStructure()
    {
        // Arrange
        var entity = new DocumentEntity
        {
            Id = Guid.NewGuid(),
            FileName = "test-doc.pdf",
            OriginalText = "full text",
            UploadedAt = DateTime.UtcNow,
            EvaluationSummary = "Good overall quality."
        };

        var result = new ImprovementResult
        {
            Sections =
            [
                new SectionResult
                {
                    OriginalTitle = "Introduction",
                    OriginalContent = "Original intro text",
                    ImprovedContent = "Improved intro text",
                    BaselineContent = "Baseline intro text",
                    MatchedSection = "Introduction",
                    Unrecognised = false,
                    OriginalQualityScore = 2,
                    BaselineQualityScore = 3,
                    RagQualityScore = 4,
                    BaselineFaithfulnessScore = 0.85,
                    RagFaithfulnessScore = 0.90,
                    BaselineFactualCorrectnessScore = 0.75,
                    RagFactualCorrectnessScore = 0.80,
                    BaselineResponseRelevancyScore = 0.70,
                    RagResponseRelevancyScore = 0.88,
                    ContextPrecisionScore = 0.92,
                    ContextRecallScore = 0.78,
                    NoiseSensitivityScore = 0.15,
                    RetrievedContexts = ["chunk1 text", "chunk2 text"],
                    RetrievedScores = [0.95f, 0.82f]
                },
                new SectionResult
                {
                    OriginalTitle = "Unknown Section",
                    OriginalContent = "Some content",
                    Unrecognised = true
                }
            ]
        };

        entity.Sections.Add(new SectionEntity { OriginalTitle = "Introduction" });
        entity.Sections.Add(new SectionEntity { OriginalTitle = "Unknown Section", Unrecognised = true });

        var results = new List<(DocumentEntity Entity, ImprovementResult Result)> { (entity, result) };

        // Act
        var json = ExperimentExporter.BuildJson(
            results,
            corpusFolder: "./sample-sows",
            corpusDocuments: ["corpus1.pdf", "corpus2.pdf"],
            totalChunks: 42,
            chatModel: "phi-4",
            embeddingModel: "nomic-embed-text");

        // Assert — valid JSON
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Top-level fields
        Assert.True(root.TryGetProperty("exportedAt", out _));

        // Corpus
        var corpus = root.GetProperty("corpus");
        Assert.Equal("./sample-sows", corpus.GetProperty("folder").GetString());
        Assert.Equal(2, corpus.GetProperty("documents").GetArrayLength());
        Assert.Equal(42, corpus.GetProperty("totalChunks").GetInt32());
        Assert.Equal("phi-4", corpus.GetProperty("chatModel").GetString());
        Assert.Equal("nomic-embed-text", corpus.GetProperty("embeddingModel").GetString());

        // Test documents
        var testDocs = root.GetProperty("testDocuments");
        Assert.Equal(1, testDocs.GetArrayLength());

        var testDoc = testDocs[0];
        Assert.Equal("test-doc.pdf", testDoc.GetProperty("fileName").GetString());
        Assert.Equal(2, testDoc.GetProperty("sectionCount").GetInt32());
        Assert.Equal(1, testDoc.GetProperty("evaluatedSectionCount").GetInt32());
        Assert.Equal("Good overall quality.", testDoc.GetProperty("evaluationSummary").GetString());

        // Sections
        var sections = testDoc.GetProperty("sections");
        Assert.Equal(2, sections.GetArrayLength());

        // Recognised section
        var sec0 = sections[0];
        Assert.Equal("Introduction", sec0.GetProperty("sectionName").GetString());
        Assert.Equal("Introduction", sec0.GetProperty("matchedCanonicalSection").GetString());
        Assert.False(sec0.GetProperty("unrecognised").GetBoolean());
        Assert.Equal("Original intro text", sec0.GetProperty("originalContent").GetString());
        Assert.Equal("Baseline intro text", sec0.GetProperty("baselineContent").GetString());
        Assert.Equal("Improved intro text", sec0.GetProperty("ragContent").GetString());
        Assert.Equal(2, sec0.GetProperty("retrievedChunkCount").GetInt32());

        // Scores
        var scores = sec0.GetProperty("scores");
        Assert.Equal(2, scores.GetProperty("originalQualityScore").GetInt32());
        Assert.Equal(3, scores.GetProperty("baselineQualityScore").GetInt32());
        Assert.Equal(4, scores.GetProperty("ragQualityScore").GetInt32());
        Assert.Equal(0.85, scores.GetProperty("baselineFaithfulnessScore").GetDouble(), 2);
        Assert.Equal(0.90, scores.GetProperty("ragFaithfulnessScore").GetDouble(), 2);
        Assert.Equal(0.92, scores.GetProperty("contextPrecisionScore").GetDouble(), 2);
        Assert.Equal(0.15, scores.GetProperty("noiseSensitivityScore").GetDouble(), 2);

        // Retrieved data
        var retrievedScores = sec0.GetProperty("retrievedScores");
        Assert.Equal(2, retrievedScores.GetArrayLength());

        var retrievedContexts = sec0.GetProperty("retrievedContexts");
        Assert.Equal(2, retrievedContexts.GetArrayLength());
        Assert.Equal("chunk1 text", retrievedContexts[0].GetString());

        // Unrecognised section — no scores
        var sec1 = sections[1];
        Assert.True(sec1.GetProperty("unrecognised").GetBoolean());
        Assert.False(sec1.TryGetProperty("scores", out _));
        Assert.Equal(0, sec1.GetProperty("retrievedChunkCount").GetInt32());
    }

    [Fact]
    public void BuildJson_EmptyResults_ProducesValidJson()
    {
        var results = new List<(DocumentEntity Entity, ImprovementResult Result)>();

        var json = ExperimentExporter.BuildJson(
            results,
            corpusFolder: "./corpus",
            corpusDocuments: [],
            totalChunks: 0,
            chatModel: "test",
            embeddingModel: "test");

        var doc = JsonDocument.Parse(json);
        Assert.Equal(0, doc.RootElement.GetProperty("testDocuments").GetArrayLength());
    }

    [Fact]
    public async Task WriteAsync_WritesJsonToFile()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"exporter-test-{Guid.NewGuid()}.json");
        try
        {
            var json = """{"test": true}""";
            await ExperimentExporter.WriteAsync(tempPath, json);

            Assert.True(File.Exists(tempPath));
            var content = await File.ReadAllTextAsync(tempPath);
            Assert.Equal(json, content);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
