namespace SoWImprover.Models;

public class DocumentEntity
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = "";
    public string OriginalText { get; set; } = "";
    public DateTime UploadedAt { get; set; }
    public string? EvaluationSummary { get; set; }
    public List<SectionEntity> Sections { get; set; } = [];
}
