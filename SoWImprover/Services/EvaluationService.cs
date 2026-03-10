using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SoWImprover.Services;

public class EvaluationService(
    IConfiguration configuration,
    ILogger<EvaluationService> logger,
    GpuMemoryManager gpuMemory)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public record SectionInput
    {
        public string Original { get; init; } = "";
        public string Baseline { get; init; } = "";
        public string RagImproved { get; init; } = "";
        public List<string> RetrievedContexts { get; init; } = [];
        public string DefinitionOfGood { get; init; } = "";
    }

    public record SectionScores
    {
        [JsonPropertyName("original_quality")]
        public int? OriginalQualityScore { get; init; }

        [JsonPropertyName("baseline_quality")]
        public int? BaselineQualityScore { get; init; }

        [JsonPropertyName("rag_quality")]
        public int? RagQualityScore { get; init; }

        [JsonPropertyName("baseline_faithfulness")]
        public double? BaselineFaithfulnessScore { get; init; }

        [JsonPropertyName("rag_faithfulness")]
        public double? RagFaithfulnessScore { get; init; }

        [JsonPropertyName("context_precision")]
        public double? ContextPrecisionScore { get; init; }

        [JsonPropertyName("context_recall")]
        public double? ContextRecallScore { get; init; }

        [JsonPropertyName("baseline_factual_correctness")]
        public double? BaselineFactualCorrectnessScore { get; init; }

        [JsonPropertyName("rag_factual_correctness")]
        public double? RagFactualCorrectnessScore { get; init; }

        [JsonPropertyName("baseline_response_relevancy")]
        public double? BaselineResponseRelevancyScore { get; init; }

        [JsonPropertyName("rag_response_relevancy")]
        public double? RagResponseRelevancyScore { get; init; }

        [JsonPropertyName("noise_sensitivity")]
        public double? NoiseSensitivityScore { get; init; }
    }

    /// <summary>
    /// Streams evaluation results one section at a time. Each yield returns
    /// (sectionIndex, scores) as soon as the Python script finishes that section.
    /// </summary>
    public async IAsyncEnumerable<(int Index, SectionScores Scores)> EvaluateStreamingAsync(
        List<SectionInput> sections,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var endpoint = configuration["Evaluation:Endpoint"]
            ?? throw new InvalidOperationException("Evaluation:Endpoint not configured in appsettings.json");
        var modelId = configuration["Evaluation:ModelName"]
            ?? throw new InvalidOperationException("Evaluation:ModelName not configured in appsettings.json");
        var embeddingModelId = configuration["Evaluation:EmbeddingModelName"]
            ?? configuration["Ollama:EmbeddingModelName"];
        var inputJson = BuildInputJson(endpoint, modelId, sections, embeddingModelId);

        // Free GPU VRAM by unloading models that are no longer needed before
        // loading the (potentially larger) evaluation model.
        await gpuMemory.PrepareForEvaluationAsync(ct);

        var scriptPath = Path.Combine(AppContext.BaseDirectory, "ragas_evaluate.py");
        if (!File.Exists(scriptPath))
            throw new FileNotFoundException(
                $"ragas_evaluate.py not found at '{scriptPath}'. Ensure it is copied to the output directory.");

        var python = FindPython();
        var psi = new ProcessStartInfo
        {
            FileName = python,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add(scriptPath);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start Python process for Ragas evaluation.");

        await process.StandardInput.WriteAsync(inputJson);
        process.StandardInput.Close();

        // Log stderr in background (progress messages + warnings)
        _ = Task.Run(async () =>
        {
            try
            {
                while (await process.StandardError.ReadLineAsync(ct) is { } line)
                    logger.LogInformation("Ragas: {Line}", line);
            }
            catch { /* process exited or cancelled */ }
        }, ct);

        // Per-section timeout: reset each time we receive output from the Python process.
        // This avoids a fixed total timeout that can't accommodate many slow sections.
        var perSectionTimeout = TimeSpan.FromMinutes(30);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(perSectionTimeout);

        // Read stdout line by line — each line is a JSON object for one section
        string? line;
        while ((line = await process.StandardOutput.ReadLineAsync(timeoutCts.Token)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            // Reset the per-section timeout on each line of output
            timeoutCts.CancelAfter(perSectionTimeout);

            var parsed = ParseSingleResult(line);
            if (parsed is not null)
                yield return parsed.Value;
        }

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            throw new InvalidOperationException("Ragas evaluation timed out — no output received for 30 minutes.");
        }

        if (process.ExitCode != 0)
            logger.LogWarning("Ragas process exited with code {Code}", process.ExitCode);
    }

    internal static string BuildInputJson(
        string endpoint, string modelId, List<SectionInput> sections,
        string? embeddingModelId = null)
    {
        var input = new
        {
            Endpoint = endpoint,
            ModelId = modelId,
            EmbeddingModelId = embeddingModelId,
            Sections = sections
        };
        return JsonSerializer.Serialize(input, JsonOpts);
    }

    internal static (int Index, SectionScores Scores)? ParseSingleResult(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var el = doc.RootElement;

            var index = el.TryGetProperty("index", out var idx) ? idx.GetInt32() : -1;
            if (index < 0) return null;

            var scores = ParseScoresFromElement(el);

            return (index, scores);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Keep for unit test compatibility
    internal static List<SectionScores> ParseOutputJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var sectionsElement = doc.RootElement.GetProperty("sections");
        var results = new List<SectionScores>();

        foreach (var el in sectionsElement.EnumerateArray())
        {
            results.Add(ParseScoresFromElement(el));
        }

        return results;
    }

    private static SectionScores ParseScoresFromElement(JsonElement el) => new()
    {
        OriginalQualityScore = el.TryGetProperty("original_quality", out var oq) && oq.ValueKind != JsonValueKind.Null
            ? oq.GetInt32() : null,
        BaselineQualityScore = el.TryGetProperty("baseline_quality", out var bq) && bq.ValueKind != JsonValueKind.Null
            ? bq.GetInt32() : null,
        RagQualityScore = el.TryGetProperty("rag_quality", out var rq) && rq.ValueKind != JsonValueKind.Null
            ? rq.GetInt32() : null,
        BaselineFaithfulnessScore = el.TryGetProperty("baseline_faithfulness", out var bf) && bf.ValueKind != JsonValueKind.Null
            ? bf.GetDouble() : null,
        RagFaithfulnessScore = el.TryGetProperty("rag_faithfulness", out var rf) && rf.ValueKind != JsonValueKind.Null
            ? rf.GetDouble() : null,
        ContextPrecisionScore = el.TryGetProperty("context_precision", out var cp) && cp.ValueKind != JsonValueKind.Null
            ? cp.GetDouble() : null,
        ContextRecallScore = el.TryGetProperty("context_recall", out var cr) && cr.ValueKind != JsonValueKind.Null
            ? cr.GetDouble() : null,
        BaselineFactualCorrectnessScore = el.TryGetProperty("baseline_factual_correctness", out var bfc) && bfc.ValueKind != JsonValueKind.Null
            ? bfc.GetDouble() : null,
        RagFactualCorrectnessScore = el.TryGetProperty("rag_factual_correctness", out var rfc) && rfc.ValueKind != JsonValueKind.Null
            ? rfc.GetDouble() : null,
        BaselineResponseRelevancyScore = el.TryGetProperty("baseline_response_relevancy", out var brr) && brr.ValueKind != JsonValueKind.Null
            ? brr.GetDouble() : null,
        RagResponseRelevancyScore = el.TryGetProperty("rag_response_relevancy", out var rrr) && rrr.ValueKind != JsonValueKind.Null
            ? rrr.GetDouble() : null,
        NoiseSensitivityScore = el.TryGetProperty("noise_sensitivity", out var ns) && ns.ValueKind != JsonValueKind.Null
            ? ns.GetDouble() : null,
    };

    private static string FindPython()
    {
        foreach (var candidate in new[] { "py", "python3", "python" })
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = candidate,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(3000);
                if (p?.ExitCode == 0) return candidate;
            }
            catch { }
        }
        throw new InvalidOperationException("Python not found.");
    }
}
