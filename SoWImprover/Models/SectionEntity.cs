namespace SoWImprover.Models;

public class SectionEntity
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public int SectionIndex { get; set; }
    public string OriginalTitle { get; set; } = "";
    public string OriginalContent { get; set; } = "";
    public string? ImprovedContent { get; set; }
    public string? BaselineContent { get; set; }
    public string? MatchedSection { get; set; }
    public string? Explanation { get; set; }
    public bool Unrecognised { get; set; }
    public bool Suppressed { get; set; }
    public int? OriginalQualityScore { get; set; }
    public int? BaselineQualityScore { get; set; }
    public int? RagQualityScore { get; set; }
    public double? BaselineFaithfulnessScore { get; set; }
    public double? RagFaithfulnessScore { get; set; }
    public double? ContextPrecisionScore { get; set; }

    /// <summary>JSON-serialised list of retrieved chunk texts, for evaluation. Null if unrecognised.</summary>
    public string? RetrievedContextsJson { get; set; }
    /// <summary>Definition of good text used for this section, for evaluation. Null if unrecognised.</summary>
    public string? DefinitionOfGoodText { get; set; }

    public DocumentEntity Document { get; set; } = null!;
    public List<SectionVersionEntity> Versions { get; set; } = [];
}
