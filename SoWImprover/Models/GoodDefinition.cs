namespace SoWImprover.Models;

/// <summary>
/// Singleton state container for the generated "definition of good" for SoW documents.
/// Written once by <c>DefinitionGeneratorService</c> on a background thread;
/// read by multiple Blazor circuit threads. <see cref="IsReady"/> uses a <c>volatile</c>
/// backing field so readers see a consistent view: once <c>IsReady</c> returns <c>true</c>,
/// <see cref="MarkdownContent"/>, <see cref="DocumentCount"/>, and <see cref="ChunkCount"/>
/// are all guaranteed to hold their final values.
/// </summary>
public class GoodDefinition
{
    // volatile ensures the IsReady write acts as a release fence, and reads act as acquire fences,
    // so readers that see IsReady=true are guaranteed to see the prior non-volatile writes.
    private volatile bool _isReady;

    /// <summary>The generated definition as a markdown string. Populated before <see cref="IsReady"/> is set.</summary>
    public string MarkdownContent { get; private set; } = "";

    /// <summary>Whether the definition has been fully generated and is safe to read.</summary>
    public bool IsReady => _isReady;

    /// <summary>Number of source documents that were analysed to produce this definition.</summary>
    public int DocumentCount { get; private set; }

    /// <summary>Total number of corpus chunks loaded from source documents.</summary>
    public int ChunkCount { get; private set; }

    /// <summary>
    /// Sets all properties and marks the definition as ready.
    /// <see cref="IsReady"/> is set last, using volatile semantics, to ensure all prior
    /// writes are visible to any thread that subsequently reads <see cref="IsReady"/>.
    /// </summary>
    public void SetReady(string markdownContent, int documentCount, int chunkCount)
    {
        MarkdownContent = markdownContent;
        DocumentCount = documentCount;
        ChunkCount = chunkCount;
        _isReady = true; // volatile write — acts as release fence for the writes above
    }
}
