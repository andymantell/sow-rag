namespace SoWImprover.Models;

public class ImprovementResult
{
    public string Original { get; init; } = "";
    public string Improved { get; init; } = "";
    public List<SectionAnnotation> Annotations { get; init; } = [];
    public List<ChunkReference> ChunksUsed { get; init; } = [];
}

public class SectionAnnotation
{
    public string SectionTitle { get; init; } = "";
    public string Explanation { get; init; } = "";
}

public class ChunkReference
{
    public string SourceFile { get; init; } = "";
    public string Snippet { get; init; } = "";
}
