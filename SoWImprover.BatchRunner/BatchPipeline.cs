using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SoWImprover.Data;
using SoWImprover.Models;
using SoWImprover.Services;

namespace SoWImprover.BatchRunner;

public class BatchPipeline(
    DocumentLoader loader,
    SoWImproverService improver,
    IDbContextFactory<SoWDbContext> dbFactory,
    ConsoleLogger log)
{
    public async Task<(DocumentEntity Entity, ImprovementResult Result)> ProcessDocumentAsync(
        string pdfPath, GoodDefinition definition, CancellationToken ct)
    {
        var fileName = Path.GetFileName(pdfPath);

        log.Log("Extracting text...");
        var text = await loader.ExtractTextAsync(pdfPath, ct);
        var wordCount = text.Split((char[])[' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;
        log.Log($"Extracted {wordCount} words");

        log.Log("Improving sections (baseline + RAG)...");
        var result = await improver.ImproveAsync(text, definition, log.AsProgress(1), ct);

        var recognised = result.Sections.Count(s => !s.Unrecognised);
        var unrecognised = result.Sections.Count(s => s.Unrecognised);
        log.Log($"Improvement complete. {recognised} sections, {unrecognised} unrecognised (skipped).");

        foreach (var sec in result.Sections.Where(s => !s.Unrecognised))
        {
            var chunkCount = sec.RetrievedScores?.Count ?? 0;
            var bestScore = sec.RetrievedScores?.Count > 0 ? sec.RetrievedScores.Max() : 0f;
            log.Log($"  {sec.MatchedSection ?? sec.OriginalTitle}: {chunkCount} chunks, best={bestScore:F3}", indent: 1);
        }

        log.Log("Persisting to database...");
        var entity = await PersistAsync(fileName, text, result, ct);

        return (entity, result);
    }

    private async Task<DocumentEntity> PersistAsync(
        string fileName, string originalText, ImprovementResult result, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var doc = new DocumentEntity
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            OriginalText = originalText,
            UploadedAt = DateTime.UtcNow
        };

        for (var i = 0; i < result.Sections.Count; i++)
        {
            var sec = result.Sections[i];
            var entity = new SectionEntity
            {
                DocumentId = doc.Id,
                SectionIndex = i,
                OriginalTitle = sec.OriginalTitle,
                OriginalContent = sec.OriginalContent,
                ImprovedContent = sec.ImprovedContent,
                BaselineContent = sec.BaselineContent,
                MatchedSection = sec.MatchedSection,
                Unrecognised = sec.Unrecognised,
                Explanation = sec.Explanation,
                RetrievedContextsJson = sec.RetrievedContexts is { Count: > 0 }
                    ? JsonSerializer.Serialize(sec.RetrievedContexts)
                    : null,
                DefinitionOfGoodText = sec.DefinitionOfGoodText,
                Versions =
                [
                    new SectionVersionEntity
                    {
                        VersionNumber = 1,
                        Content = sec.ImprovedContent ?? sec.OriginalContent,
                        CreatedAt = DateTime.UtcNow
                    }
                ]
            };
            doc.Sections.Add(entity);
        }

        db.Documents.Add(doc);
        await db.SaveChangesAsync(ct);
        return doc;
    }
}
