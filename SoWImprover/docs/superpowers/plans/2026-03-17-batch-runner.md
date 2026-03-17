# Batch Runner Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a console app that batch-processes SoW PDFs through baseline + RAG improvement, runs Ragas evaluation, exports structured results, and provides a Claude CLI slash command for report generation.

**Architecture:** Extract corpus initialisation from `DefinitionGeneratorService` into a shared `CorpusInitialisationService`. New `SoWImprover.BatchRunner` console project wires up the same DI as the main app, orchestrates the pipeline per document, and exports JSON. A `.claude/commands/experiment-report.md` slash command drives report generation via Claude CLI.

**Tech Stack:** .NET 8, xUnit + NSubstitute, SQLite/EF Core, existing SoW Improver services

**Spec:** `docs/superpowers/specs/2026-03-17-batch-runner-design.md`

---

## Chunk 1: Extract CorpusInitialisationService

### Task 1: Extract cache DTOs from DefinitionGeneratorService

The file-scoped cache DTOs (`DefinitionCacheFile`, `EmbeddingCacheFile`, `RedactionCacheFile` and their entry types) at `DefinitionGeneratorService.cs:323-365` are inaccessible outside that file. Extract them into a shared location.

**Files:**
- Create: `SoWImprover/Models/CorpusCacheModels.cs`
- Modify: `SoWImprover/Services/DefinitionGeneratorService.cs:323-365`

- [ ] **Step 1: Create CorpusCacheModels.cs with all cache DTOs**

Keep as mutable classes (not positional records) — the existing code constructs these with object initializer syntax, and `System.Text.Json` deserialization expects parameterless constructors. Just remove the `file` modifier and change to `public sealed class`.

```csharp
namespace SoWImprover.Models;

public sealed class EmbeddingCacheFile
{
    public string Fingerprint { get; set; } = "";
    public string Model { get; set; } = "";
    public List<EmbeddingCacheEntry> Entries { get; set; } = [];
}

public sealed class EmbeddingCacheEntry
{
    public string SourceFile { get; set; } = "";
    public int ChunkIndex { get; set; }
    public int GlobalIndex { get; set; }
    public float[] Vector { get; set; } = [];
}

public sealed class RedactionCacheFile
{
    public string Fingerprint { get; set; } = "";
    public string Model { get; set; } = "";
    public List<RedactionCacheEntry> Entries { get; set; } = [];
}

public sealed class RedactionCacheEntry
{
    public string SourceFile { get; set; } = "";
    public int ChunkIndex { get; set; }
    public string RedactedText { get; set; } = "";
}

public sealed class DefinitionCacheFile
{
    public string Fingerprint { get; set; } = "";
    public string Model { get; set; } = "";
    public List<DefinitionCacheSection> Sections { get; set; } = [];
}

public sealed class DefinitionCacheSection
{
    public string Name { get; set; } = "";
    public string Content { get; set; } = "";
}
```

- [ ] **Step 2: Remove file-scoped DTOs from DefinitionGeneratorService.cs**

Delete lines 323-365 (the `file` record declarations). Add `using SoWImprover.Models;` at the top if not already present.

- [ ] **Step 3: Build and verify no errors**

Run: `dotnet build SoWImprover/SoWImprover.csproj`
Expected: Build succeeds — all references resolve to the new shared types.

- [ ] **Step 4: Run existing tests**

Run: `dotnet test SoWImprover.Tests/SoWImprover.Tests.csproj`
Expected: All tests pass (no behaviour change).

---

### Task 2: Extract CorpusInitialisationService

Extract the corpus init logic from `DefinitionGeneratorService.ExecuteAsync` (lines 17-49) and its private helpers (`ComputeCorpusFingerprint`, `GetOrComputeChunkVectorsAsync`, `RedactChunksAsync`, `GetOrBuildDefinitionAsync`) into a shared service.

**Files:**
- Create: `SoWImprover/Services/CorpusInitialisationService.cs`
- Modify: `SoWImprover/Services/DefinitionGeneratorService.cs`
- Modify: `SoWImprover/Program.cs` (add DI registration)

- [ ] **Step 1: Write the failing test**

Create: `SoWImprover.Tests/Services/CorpusInitialisationServiceTests.cs`

**NOTE:** `DocumentLoader.LoadFolder` and `GetCachedTexts` are not virtual, so NSubstitute cannot intercept them. Before writing this test, make `LoadFolder` and `GetCachedTexts` virtual in `DocumentLoader.cs`. `ExtractTextAsync` is already virtual.

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SoWImprover.Models;
using SoWImprover.Services;

namespace SoWImprover.Tests.Services;

public class CorpusInitialisationServiceTests
{
    [Fact]
    public async Task InitialiseAsync_SetsGoodDefinitionReady()
    {
        // Arrange
        var definition = new GoodDefinition();
        var loader = Substitute.For<DocumentLoader>();
        var embeddingService = Substitute.For<IEmbeddingService>();
        var chatService = Substitute.For<IChatService>();
        var definitionBuilder = new DefinitionBuilder(
            chatService, Substitute.For<ILogger<DefinitionBuilder>>());

        // Stub loader to return minimal data
        var chunks = new List<DocumentChunk>
        {
            new() { SourceFile = "test.pdf", ChunkIndex = 0, Text = "test chunk" }
        };
        loader.LoadFolder(Arg.Any<string>()).Returns(chunks);
        loader.GetCachedTexts().Returns(
            new List<(string, string)> { ("test.pdf", "test text") }
                as IReadOnlyList<(string FileName, string Text)>);

        // Stub embedding to return a single vector
        embeddingService.EmbedBatchAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new float[] { 0.1f, 0.2f } });

        // Stub chat for redaction + definition building
        chatService.CompleteAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("## Acceptance Criteria\n\nGood content");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Docs:KnownGoodFolder"] = "./test-corpus",
                ["Docs:TopKChunks"] = "5",
                ["Docs:MinChunkScore"] = "0.3",
                ["Foundry:UseLocal"] = "true",
                ["Foundry:LocalModelName"] = "test-model",
                ["Ollama:EmbeddingModelName"] = "test-embed",
            })
            .Build();

        var sut = new CorpusInitialisationService(
            loader, embeddingService, chatService, definitionBuilder, config,
            Substitute.For<ILogger<CorpusInitialisationService>>());

        // Act
        await sut.InitialiseAsync(definition, s => { }, CancellationToken.None);

        // Assert
        Assert.True(definition.IsReady);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SoWImprover.Tests --filter "CorpusInitialisationServiceTests" -v n`
Expected: FAIL — `CorpusInitialisationService` does not exist.

- [ ] **Step 3: Create CorpusInitialisationService**

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SoWImprover.Models;

namespace SoWImprover.Services;

public class CorpusInitialisationService(
    DocumentLoader loader,
    IEmbeddingService embeddingService,
    IChatService chatService,
    DefinitionBuilder definitionBuilder,
    IConfiguration configuration,
    ILogger<CorpusInitialisationService> logger)
{
    public async Task InitialiseAsync(
        GoodDefinition definition,
        Action<string> progress,
        CancellationToken ct)
    {
        var folder = configuration["Docs:KnownGoodFolder"] ?? "./sample-sows";
        var topK = configuration.GetValue("Docs:TopKChunks", 5);
        var minScore = configuration.GetValue("Docs:MinChunkScore", 0.3f);

        progress("Loading corpus PDFs...");
        var chunks = loader.LoadFolder(folder);
        var documents = loader.GetCachedTexts();
        logger.LogInformation("Loaded {Count} chunks from {DocCount} documents", chunks.Count, documents.Count);

        progress($"Embedding {chunks.Count} chunks...");
        var vectors = await GetOrComputeChunkVectorsAsync(chunks, folder, progress, ct);

        progress("Redacting chunks...");
        await RedactChunksAsync(chunks, folder, progress, ct);

        progress("Building section definitions...");
        var sections = await GetOrBuildDefinitionAsync(documents, folder, progress, ct);

        var retriever = new EmbeddingRetriever(chunks, vectors, embeddingService, topK, minScore);
        definition.SetReady(sections, retriever, documents.Count, chunks.Count);
        progress($"Corpus ready. {documents.Count} documents, {chunks.Count} chunks, {sections.Count} sections defined.");
    }

    // ── All methods below are moved verbatim from DefinitionGeneratorService ──

    // Move verbatim from DefinitionGeneratorService.cs:310-320 — keep exact
    // same ordering and casing to preserve cache compatibility.
    internal static string ComputeCorpusFingerprint(string folder)
    {
        var entries = Directory.GetFiles(folder, "*.pdf")
            .OrderBy(f => f)
            .Select(f => $"{Path.GetFileName(f)}|{new FileInfo(f).Length}");
        var raw = string.Join("\n", entries);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // GetOrComputeChunkVectorsAsync — move verbatim from lines 128-204
    // RedactChunksAsync — move verbatim from lines 211-304
    // GetOrBuildDefinitionAsync — move verbatim from lines 56-121

    // NOTE TO IMPLEMENTER: Move these three methods verbatim from
    // DefinitionGeneratorService.cs. They reference:
    //   - embeddingService (constructor param)
    //   - chatService (constructor param, for redaction)
    //   - definitionBuilder (constructor param)
    //   - configuration (constructor param, for model names)
    //   - logger (constructor param)
    // Replace all `_embeddingService` etc. field references with the
    // primary constructor parameter names.
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test SoWImprover.Tests --filter "CorpusInitialisationServiceTests" -v n`
Expected: PASS

- [ ] **Step 5: Refactor DefinitionGeneratorService to use CorpusInitialisationService**

Modify `DefinitionGeneratorService.cs` to become a thin wrapper:

Keep it simple — `BackgroundServiceExceptionBehavior.StopHost` is already configured in `Program.cs`, so no try/catch needed:

```csharp
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
```

- [ ] **Step 6: Register CorpusInitialisationService in Program.cs**

Add before the `AddHostedService<DefinitionGeneratorService>()` line:

```csharp
builder.Services.AddSingleton<CorpusInitialisationService>();
```

- [ ] **Step 7: Build and run all tests**

Run: `dotnet build SoWImprover/SoWImprover.csproj && dotnet test SoWImprover.Tests/SoWImprover.Tests.csproj`
Expected: Build succeeds, all tests pass.

---

## Chunk 2: Create BatchRunner Console Project

### Task 3: Scaffold the console project

**Files:**
- Create: `SoWImprover.BatchRunner/SoWImprover.BatchRunner.csproj`
- Modify: `SoWImprover.slnx`

- [ ] **Step 1: Create the project directory**

Run: `mkdir SoWImprover.BatchRunner`

- [ ] **Step 2: Create the .csproj file**

Create: `SoWImprover.BatchRunner/SoWImprover.BatchRunner.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\SoWImprover\SoWImprover.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Add to solution**

Add a new line to `SoWImprover.slnx`:

```xml
<Project Path="SoWImprover.BatchRunner/SoWImprover.BatchRunner.csproj" />
```

- [ ] **Step 4: Create minimal Program.cs**

Create: `SoWImprover.BatchRunner/Program.cs`

```csharp
using SoWImprover.BatchRunner;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: SoWImprover.BatchRunner <test-folder-path>");
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

Console.WriteLine($"Found {pdfs.Length} test PDFs in: {testFolder}");
return 0;
```

- [ ] **Step 5: Build**

Run: `dotnet build SoWImprover.BatchRunner/SoWImprover.BatchRunner.csproj`
Expected: Build succeeds.

---

### Task 4: Add test project reference and ConsoleLogger helper

Before writing batch runner tests, add a project reference to the test project.

**Files:**
- Modify: `SoWImprover.Tests/SoWImprover.Tests.csproj`

- [ ] **Step 0: Add ProjectReference to batch runner**

Add to `SoWImprover.Tests/SoWImprover.Tests.csproj` in the `<ItemGroup>` with project references:

```xml
<ProjectReference Include="..\SoWImprover.BatchRunner\SoWImprover.BatchRunner.csproj" />
```

### ConsoleLogger helper

A small helper for consistent timestamped console output.

**Files:**
- Create: `SoWImprover.BatchRunner/ConsoleLogger.cs`
- Test: `SoWImprover.Tests/BatchRunner/ConsoleLoggerTests.cs`

- [ ] **Step 1: Write the failing test**

Create: `SoWImprover.Tests/BatchRunner/ConsoleLoggerTests.cs`

```csharp
using SoWImprover.BatchRunner;

namespace SoWImprover.Tests.BatchRunner;

public class ConsoleLoggerTests
{
    [Fact]
    public void Log_IncludesTimestamp()
    {
        var writer = new StringWriter();
        var logger = new ConsoleLogger(writer);

        logger.Log("Test message");

        var output = writer.ToString().TrimEnd();
        // Format: [HH:mm:ss] Test message
        Assert.Matches(@"^\[\d{2}:\d{2}:\d{2}\] Test message$", output);
    }

    [Fact]
    public void Log_WithIndent_AddsSpaces()
    {
        var writer = new StringWriter();
        var logger = new ConsoleLogger(writer);

        logger.Log("Indented", indent: 1);

        var output = writer.ToString().TrimEnd();
        Assert.Matches(@"^\[\d{2}:\d{2}:\d{2}\]   Indented$", output);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SoWImprover.Tests --filter "ConsoleLoggerTests" -v n`
Expected: FAIL — `ConsoleLogger` does not exist.

- [ ] **Step 3: Create ConsoleLogger**

Create: `SoWImprover.BatchRunner/ConsoleLogger.cs`

```csharp
namespace SoWImprover.BatchRunner;

public class ConsoleLogger(TextWriter writer)
{
    public ConsoleLogger() : this(Console.Out) { }

    public void Log(string message, int indent = 0)
    {
        var prefix = indent > 0 ? new string(' ', indent * 2) : "";
        writer.WriteLine($"[{DateTime.Now:HH:mm:ss}] {prefix}{message}");
    }

    public IProgress<string> AsProgress(int indent = 0) =>
        new SynchronousProgress(msg => Log(msg, indent));

    // Progress<T> posts to SynchronizationContext (thread pool in console apps),
    // causing out-of-order messages. Use synchronous delivery instead.
    private sealed class SynchronousProgress(Action<string> callback) : IProgress<string>
    {
        public void Report(string value) => callback(value);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test SoWImprover.Tests --filter "ConsoleLoggerTests" -v n`
Expected: PASS

---

### Task 5: Wire up DI and corpus initialisation in Program.cs

**Files:**
- Modify: `SoWImprover.BatchRunner/Program.cs`

- [ ] **Step 1: Implement DI setup and corpus init**

Replace `SoWImprover.BatchRunner/Program.cs` with:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SoWImprover.BatchRunner;
using SoWImprover.Data;
using SoWImprover.Models;
using SoWImprover.Services;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: SoWImprover.BatchRunner <test-folder-path>");
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

// Find the main app directory by walking up from the executable until we find appsettings.json
var mainAppDir = FindMainAppDir();
var config = new ConfigurationBuilder()
    .SetBasePath(mainAppDir)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

static string FindMainAppDir()
{
    // Walk up from executable looking for SoWImprover/appsettings.json
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        var candidate = Path.Combine(dir.FullName, "SoWImprover", "appsettings.json");
        if (File.Exists(candidate))
            return Path.Combine(dir.FullName, "SoWImprover");
        dir = dir.Parent;
    }
    // Fallback: assume we're running from repo root via dotnet run
    var fallback = Path.GetFullPath("SoWImprover");
    if (File.Exists(Path.Combine(fallback, "appsettings.json")))
        return fallback;
    throw new FileNotFoundException(
        "Cannot find SoWImprover/appsettings.json. Run from the repo root or ensure the main app directory is an ancestor.");
}

// Build DI container (mirrors Program.cs in main app, minus hosted services and Blazor)
var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(config);
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
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
services.AddSingleton<GpuMemoryManager>();
services.AddDbContextFactory<SoWDbContext>(opts =>
    opts.UseSqlite($"Data Source={Path.Combine(mainAppDir, "sow-improver.db")}"));

var sp = services.BuildServiceProvider();

// Ensure DB exists
using (var db = sp.GetRequiredService<IDbContextFactory<SoWDbContext>>().CreateDbContext())
    db.Database.EnsureCreated();

var definition = sp.GetRequiredService<GoodDefinition>();
var corpusInit = sp.GetRequiredService<CorpusInitialisationService>();
var corpusFolder = config["Docs:KnownGoodFolder"] ?? "./sample-sows";

log.Log("=== SoW Improver Batch Runner ===");
log.Log($"Test folder: {testFolder} ({pdfs.Length} PDFs found)");
log.Log($"Corpus:      {Path.GetFullPath(Path.Combine(mainAppDir, corpusFolder))}");
log.Log($"Chat model:  {config["Foundry:LocalModelName"]} | Embedding model: {config["Ollama:EmbeddingModelName"]}");
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

// Phase 2 & 3: see Task 6 and Task 7
log.Log("Pipeline not yet implemented — corpus init only.");
return 0;
```

- [ ] **Step 2: Build**

Run: `dotnet build SoWImprover.BatchRunner/SoWImprover.BatchRunner.csproj`
Expected: Build succeeds.

---

### Task 6: Implement per-document improvement + persistence

**Prerequisite:** Make `SoWImproverService.ImproveAsync` virtual so NSubstitute can intercept it. Also make `EvaluationService.EvaluateStreamingAsync` virtual (needed in Task 7). These are concrete classes without interfaces — adding `virtual` is the minimal change to enable testing.

This is the core pipeline loop: for each PDF, extract text, improve (baseline + RAG), persist to SQLite.

**Files:**
- Create: `SoWImprover.BatchRunner/BatchPipeline.cs`
- Test: `SoWImprover.Tests/BatchRunner/BatchPipelineTests.cs`
- Modify: `SoWImprover.BatchRunner/Program.cs`

- [ ] **Step 1: Write the failing test**

Create: `SoWImprover.Tests/BatchRunner/BatchPipelineTests.cs`

Test that the pipeline persists a document with sections to the database:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SoWImprover.BatchRunner;
using SoWImprover.Data;
using SoWImprover.Models;
using SoWImprover.Services;

namespace SoWImprover.Tests.BatchRunner;

public class BatchPipelineTests
{
    [Fact]
    public async Task ProcessDocumentAsync_PersistsDocumentAndSections()
    {
        // Arrange — in-memory SQLite
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        connection.Open();
        var services = new ServiceCollection();
        services.AddDbContextFactory<SoWDbContext>(opts => opts.UseSqlite(connection));
        var sp = services.BuildServiceProvider();
        var dbFactory = sp.GetRequiredService<IDbContextFactory<SoWDbContext>>();
        await using (var db = await dbFactory.CreateDbContextAsync())
            await db.Database.EnsureCreatedAsync();

        var loader = Substitute.For<DocumentLoader>();
        loader.ExtractTextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("# Introduction\n\nSome text here.");

        var improver = Substitute.For<SoWImproverService>();
        var result = new ImprovementResult
        {
            Sections =
            [
                new SectionResult
                {
                    OriginalTitle = "Introduction",
                    OriginalContent = "Some text here.",
                    BaselineContent = "Improved baseline.",
                    ImprovedContent = "Improved with RAG.",
                    MatchedSection = "Introduction/Background",
                    Explanation = "Added structure.",
                    RetrievedContexts = ["chunk1", "chunk2"],
                    RetrievedScores = [0.72f, 0.55f],
                    DefinitionOfGoodText = "Good intro definition."
                }
            ],
            ChunksUsed = []
        };
        improver.ImproveAsync(
            Arg.Any<string>(), Arg.Any<GoodDefinition>(),
            Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>())
            .Returns(result);

        var log = new ConsoleLogger(TextWriter.Null);
        var pipeline = new BatchPipeline(loader, improver, dbFactory, log);

        // Act
        var (docEntity, sectionResults) = await pipeline.ProcessDocumentAsync(
            "test.pdf", new GoodDefinition(), CancellationToken.None);

        // Assert — check database
        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var doc = await verifyDb.Documents.Include(d => d.Sections).FirstAsync();
        Assert.Equal("test.pdf", doc.FileName);
        Assert.Single(doc.Sections);
        Assert.Equal("Introduction/Background", doc.Sections[0].MatchedSection);
        Assert.Equal("[\"chunk1\",\"chunk2\"]", doc.Sections[0].RetrievedContextsJson);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SoWImprover.Tests --filter "BatchPipelineTests" -v n`
Expected: FAIL — `BatchPipeline` does not exist.

- [ ] **Step 3: Create BatchPipeline**

Create: `SoWImprover.BatchRunner/BatchPipeline.cs`

```csharp
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
    /// <summary>
    /// Extracts, improves (baseline + RAG), and persists a single document.
    /// Returns the persisted entity and in-memory section results (for export).
    /// </summary>
    public async Task<(DocumentEntity Entity, ImprovementResult Result)> ProcessDocumentAsync(
        string pdfPath, GoodDefinition definition, CancellationToken ct)
    {
        var fileName = Path.GetFileName(pdfPath);

        log.Log($"Extracting text...", indent: 0);
        var text = await loader.ExtractTextAsync(pdfPath, ct);
        var wordCount = text.Split((char[])[' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;
        log.Log($"Extracted {wordCount} words");

        log.Log("Improving sections (baseline + RAG)...");
        var result = await improver.ImproveAsync(text, definition, log.AsProgress(1), ct);

        var recognised = result.Sections.Count(s => !s.Unrecognised);
        var unrecognised = result.Sections.Count(s => s.Unrecognised);
        log.Log($"Improvement complete. {recognised} sections, {unrecognised} unrecognised (skipped).");

        // Log retrieval info per section
        foreach (var sec in result.Sections.Where(s => !s.Unrecognised))
        {
            var chunkCount = sec.RetrievedScores?.Count ?? 0;
            var bestScore = sec.RetrievedScores?.Count > 0 ? sec.RetrievedScores.Max() : 0f;
            log.Log($"  {sec.MatchedSection ?? sec.OriginalTitle}: {chunkCount} chunks, best={bestScore:F3}", indent: 1);
        }

        // Persist
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test SoWImprover.Tests --filter "BatchPipelineTests" -v n`
Expected: PASS

- [ ] **Step 5: Wire BatchPipeline into Program.cs**

Add DI registration and replace the "not yet implemented" line with the document loop.

In the DI section of Program.cs, add:
```csharp
services.AddSingleton<BatchPipeline>();
```

Replace the Phase 2 placeholder with:

```csharp
var pipeline = sp.GetRequiredService<BatchPipeline>();
var gpuMemory = sp.GetRequiredService<GpuMemoryManager>();
var allResults = new List<(DocumentEntity Entity, ImprovementResult Result)>();

for (var i = 0; i < pdfs.Length; i++)
{
    var pdf = pdfs[i];
    log.Log($"=== Document {i + 1}/{pdfs.Length}: {Path.GetFileName(pdf)} ===");

    try
    {
        await gpuMemory.PrepareForImprovementAsync();
        var (entity, result) = await pipeline.ProcessDocumentAsync(pdf, definition, CancellationToken.None);
        allResults.Add((entity, result));
    }
    catch (Exception ex)
    {
        log.Log($"ERROR: Failed to process {Path.GetFileName(pdf)}: {ex.Message}");
        log.Log($"  {ex.GetType().Name}: {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}");
        continue;
    }

    log.Log("");
}
```

- [ ] **Step 6: Build**

Run: `dotnet build SoWImprover.BatchRunner/SoWImprover.BatchRunner.csproj`
Expected: Build succeeds.

---

### Task 7: Add evaluation pipeline

**Files:**
- Create: `SoWImprover.BatchRunner/EvaluationRunner.cs`
- Test: `SoWImprover.Tests/BatchRunner/EvaluationRunnerTests.cs`
- Modify: `SoWImprover.BatchRunner/Program.cs`

- [ ] **Step 1: Write the failing test**

Create: `SoWImprover.Tests/BatchRunner/EvaluationRunnerTests.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SoWImprover.BatchRunner;
using SoWImprover.Data;
using SoWImprover.Models;
using SoWImprover.Services;

namespace SoWImprover.Tests.BatchRunner;

public class EvaluationRunnerTests
{
    [Fact]
    public async Task EvaluateDocumentAsync_PersistsScoresAndSummary()
    {
        // Arrange — in-memory SQLite with a pre-persisted document
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        connection.Open();
        var services = new ServiceCollection();
        services.AddDbContextFactory<SoWDbContext>(opts => opts.UseSqlite(connection));
        var sp = services.BuildServiceProvider();
        var dbFactory = sp.GetRequiredService<IDbContextFactory<SoWDbContext>>();
        await using (var db = await dbFactory.CreateDbContextAsync())
            await db.Database.EnsureCreatedAsync();

        // Pre-persist a document with one section
        var docId = Guid.NewGuid();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var doc = new DocumentEntity
            {
                Id = docId, FileName = "test.pdf",
                OriginalText = "original", UploadedAt = DateTime.UtcNow
            };
            doc.Sections.Add(new SectionEntity
            {
                DocumentId = docId, SectionIndex = 0,
                OriginalTitle = "Scope", OriginalContent = "Original scope",
                BaselineContent = "Baseline scope", ImprovedContent = "RAG scope",
                MatchedSection = "Scope of Work",
                RetrievedContextsJson = "[\"ctx1\"]",
                DefinitionOfGoodText = "Good scope"
            });
            db.Documents.Add(doc);
            await db.SaveChangesAsync();
        }

        // Load entity for the test
        DocumentEntity entity;
        await using (var db = await dbFactory.CreateDbContextAsync())
            entity = await db.Documents.Include(d => d.Sections).FirstAsync(d => d.Id == docId);

        // Stub evaluation service
        var evaluator = Substitute.For<EvaluationService>();
        var scores = new EvaluationService.SectionScores { RagQualityScore = 4, BaselineQualityScore = 3 };
        evaluator.EvaluateStreamingAsync(Arg.Any<List<EvaluationService.SectionInput>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { (0, scores) }.ToAsyncEnumerable());

        var summaryService = Substitute.For<IEvaluationSummaryService>();
        summaryService.GenerateSummaryAsync(
            Arg.Any<List<SectionSummaryInput>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("Test summary");

        var sectionResults = new List<SectionResult>
        {
            new()
            {
                OriginalContent = "Original scope",
                BaselineContent = "Baseline scope",
                ImprovedContent = "RAG scope",
                MatchedSection = "Scope of Work",
                RetrievedContexts = ["ctx1"],
                RetrievedScores = [0.7f],
                DefinitionOfGoodText = "Good scope"
            }
        };

        var log = new ConsoleLogger(TextWriter.Null);
        var runner = new EvaluationRunner(evaluator, summaryService, dbFactory, log);

        // Act
        await runner.EvaluateDocumentAsync(entity, sectionResults, CancellationToken.None);

        // Assert
        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var doc = await verifyDb.Documents.Include(d => d.Sections).FirstAsync();
        Assert.Equal("Test summary", doc.EvaluationSummary);
        Assert.Equal(4, doc.Sections[0].RagQualityScore);
        Assert.Equal(3, doc.Sections[0].BaselineQualityScore);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SoWImprover.Tests --filter "EvaluationRunnerTests" -v n`
Expected: FAIL — `EvaluationRunner` does not exist.

- [ ] **Step 3: Create EvaluationRunner**

Create: `SoWImprover.BatchRunner/EvaluationRunner.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using SoWImprover.Data;
using SoWImprover.Models;
using SoWImprover.Services;

namespace SoWImprover.BatchRunner;

public class EvaluationRunner(
    EvaluationService evaluator,
    IEvaluationSummaryService summaryService,
    IDbContextFactory<SoWDbContext> dbFactory,
    ConsoleLogger log)
{
    public async Task EvaluateDocumentAsync(
        DocumentEntity entity,
        IReadOnlyList<SectionResult> sectionResults,
        CancellationToken ct)
    {
        // Build evaluation inputs — only recognised sections with baseline content
        var evaluable = new List<(int EntitySectionIndex, SectionResult Result)>();
        for (var i = 0; i < entity.Sections.Count; i++)
        {
            var sec = entity.Sections[i];
            var result = sectionResults[i];
            if (!sec.Unrecognised && sec.BaselineContent is not null)
                evaluable.Add((i, result));
        }

        if (evaluable.Count == 0)
        {
            log.Log("No sections to evaluate.");
            return;
        }

        var inputs = evaluable.Select(e => new EvaluationService.SectionInput
        {
            Original = e.Result.OriginalContent,
            Baseline = e.Result.BaselineContent!,
            RagImproved = e.Result.ImprovedContent!,
            RetrievedContexts = e.Result.RetrievedContexts ?? [],
            DefinitionOfGood = e.Result.DefinitionOfGoodText ?? ""
        }).ToList();

        log.Log("Running evaluation...");
        var evalIndex = 0;
        await foreach (var (streamIdx, scores) in evaluator.EvaluateStreamingAsync(inputs, ct))
        {
            var (entityIdx, result) = evaluable[streamIdx];
            var sec = entity.Sections[entityIdx];
            evalIndex++;

            // Merge scores into section result (in-memory, for export)
            // Use null-coalescing — EvaluateStreamingAsync can yield partial updates
            result.OriginalQualityScore = scores.OriginalQualityScore ?? result.OriginalQualityScore;
            result.BaselineQualityScore = scores.BaselineQualityScore ?? result.BaselineQualityScore;
            result.RagQualityScore = scores.RagQualityScore ?? result.RagQualityScore;
            result.BaselineFaithfulnessScore = scores.BaselineFaithfulnessScore ?? result.BaselineFaithfulnessScore;
            result.RagFaithfulnessScore = scores.RagFaithfulnessScore ?? result.RagFaithfulnessScore;
            result.BaselineFactualCorrectnessScore = scores.BaselineFactualCorrectnessScore ?? result.BaselineFactualCorrectnessScore;
            result.RagFactualCorrectnessScore = scores.RagFactualCorrectnessScore ?? result.RagFactualCorrectnessScore;
            result.BaselineResponseRelevancyScore = scores.BaselineResponseRelevancyScore ?? result.BaselineResponseRelevancyScore;
            result.RagResponseRelevancyScore = scores.RagResponseRelevancyScore ?? result.RagResponseRelevancyScore;
            result.ContextPrecisionScore = scores.ContextPrecisionScore ?? result.ContextPrecisionScore;
            result.ContextRecallScore = scores.ContextRecallScore ?? result.ContextRecallScore;
            result.NoiseSensitivityScore = scores.NoiseSensitivityScore ?? result.NoiseSensitivityScore;

            // Persist scores to DB
            await PersistScoresAsync(sec, scores, ct);

            // Log
            log.Log($"  [{evalIndex}/{evaluable.Count}] {sec.MatchedSection ?? sec.OriginalTitle}", indent: 1);
            log.Log($"    Original: {scores.OriginalQualityScore} | Baseline: {scores.BaselineQualityScore} | RAG: {scores.RagQualityScore}", indent: 2);
            log.Log($"    Faithfulness — baseline: {scores.BaselineFaithfulnessScore:F2} | RAG: {scores.RagFaithfulnessScore:F2}", indent: 2);
            log.Log($"    Factual correctness — baseline: {scores.BaselineFactualCorrectnessScore:F2} | RAG: {scores.RagFactualCorrectnessScore:F2}", indent: 2);
            log.Log($"    Response relevancy — baseline: {scores.BaselineResponseRelevancyScore:F2} | RAG: {scores.RagResponseRelevancyScore:F2}", indent: 2);
            log.Log($"    Context — precision: {scores.ContextPrecisionScore:F2} | recall: {scores.ContextRecallScore:F2} | noise: {scores.NoiseSensitivityScore:F2}", indent: 2);
        }

        log.Log("Evaluation complete.");

        // Generate summary
        log.Log("Generating summary...");
        var summaryInputs = evaluable
            .Where(e => e.Result.RagQualityScore.HasValue)
            .Select(e =>
            {
                var sec = entity.Sections[e.EntitySectionIndex];
                var r = e.Result;
                return new SectionSummaryInput
                {
                    Title = sec.OriginalTitle,
                    OriginalContent = r.OriginalContent,
                    RagImprovedContent = r.ImprovedContent,
                    OriginalQualityScore = r.OriginalQualityScore,
                    BaselineQualityScore = r.BaselineQualityScore,
                    RagQualityScore = r.RagQualityScore,
                    BaselineFaithfulnessScore = r.BaselineFaithfulnessScore,
                    RagFaithfulnessScore = r.RagFaithfulnessScore,
                    BaselineFactualCorrectnessScore = r.BaselineFactualCorrectnessScore,
                    RagFactualCorrectnessScore = r.RagFactualCorrectnessScore,
                    BaselineResponseRelevancyScore = r.BaselineResponseRelevancyScore,
                    RagResponseRelevancyScore = r.RagResponseRelevancyScore,
                    ContextPrecisionScore = r.ContextPrecisionScore,
                    ContextRecallScore = r.ContextRecallScore,
                    NoiseSensitivityScore = r.NoiseSensitivityScore
                };
            }).ToList();

        var summary = await summaryService.GenerateSummaryAsync(summaryInputs, evaluable.Count, ct);
        log.Log($"Summary: {(string.IsNullOrWhiteSpace(summary) ? "(empty)" : "done")}");

        // Persist summary
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Attach(entity);
        entity.EvaluationSummary = summary;
        db.Entry(entity).Property(d => d.EvaluationSummary).IsModified = true;
        await db.SaveChangesAsync(ct);
    }

    private async Task PersistScoresAsync(
        SectionEntity sec, EvaluationService.SectionScores scores, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Attach(sec);
        sec.OriginalQualityScore = scores.OriginalQualityScore;
        sec.BaselineQualityScore = scores.BaselineQualityScore;
        sec.RagQualityScore = scores.RagQualityScore;
        sec.BaselineFaithfulnessScore = scores.BaselineFaithfulnessScore;
        sec.RagFaithfulnessScore = scores.RagFaithfulnessScore;
        sec.BaselineFactualCorrectnessScore = scores.BaselineFactualCorrectnessScore;
        sec.RagFactualCorrectnessScore = scores.RagFactualCorrectnessScore;
        sec.BaselineResponseRelevancyScore = scores.BaselineResponseRelevancyScore;
        sec.RagResponseRelevancyScore = scores.RagResponseRelevancyScore;
        sec.ContextPrecisionScore = scores.ContextPrecisionScore;
        sec.ContextRecallScore = scores.ContextRecallScore;
        sec.NoiseSensitivityScore = scores.NoiseSensitivityScore;

        foreach (var prop in new[]
        {
            nameof(SectionEntity.OriginalQualityScore),
            nameof(SectionEntity.BaselineQualityScore),
            nameof(SectionEntity.RagQualityScore),
            nameof(SectionEntity.BaselineFaithfulnessScore),
            nameof(SectionEntity.RagFaithfulnessScore),
            nameof(SectionEntity.BaselineFactualCorrectnessScore),
            nameof(SectionEntity.RagFactualCorrectnessScore),
            nameof(SectionEntity.BaselineResponseRelevancyScore),
            nameof(SectionEntity.RagResponseRelevancyScore),
            nameof(SectionEntity.ContextPrecisionScore),
            nameof(SectionEntity.ContextRecallScore),
            nameof(SectionEntity.NoiseSensitivityScore),
        })
        {
            db.Entry(sec).Property(prop).IsModified = true;
        }

        await db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test SoWImprover.Tests --filter "EvaluationRunnerTests" -v n`
Expected: PASS

- [ ] **Step 5: Wire into Program.cs**

Add DI registration:
```csharp
services.AddSingleton<EvaluationRunner>();
```

After the improvement loop in Program.cs, add evaluation:

```csharp
var evalRunner = sp.GetRequiredService<EvaluationRunner>();

for (var i = 0; i < allResults.Count; i++)
{
    var (entity, result) = allResults[i];
    log.Log($"=== Evaluating {i + 1}/{allResults.Count}: {entity.FileName} ===");

    try
    {
        await evalRunner.EvaluateDocumentAsync(entity, result.Sections, CancellationToken.None);
    }
    catch (Exception ex)
    {
        log.Log($"ERROR: Evaluation failed for {entity.FileName}: {ex.Message}");
        continue;
    }

    log.Log("");
}
```

- [ ] **Step 6: Build and run all tests**

Run: `dotnet build SoWImprover.BatchRunner/SoWImprover.BatchRunner.csproj && dotnet test SoWImprover.Tests -v n`
Expected: Build succeeds, all tests pass.

---

## Chunk 3: Data Export, Slash Command, Documentation

### Task 8: Implement JSON export

**Files:**
- Create: `SoWImprover.BatchRunner/ExperimentExporter.cs`
- Test: `SoWImprover.Tests/BatchRunner/ExperimentExporterTests.cs`
- Modify: `SoWImprover.BatchRunner/Program.cs`

- [ ] **Step 1: Write the failing test**

Create: `SoWImprover.Tests/BatchRunner/ExperimentExporterTests.cs`

```csharp
using System.Text.Json;
using SoWImprover.BatchRunner;
using SoWImprover.Models;

namespace SoWImprover.Tests.BatchRunner;

public class ExperimentExporterTests
{
    [Fact]
    public void Export_ProducesValidJson()
    {
        var results = new List<(DocumentEntity Entity, ImprovementResult Result)>
        {
            (
                new DocumentEntity
                {
                    Id = Guid.NewGuid(), FileName = "test.pdf",
                    OriginalText = "text", UploadedAt = DateTime.UtcNow,
                    EvaluationSummary = "Summary here",
                    Sections =
                    [
                        new SectionEntity
                        {
                            SectionIndex = 0, OriginalTitle = "Scope",
                            OriginalContent = "Original", MatchedSection = "Scope of Work",
                            BaselineContent = "Baseline", ImprovedContent = "RAG",
                            RagQualityScore = 4, BaselineQualityScore = 3
                        }
                    ]
                },
                new ImprovementResult
                {
                    Sections =
                    [
                        new SectionResult
                        {
                            OriginalTitle = "Scope", OriginalContent = "Original",
                            MatchedSection = "Scope of Work",
                            BaselineContent = "Baseline", ImprovedContent = "RAG",
                            RetrievedScores = [0.72f, 0.55f],
                            RetrievedContexts = ["chunk1", "chunk2"],
                            RagQualityScore = 4, BaselineQualityScore = 3
                        }
                    ]
                }
            )
        };

        var json = ExperimentExporter.BuildJson(
            results,
            corpusFolder: "./sample-sows",
            corpusDocuments: ["doc1.pdf", "doc2.pdf"],
            totalChunks: 42,
            chatModel: "phi-4",
            embeddingModel: "nomic-embed-text");

        // Verify it's valid JSON
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("phi-4", root.GetProperty("corpus").GetProperty("chatModel").GetString());
        Assert.Equal(42, root.GetProperty("corpus").GetProperty("totalChunks").GetInt32());
        Assert.Single(root.GetProperty("testDocuments").EnumerateArray());

        var testDoc = root.GetProperty("testDocuments")[0];
        Assert.Equal("test.pdf", testDoc.GetProperty("fileName").GetString());
        Assert.Equal("Summary here", testDoc.GetProperty("evaluationSummary").GetString());

        var section = testDoc.GetProperty("sections")[0];
        Assert.Equal(4, section.GetProperty("scores").GetProperty("ragQuality").GetInt32());
        Assert.Equal(2, section.GetProperty("retrievedChunkCount").GetInt32());
        Assert.Equal("chunk1", section.GetProperty("retrievedContexts")[0].GetString());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SoWImprover.Tests --filter "ExperimentExporterTests" -v n`
Expected: FAIL — `ExperimentExporter` does not exist.

- [ ] **Step 3: Create ExperimentExporter**

Create: `SoWImprover.BatchRunner/ExperimentExporter.cs`

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using SoWImprover.Models;

namespace SoWImprover.BatchRunner;

public static class ExperimentExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string BuildJson(
        IReadOnlyList<(DocumentEntity Entity, ImprovementResult Result)> results,
        string corpusFolder,
        IReadOnlyList<string> corpusDocuments,
        int totalChunks,
        string chatModel,
        string embeddingModel)
    {
        var export = new
        {
            ExportedAt = DateTime.UtcNow,
            Corpus = new
            {
                Folder = corpusFolder,
                Documents = corpusDocuments,
                TotalChunks = totalChunks,
                EmbeddingModel = embeddingModel,
                ChatModel = chatModel
            },
            TestDocuments = results.Select(r => BuildDocumentExport(r.Entity, r.Result)).ToList()
        };

        return JsonSerializer.Serialize(export, JsonOptions);
    }

    private static object BuildDocumentExport(DocumentEntity entity, ImprovementResult result)
    {
        var sections = result.Sections.Select((sec, i) =>
        {
            var entitySec = i < entity.Sections.Count ? entity.Sections[i] : null;
            return new
            {
                SectionName = sec.OriginalTitle,
                MatchedCanonicalSection = sec.MatchedSection,
                Unrecognised = sec.Unrecognised,
                Scores = new
                {
                    OriginalQuality = sec.OriginalQualityScore,
                    BaselineQuality = sec.BaselineQualityScore,
                    RagQuality = sec.RagQualityScore,
                    BaselineFaithfulness = sec.BaselineFaithfulnessScore,
                    RagFaithfulness = sec.RagFaithfulnessScore,
                    BaselineFactualCorrectness = sec.BaselineFactualCorrectnessScore,
                    RagFactualCorrectness = sec.RagFactualCorrectnessScore,
                    BaselineResponseRelevancy = sec.BaselineResponseRelevancyScore,
                    RagResponseRelevancy = sec.RagResponseRelevancyScore,
                    ContextPrecision = sec.ContextPrecisionScore,
                    ContextRecall = sec.ContextRecallScore,
                    NoiseSensitivity = sec.NoiseSensitivityScore
                },
                RetrievedChunkCount = sec.RetrievedScores?.Count ?? 0,
                RetrievedScores = sec.RetrievedScores,
                RetrievedContexts = sec.RetrievedContexts,
                OriginalContent = sec.OriginalContent,
                BaselineContent = sec.BaselineContent,
                RagContent = sec.ImprovedContent
            };
        }).ToList();

        var evaluatedCount = result.Sections.Count(s => s.RagQualityScore.HasValue);

        return new
        {
            FileName = entity.FileName,
            SectionCount = result.Sections.Count,
            EvaluatedSectionCount = evaluatedCount,
            EvaluationSummary = entity.EvaluationSummary,
            Sections = sections
        };
    }

    public static async Task WriteAsync(string path, string json, CancellationToken ct = default)
    {
        await File.WriteAllTextAsync(path, json, ct);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test SoWImprover.Tests --filter "ExperimentExporterTests" -v n`
Expected: PASS

- [ ] **Step 5: Wire into Program.cs**

After the evaluation loop, add the export phase:

```csharp
// Phase 3: Export
log.Log("=== All documents processed ===");

var corpusDocs = definition.Retriever is not null
    ? Directory.GetFiles(Path.Combine(mainAppDir, corpusFolder), "*.pdf")
        .Select(Path.GetFileName).ToList()!
    : new List<string>();

var json = ExperimentExporter.BuildJson(
    allResults,
    corpusFolder: corpusFolder,
    corpusDocuments: corpusDocs,
    totalChunks: definition.ChunkCount,
    chatModel: config["Foundry:LocalModelName"] ?? "unknown",
    embeddingModel: config["Ollama:EmbeddingModelName"] ?? "unknown");

var exportPath = Path.Combine(Directory.GetCurrentDirectory(), "experiment-results.json");
await ExperimentExporter.WriteAsync(exportPath, json);

log.Log($"Results exported to: {exportPath}");
log.Log($"Database at: {Path.Combine(mainAppDir, "sow-improver.db")}");
log.Log("");
log.Log("To generate the analysis report, run:");
log.Log("  claude /experiment-report");

return 0;
```

- [ ] **Step 6: Build and run all tests**

Run: `dotnet build SoWImprover.BatchRunner/SoWImprover.BatchRunner.csproj && dotnet test SoWImprover.Tests -v n`
Expected: Build succeeds, all tests pass.

---

### Task 9: Create the Claude slash command

**Files:**
- Create: `.claude/commands/experiment-report.md`

- [ ] **Step 1: Create the slash command**

Create: `.claude/commands/experiment-report.md`

```markdown
Read the experiment results and write a technical analysis report comparing RAG vs baseline SoW improvement.

## Steps

1. Read `experiment-results.json` in the repo root. If not found, tell the user to run the batch runner first.
2. Read `experiment-plan-rag-vs-baseline.md` for the analysis framework and report structure.
3. Read each corpus PDF in `SoWImprover/sample-sows/` — extract via `python SoWImprover/pdf_to_markdown.py <path>` and read the output. Form your own independent judgement on each document's quality, specificity, vocabulary, and appropriateness as RAG source material. Consider: what does this document actually contain after redaction? Is the content specific enough to add value beyond what an LLM already knows? How well does it cover the 15 canonical sections?
4. Analyse the quantitative results from the JSON export following the analysis plan sections:
   - Aggregate RAG vs baseline comparison (quality, faithfulness, factual correctness, relevancy)
   - RAG-specific metrics (context precision, recall, noise sensitivity)
   - Per-section breakdown by canonical section type
   - Retrieval quality (similarity score distributions, chunk relevance)
5. Do qualitative analysis — read the actual original, baseline, and RAG content for representative sections. What changed? Was the RAG version genuinely better, or just different?
6. Write the report following this structure:
   - **Executive Summary** — 2-3 sentences: does RAG help, is the corpus sufficient?
   - **Methodology** — experiment setup, metrics, LLM configuration, corpus description
   - **Results** — aggregate comparison tables, per-section breakdown, statistical tests where sample size permits
   - **Retrieval Analysis** — what the retriever found, similarity score distributions, chunk relevance
   - **Corpus Assessment** — your independent qualitative review of each corpus document and its RAG suitability
   - **Discussion** — interpretation, limitations (evaluator bias, sample size, local LLM ceiling, single-run variance)
   - **Recommendations** — concrete next steps
7. Save to `experiment-report-YYYY-MM-DD.md` at repo root (use today's date).

## Tone

Frank, technical, internal report. No sugar-coating. Ground every claim in specific data from the results. If RAG isn't helping, say so directly and explain why.
```

- [ ] **Step 2: Verify the command is discoverable**

Run: `claude /experiment-report --help` or check that `/experiment-report` appears in Claude CLI command completion.

---

### Task 10: Update README and .gitignore

**Files:**
- Modify: `README.md` (or create if not present)
- Modify: `.gitignore`

- [ ] **Step 1: Check if README exists**

Run: `ls -la README.md` in repo root.

- [ ] **Step 2: Add experiment section to README**

Add to the README:

```markdown
## Running the RAG vs Baseline Experiment

### Prerequisites
- Local LLM running (Foundry or Ollama) with chat and embedding models
- Claude CLI installed (for report generation)
- Python with pymupdf4llm and ragas packages installed

### Step 1: Run the batch evaluation

```bash
dotnet run --project SoWImprover.BatchRunner -- "C:\path\to\test-pdfs"
```

Processes every PDF in the folder through baseline and RAG improvement,
runs Ragas evaluation, and exports results to `experiment-results.json`.
This takes a long time with local models — expect ~15-30 minutes per document.

Do not run the main app at the same time (SQLite concurrency).

### Step 2: Generate the analysis report

```bash
claude /experiment-report
```

Reads the exported results and corpus documents, then writes a technical
analysis report comparing RAG vs baseline performance. Saved to
`experiment-report-YYYY-MM-DD.md`.
```

- [ ] **Step 3: Add export and report files to .gitignore**

Append to `.gitignore`:

```
experiment-results.json
experiment-report-*.md
```

- [ ] **Step 4: Build full solution and run all tests**

Run: `dotnet build SoWImprover.slnx && dotnet test SoWImprover.Tests -v n`
Expected: Build succeeds, all tests pass.

---

### Task 11: Error resilience test

Verify that a failing document doesn't abort the batch.

**Files:**
- Modify: `SoWImprover.Tests/BatchRunner/BatchPipelineTests.cs`

- [ ] **Step 1: Write the test**

Add to `BatchPipelineTests.cs`:

```csharp
[Fact]
public async Task ProcessDocumentAsync_WhenExtractionFails_ThrowsForCaller()
{
    // Arrange
    var loader = Substitute.For<DocumentLoader>();
    loader.ExtractTextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
        .ThrowsAsync(new InvalidOperationException("PDF corrupt"));

    var services = new ServiceCollection();
    var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
    connection.Open();
    services.AddDbContextFactory<SoWDbContext>(opts => opts.UseSqlite(connection));
    var sp = services.BuildServiceProvider();
    var dbFactory = sp.GetRequiredService<IDbContextFactory<SoWDbContext>>();

    var log = new ConsoleLogger(TextWriter.Null);
    var pipeline = new BatchPipeline(
        loader, Substitute.For<SoWImproverService>(), dbFactory, log);

    // Act & Assert — pipeline throws, caller (Program.cs) catches and continues
    await Assert.ThrowsAsync<InvalidOperationException>(
        () => pipeline.ProcessDocumentAsync("bad.pdf", new GoodDefinition(), CancellationToken.None));
}
```

- [ ] **Step 2: Run test**

Run: `dotnet test SoWImprover.Tests --filter "ProcessDocumentAsync_WhenExtractionFails" -v n`
Expected: PASS — the pipeline propagates the exception, Program.cs catches and continues to the next document.

---
