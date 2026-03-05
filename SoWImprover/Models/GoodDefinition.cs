namespace SoWImprover.Models;

/// <summary>A single canonical section with its definition-of-good content.</summary>
public record DefinedSection(string Name, string Content);

/// <summary>
/// Singleton state container for the generated "definition of good" for SoW documents.
/// Written once by <c>DefinitionGeneratorService</c> on a background thread;
/// read by multiple Blazor circuit threads. <see cref="IsReady"/> uses a <c>volatile</c>
/// backing field so readers see a consistent view.
/// </summary>
public class GoodDefinition
{
    private volatile bool _isReady;

    /// <summary>Canonical sections discovered from the corpus, each with its definition content.</summary>
    public IReadOnlyList<DefinedSection> Sections { get; private set; } = [];

    /// <summary>Full definition assembled as markdown for sidebar display.</summary>
    public string MarkdownContent { get; private set; } = "";

    /// <summary>Current progress message during generation. Updated freely from the background thread.</summary>
    public string ProgressMessage { get; private set; } = "";

    /// <summary>Whether the definition has been fully generated and is safe to read.</summary>
    public bool IsReady => _isReady;

    /// <summary>Updates the progress message. Safe to call at any point before SetReady.</summary>
    public void SetProgress(string message) => ProgressMessage = message;

    /// <summary>Number of source documents analysed.</summary>
    public int DocumentCount { get; private set; }

    /// <summary>Total corpus chunks loaded.</summary>
    public int ChunkCount { get; private set; }

    /// <summary>
    /// Sets all properties and marks the definition as ready.
    /// IsReady is set last (volatile write) to act as a release fence.
    /// </summary>
    public void SetReady(IReadOnlyList<DefinedSection> sections, int documentCount, int chunkCount)
    {
        Sections = sections;
        MarkdownContent = string.Join("\n\n", sections.Select(s => $"## {s.Name}\n\n{s.Content}"));
        DocumentCount = documentCount;
        ChunkCount = chunkCount;
        _isReady = true;
    }
}
