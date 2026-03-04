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

        // Reuse texts already extracted during startup chunk-loading — avoids spawning a second
        // Python subprocess per document.
        var documents = loader.GetCachedTexts();

        logger.LogInformation("Generating definition from {Count} document(s), {Chunks} chunks",
            documents.Count, retriever.ChunkCount);

        // This will throw and crash the BackgroundService (and app) if any LLM call fails
        var markdown = await builder.BuildDefinitionAsync(documents, ct);

        definition.SetReady(markdown, retriever.DocumentCount, retriever.ChunkCount);

        logger.LogInformation("Definition of good is ready ({Length} chars)", markdown.Length);
    }
}
