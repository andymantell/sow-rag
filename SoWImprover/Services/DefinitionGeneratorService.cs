using SoWImprover.Models;

namespace SoWImprover.Services;

public class DefinitionGeneratorService(
    CorpusInitialisationService corpusInit,
    GoodDefinition definition) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await corpusInit.InitialiseAsync(
            definition,
            msg => definition.SetProgress(msg),
            ct);
    }
}
