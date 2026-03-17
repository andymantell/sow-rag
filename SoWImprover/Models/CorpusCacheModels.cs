namespace SoWImprover.Models;

public sealed class DefinitionCacheFile
{
    public string Fingerprint { get; set; } = "";
    public string Model { get; set; } = "";
    public List<DefinitionCacheSection> Sections { get; set; } = [];
}

public sealed class DefinitionCacheSection
{
    public string Name { get; set; } = "";
    public string Content { get; set; } = "";
}

public sealed class EmbeddingCacheFile
{
    public string Fingerprint { get; set; } = "";
    public string Model { get; set; } = "";
    public List<EmbeddingCacheEntry> Entries { get; set; } = [];
}

public sealed class EmbeddingCacheEntry
{
    public string SourceFile { get; set; } = "";
    public int ChunkIndex { get; set; }
    public int GlobalIndex { get; set; }
    public float[] Vector { get; set; } = [];
}

public sealed class RedactionCacheFile
{
    public string Fingerprint { get; set; } = "";
    public string Model { get; set; } = "";
    public List<RedactionCacheEntry> Entries { get; set; } = [];
}

public sealed class RedactionCacheEntry
{
    public string SourceFile { get; set; } = "";
    public int ChunkIndex { get; set; }
    public string RedactedText { get; set; } = "";
}
