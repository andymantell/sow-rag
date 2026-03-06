namespace SoWImprover.Models;

public class SectionEntity
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public int SectionIndex { get; set; }
    public string OriginalTitle { get; set; } = "";
    public string OriginalContent { get; set; } = "";
    public string? ImprovedContent { get; set; }
    public string? MatchedSection { get; set; }
    public string? Explanation { get; set; }
    public bool Unrecognised { get; set; }
    public bool Suppressed { get; set; }

    public DocumentEntity Document { get; set; } = null!;
}
