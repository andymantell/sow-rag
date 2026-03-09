namespace SoWImprover.Models;

/// <summary>The result of improving an uploaded SoW document.</summary>
public class ImprovementResult
{
    /// <summary>Per-section results, in document order.</summary>
    public List<SectionResult> Sections { get; init; } = [];

    /// <summary>Deduplicated corpus chunks used as retrieval context.</summary>
    public List<ChunkReference> ChunksUsed { get; init; } = [];
}

/// <summary>Improvement result for a single section of the uploaded document.</summary>
public class SectionResult
{
    /// <summary>Heading as it appeared in the original document.</summary>
    public string OriginalTitle { get; init; } = "";

    /// <summary>Body text of the original section (without heading).</summary>
    public string OriginalContent { get; init; } = "";

    /// <summary>Improved body text. Null when the section was not recognised.</summary>
    public string? ImprovedContent { get; set; }

    /// <summary>LLM-improved text without RAG context (baseline). Null when unrecognised.</summary>
    public string? BaselineContent { get; set; }

    /// <summary>The canonical section name this was matched to. Null if unrecognised.</summary>
    public string? MatchedSection { get; init; }

    /// <summary>True when no canonical match was found and the section was left unchanged.</summary>
    public bool Unrecognised { get; init; }

    /// <summary>Bullet-point explanation of changes made. Null when unrecognised.</summary>
    public string? Explanation { get; init; }

    /// <summary>Number of versions that exist for this section. 0 if never improved.</summary>
    public int VersionCount { get; set; }

    /// <summary>Quality score (1-5) for the original content. Null if not evaluated.</summary>
    public int? OriginalQualityScore { get; set; }

    /// <summary>Quality score (1-5) for the baseline (no RAG) version. Null if not evaluated.</summary>
    public int? BaselineQualityScore { get; set; }

    /// <summary>Quality score (1-5) for the RAG-improved version. Null if not evaluated.</summary>
    public int? RagQualityScore { get; set; }

    /// <summary>Faithfulness score (0-1) for the baseline version. Measures fidelity to original content.</summary>
    public double? BaselineFaithfulnessScore { get; set; }

    /// <summary>Faithfulness score (0-1) for the RAG version. Measures fidelity to original content.</summary>
    public double? RagFaithfulnessScore { get; set; }

    /// <summary>Context precision score (0-1) for RAG retrieval. Null if not evaluated or no chunks.</summary>
    public double? ContextPrecisionScore { get; set; }

    /// <summary>Context recall score (0-1) for RAG retrieval. Measures how much relevant info was retrieved.</summary>
    public double? ContextRecallScore { get; set; }

    /// <summary>Factual correctness (0-1 F1) of the baseline output vs original content.</summary>
    public double? BaselineFactualCorrectnessScore { get; set; }

    /// <summary>Factual correctness (0-1 F1) of the RAG output vs original content.</summary>
    public double? RagFactualCorrectnessScore { get; set; }

    /// <summary>Response relevancy (0-1) of the baseline output to the task.</summary>
    public double? BaselineResponseRelevancyScore { get; set; }

    /// <summary>Response relevancy (0-1) of the RAG output to the task.</summary>
    public double? RagResponseRelevancyScore { get; set; }

    /// <summary>Noise sensitivity (0-1, lower is better) — how much irrelevant chunks harmed RAG output.</summary>
    public double? NoiseSensitivityScore { get; set; }

    /// <summary>Retrieved chunk texts used for RAG improvement. Not persisted — used for evaluation only.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<string>? RetrievedContexts { get; set; }

    /// <summary>The definition of good used for this section. Not persisted — used for evaluation only.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? DefinitionOfGoodText { get; set; }
}

/// <summary>A reference to a corpus chunk used as retrieval context.</summary>
public class ChunkReference
{
    public string SourceFile { get; init; } = "";
    public string Snippet { get; init; } = "";
}
