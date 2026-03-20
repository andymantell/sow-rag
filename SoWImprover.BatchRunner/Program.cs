using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SoWImprover.BatchRunner;
using SoWImprover.Data;
using SoWImprover.Models;
using SoWImprover.Services;

if (args is ["--clean"])
{
    var dbPath = Path.Combine(FindMainAppDir(), "sow-improver.db");
    if (File.Exists(dbPath))
    {
        File.Delete(dbPath);
        Console.WriteLine($"Deleted {dbPath}");
    }
    return 0;
}

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: SoWImprover.BatchRunner <test-folder-path> | --clean");
    return 1;
}

var testFolder = Path.GetFullPath(args[0]);
if (!Directory.Exists(testFolder))
{
    Console.Error.WriteLine($"Test folder not found: {testFolder}");
    return 1;
}

var pdfs = Directory.GetFiles(testFolder, "*.pdf");
if (pdfs.Length == 0)
{
    Console.Error.WriteLine($"No PDF files found in: {testFolder}");
    return 1;
}

var log = new ConsoleLogger();

// Find the main app directory by walking up from executable
var mainAppDir = FindMainAppDir();
var config = new ConfigurationBuilder()
    .SetBasePath(mainAppDir)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

// Resolve corpus folder relative to main app dir (config has relative path like "./sample-sows")
var corpusFolder = config["Docs:KnownGoodFolder"] ?? "./sample-sows";
var resolvedCorpusFolder = Path.GetFullPath(Path.Combine(mainAppDir, corpusFolder));

// Override config with absolute path so CorpusInitialisationService finds it
config["Docs:KnownGoodFolder"] = resolvedCorpusFolder;

// Build DI container (mirrors main app, minus Blazor and hosted services)
var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(config);
services.AddLogging(b => b.AddConsole()
    .SetMinimumLevel(LogLevel.Warning)
    .AddFilter("SoWImprover", LogLevel.Information));
services.AddSingleton<GoodDefinition>();
services.AddSingleton<DocumentLoader>();
services.AddSingleton<FoundryClientFactory>();
services.AddSingleton<IChatService, ChatService>();
services.AddSingleton<IEmbeddingService, EmbeddingService>();
services.AddSingleton<DefinitionBuilder>();
services.AddSingleton<CorpusInitialisationService>();
services.AddSingleton<SoWImproverService>();
services.AddSingleton<EvaluationService>();
services.AddSingleton<IEvaluationSummaryService, EvaluationSummaryService>();
services.AddDbContextFactory<SoWDbContext>(opts =>
    opts.UseSqlite($"Data Source={Path.Combine(mainAppDir, "sow-improver.db")}"));

var sp = services.BuildServiceProvider();

// Ensure DB exists
using (var db = sp.GetRequiredService<IDbContextFactory<SoWDbContext>>().CreateDbContext())
    db.Database.EnsureCreated();

var definition = sp.GetRequiredService<GoodDefinition>();
var corpusInit = sp.GetRequiredService<CorpusInitialisationService>();

log.Log("=== SoW Improver Batch Runner ===");
log.Log($"Test folder: {testFolder} ({pdfs.Length} PDFs found)");
log.Log($"Corpus:      {resolvedCorpusFolder}");
log.Log($"Chat model:  {config["Ollama:ChatModelName"]} | Embedding model: {config["Ollama:EmbeddingModelName"]}");
log.Log("");

// Phase 1: Corpus initialisation
log.Log("--- Corpus Initialisation ---");
try
{
    await corpusInit.InitialiseAsync(definition, msg => log.Log(msg), CancellationToken.None);
}
catch (Exception ex)
{
    log.Log($"FATAL: Corpus initialisation failed: {ex.Message}");
    return 1;
}
log.Log("");

// Phase 2: Document processing + evaluation loop
var dbFactory = sp.GetRequiredService<IDbContextFactory<SoWDbContext>>();
var pipeline = new BatchPipeline(
    sp.GetRequiredService<DocumentLoader>(),
    sp.GetRequiredService<SoWImproverService>(),
    dbFactory,
    config,
    log);
var evalRunner = new EvaluationRunner(
    sp.GetRequiredService<EvaluationService>(),
    sp.GetRequiredService<IEvaluationSummaryService>(),
    dbFactory,
    config,
    log);
var allResults = new List<(DocumentEntity Entity, ImprovementResult Result)>();
var exportPath = Path.Combine(Directory.GetCurrentDirectory(), "experiment-results.json");

for (var i = 0; i < pdfs.Length; i++)
{
    var pdf = pdfs[i];
    log.Log($"=== Document {i + 1}/{pdfs.Length}: {Path.GetFileName(pdf)} ===");

    try
    {
        var (entity, result) = await pipeline.ProcessDocumentAsync(pdf, definition, CancellationToken.None);
        allResults.Add((entity, result));

        log.Log("");
        await evalRunner.EvaluateDocumentAsync(entity, result.Sections, CancellationToken.None);

        // Incremental export after each document
        var corpusDocsIncr = Directory.GetFiles(resolvedCorpusFolder, "*.pdf")
            .Select(Path.GetFileName)
            .Where(f => f is not null)
            .ToList();
        var incrJson = ExperimentExporter.BuildJson(
            allResults,
            corpusFolder: resolvedCorpusFolder,
            corpusDocuments: corpusDocsIncr!,
            totalChunks: definition.ChunkCount,
            chatModel: config["Ollama:ChatModelName"] ?? "unknown",
            embeddingModel: config["Ollama:EmbeddingModelName"] ?? "unknown");
        await ExperimentExporter.WriteAsync(exportPath, incrJson);
        log.Log($"Partial results exported ({allResults.Count}/{pdfs.Length} documents)");
    }
    catch (Exception ex)
    {
        log.Log($"ERROR: Failed to process {Path.GetFileName(pdf)}: {ex.Message}");
        log.Log($"  {ex.GetType().Name}: {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}");
        continue;
    }

    log.Log("");
}

// Phase 3: Export
log.Log("=== All documents processed ===");

var corpusDocs = Directory.GetFiles(resolvedCorpusFolder, "*.pdf")
    .Select(Path.GetFileName)
    .Where(f => f is not null)
    .ToList();

var json = ExperimentExporter.BuildJson(
    allResults,
    corpusFolder: resolvedCorpusFolder,
    corpusDocuments: corpusDocs!,
    totalChunks: definition.ChunkCount,
    chatModel: config["Ollama:ChatModelName"] ?? "unknown",
    embeddingModel: config["Ollama:EmbeddingModelName"] ?? "unknown");

await ExperimentExporter.WriteAsync(exportPath, json);

log.Log($"Results exported to: {exportPath}");
log.Log($"Database at: {Path.Combine(mainAppDir, "sow-improver.db")}");
log.Log("");
log.Log("To generate the analysis report, run:");
log.Log("  claude /experiment-report");

return 0;

static string FindMainAppDir()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        var candidate = Path.Combine(dir.FullName, "SoWImprover", "appsettings.json");
        if (File.Exists(candidate))
            return Path.Combine(dir.FullName, "SoWImprover");
        dir = dir.Parent;
    }
    var fallback = Path.GetFullPath("SoWImprover");
    if (File.Exists(Path.Combine(fallback, "appsettings.json")))
        return fallback;
    throw new FileNotFoundException(
        "Cannot find SoWImprover/appsettings.json. Run from the repo root or ensure the main app directory is an ancestor.");
}
