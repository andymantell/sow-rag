using SoWImprover.Models;

namespace SoWImprover.Services;

public class DefinitionGeneratorService(
    DocumentLoader loader,
    DefinitionBuilder builder,
    SimpleRetriever retriever,
    GoodDefinition definition,
    IConfiguration config,
    ILogger<DefinitionGeneratorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var folder = config["Docs:KnownGoodFolder"] ?? "./sample-sows";

        logger.LogInformation("Starting definition generation from folder: {Folder}", folder);

        // Populate counts from the already-loaded retriever
        definition.DocumentCount = retriever.DocumentCount;
        definition.ChunkCount = retriever.ChunkCount;

        // Load full document texts for per-document analysis
        var pdfFiles = loader.GetPdfFiles(folder);
        var documents = new List<(string FileName, string Text)>(pdfFiles.Length);
        foreach (var f in pdfFiles)
            documents.Add((Path.GetFileName(f), await loader.ExtractTextAsync(f, ct)));

        logger.LogInformation("Generating definition from {Count} document(s), {Chunks} chunks",
            documents.Count, retriever.ChunkCount);

        // This will throw and crash the BackgroundService (and app) if any LLM call fails
        var markdown = await builder.BuildDefinitionAsync(documents, ct);

        definition.MarkdownContent = markdown;
        definition.IsReady = true;

        logger.LogInformation("Definition of good is ready ({Length} chars)", markdown.Length);
    }
}
