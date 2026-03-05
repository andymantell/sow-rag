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

        var documents = loader.GetCachedTexts();

        logger.LogInformation("Generating definition from {Count} document(s), {Chunks} chunks",
            documents.Count, retriever.ChunkCount);

        var sections = await builder.BuildDefinitionAsync(documents, definition.SetProgress, ct);

        definition.SetReady(sections, retriever.DocumentCount, retriever.ChunkCount);

        logger.LogInformation("Definition of good is ready ({Count} section(s))", sections.Count);
    }
}
