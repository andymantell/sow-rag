namespace SoWImprover.Models;

/// <summary>A fixed-size text chunk from a corpus document, used for TF-IDF retrieval.</summary>
public class DocumentChunk
{
    /// <summary>Filename of the source PDF (basename only, no path).</summary>
    public string SourceFile { get; init; } = "";

    /// <summary>The chunk text (at most <c>Docs:ChunkSize</c> words).</summary>
    public string Text { get; init; } = "";

    /// <summary>Zero-based index of this chunk within its source document.</summary>
    public int ChunkIndex { get; init; }
}
