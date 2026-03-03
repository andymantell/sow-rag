namespace SoWImprover.Models;

public class ImprovementResult
{
    public string Original { get; init; } = "";
    public string Improved { get; init; } = "";
    public List<FlaggedSection> FlaggedSections { get; init; } = [];
    public List<ChunkReference> ChunksUsed { get; init; } = [];
}

public class FlaggedSection
{
    public string SectionTitle { get; init; } = "";
    public string Reason { get; init; } = "";
}

public class ChunkReference
{
    public string SourceFile { get; init; } = "";
    public string Snippet { get; init; } = "";
}
