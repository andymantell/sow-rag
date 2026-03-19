using SoWImprover.Services;

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
    private bool _isReady;
    private volatile string _progressMessage = "";

    /// <summary>
    /// Raised on the background thread whenever progress changes or the definition becomes ready.
    /// Subscribers must marshal to their own synchronisation context (e.g. <c>InvokeAsync</c>).
    /// </summary>
    public event Action? OnChanged;

    /// <summary>Canonical sections discovered from the corpus, each with its definition content.</summary>
    public IReadOnlyList<DefinedSection> Sections { get; private set; } = [];

    /// <summary>Full definition assembled as markdown for sidebar display.</summary>
    public string MarkdownContent { get; private set; } = "";

    /// <summary>Current progress message during generation. Written from the background thread, read from Blazor circuit threads.</summary>
    public string ProgressMessage => _progressMessage;

    /// <summary>Whether the definition has been fully generated and is safe to read.</summary>
    public bool IsReady => Volatile.Read(ref _isReady);

    /// <summary>Updates the progress message and notifies subscribers.</summary>
    public void SetProgress(string message)
    {
        _progressMessage = message;
        OnChanged?.Invoke();
    }

    /// <summary>Number of source documents analysed.</summary>
    public int DocumentCount { get; private set; }

    /// <summary>Total corpus chunks loaded.</summary>
    public int ChunkCount { get; private set; }

    /// <summary>The semantic retriever, available once IsReady is true.</summary>
    public EmbeddingRetriever? Retriever { get; private set; }

    /// <summary>
    /// Sets all properties and marks the definition as ready.
    /// IsReady is set last (volatile write) to act as a release fence.
    /// </summary>
    public void SetReady(
        IReadOnlyList<DefinedSection> sections,
        EmbeddingRetriever retriever,
        int documentCount,
        int chunkCount)
    {
        Sections = sections;
        MarkdownContent = string.Join("\n\n", sections.Select(s => $"## {s.Name}\n\n{s.Content}"));
        Retriever = retriever;
        DocumentCount = documentCount;
        ChunkCount = chunkCount;
        // Full memory barrier ensures all property writes above are visible to other threads
        // before _isReady becomes true. Portable across x86 and ARM.
        Volatile.Write(ref _isReady, true);
        OnChanged?.Invoke();
    }
}
