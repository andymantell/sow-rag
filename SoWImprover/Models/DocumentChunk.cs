namespace SoWImprover.Models;

public class DocumentChunk
{
    public string SourceFile { get; init; } = "";
    public string Text { get; init; } = "";
    public int ChunkIndex { get; init; }
}
