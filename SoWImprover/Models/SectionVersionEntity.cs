namespace SoWImprover.Models;

public class SectionVersionEntity
{
    public Guid Id { get; set; }
    public Guid SectionId { get; set; }
    public int VersionNumber { get; set; }
    public string Content { get; set; } = "";
    public DateTime CreatedAt { get; set; }

    public SectionEntity Section { get; set; } = null!;
}
