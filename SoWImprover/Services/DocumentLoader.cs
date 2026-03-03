using UglyToad.PdfPig;
using SoWImprover.Models;

namespace SoWImprover.Services;

public class DocumentLoader(IConfiguration config)
{
    private readonly int _chunkSize = config.GetValue<int>("Docs:ChunkSize", 500);
    private readonly int _chunkOverlap = config.GetValue<int>("Docs:ChunkOverlap", 50);

    public List<DocumentChunk> LoadFolder(string folderPath)
    {
        var fullPath = Path.GetFullPath(folderPath);
        var files = Directory.GetFiles(fullPath, "*.pdf", SearchOption.TopDirectoryOnly);

        if (files.Length == 0)
            throw new InvalidOperationException(
                $"No PDFs found in KnownGoodFolder '{fullPath}' — populate the folder before starting.");

        var chunks = new List<DocumentChunk>();
        foreach (var file in files)
        {
            var text = ExtractText(file);
            chunks.AddRange(ChunkText(text, Path.GetFileName(file)));
        }
        return chunks;
    }

    public string ExtractText(string filePath)
    {
        using var doc = PdfDocument.Open(filePath);
        var sb = new System.Text.StringBuilder();
        foreach (var page in doc.GetPages())
            sb.AppendLine(page.Text);
        return sb.ToString();
    }

    public string[] GetPdfFiles(string folderPath)
        => Directory.GetFiles(Path.GetFullPath(folderPath), "*.pdf", SearchOption.TopDirectoryOnly);

    private List<DocumentChunk> ChunkText(string text, string sourceFile)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<DocumentChunk>();
        int i = 0, index = 0;

        while (i < words.Length)
        {
            var take = Math.Min(_chunkSize, words.Length - i);
            chunks.Add(new DocumentChunk
            {
                SourceFile = sourceFile,
                Text = string.Join(" ", words, i, take),
                ChunkIndex = index++
            });
            i += _chunkSize - _chunkOverlap;
        }

        return chunks;
    }
}
