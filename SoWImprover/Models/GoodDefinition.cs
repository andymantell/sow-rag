namespace SoWImprover.Models;

public class GoodDefinition
{
    public string MarkdownContent { get; set; } = "";
    public bool IsReady { get; set; }
    public int DocumentCount { get; set; }
    public int ChunkCount { get; set; }
}
