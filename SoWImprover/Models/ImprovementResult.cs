namespace SoWImprover.Models;

/// <summary>The result of improving an uploaded SoW document.</summary>
public class ImprovementResult
{
    /// <summary>Normalised original text extracted from the uploaded PDF.</summary>
    public string Original { get; init; } = "";

    /// <summary>Improved version of the document as a markdown string.</summary>
    public string Improved { get; init; } = "";

    /// <summary>Per-section annotations describing what was improved.</summary>
    public List<SectionAnnotation> Annotations { get; init; } = [];

    /// <summary>Deduplicated corpus chunks that were used as context during improvement.</summary>
    public List<ChunkReference> ChunksUsed { get; init; } = [];
}

/// <summary>Describes what was improved in a single section of the document.</summary>
public class SectionAnnotation
{
    /// <summary>The section heading as it appeared in the original document.</summary>
    public string SectionTitle { get; init; } = "";

    /// <summary>Bullet-point explanation of the changes made (lines starting with '-').</summary>
    public string Explanation { get; init; } = "";
}

/// <summary>A reference to a corpus chunk used as retrieval context.</summary>
public class ChunkReference
{
    /// <summary>Filename of the source document this chunk came from.</summary>
    public string SourceFile { get; init; } = "";

    /// <summary>Up to 200 characters of the chunk text.</summary>
    public string Snippet { get; init; } = "";
}
