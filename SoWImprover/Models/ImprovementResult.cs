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
}

/// <summary>A reference to a corpus chunk used as retrieval context.</summary>
public class ChunkReference
{
    public string SourceFile { get; init; } = "";
    public string Snippet { get; init; } = "";
}
