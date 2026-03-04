using SoWImprover.Models;
using SoWImprover.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Singletons ──────────────────────────────────────────────────────────────
builder.Services.AddSingleton<GoodDefinition>();
builder.Services.AddSingleton<DocumentLoader>();
builder.Services.AddSingleton<FoundryClientFactory>();
builder.Services.AddSingleton<DefinitionBuilder>();
builder.Services.AddSingleton<DiffService>();

// SimpleRetriever is built once from the loaded chunks.
// The factory throws immediately if KnownGoodFolder has no PDFs.
builder.Services.AddSingleton(sp =>
{
    var loader = sp.GetRequiredService<DocumentLoader>();
    var cfg = sp.GetRequiredService<IConfiguration>();
    var folder = cfg["Docs:KnownGoodFolder"] ?? "./sample-sows";
    var chunks = loader.LoadFolder(folder);   // throws if empty
    return new SimpleRetriever(chunks, cfg);
});

builder.Services.AddSingleton<SoWImproverService>();

// BackgroundService: generates the definition of good at startup
builder.Services.AddHostedService<DefinitionGeneratorService>();

// ── Build ────────────────────────────────────────────────────────────────────
var app = builder.Build();

// Eagerly resolve SimpleRetriever so chunk-loading errors surface at startup,
// not on the first request.
_ = app.Services.GetRequiredService<SimpleRetriever>();

app.UseDefaultFiles();
app.UseStaticFiles();

// ── API Endpoints ────────────────────────────────────────────────────────────

app.MapGet("/api/status", (GoodDefinition def, IConfiguration cfg, SimpleRetriever retriever) =>
    Results.Ok(new
    {
        modelName = cfg.GetValue<bool>("Foundry:UseLocal")
            ? cfg["Foundry:LocalModelName"]
            : cfg["Foundry:CloudModelName"],
        isLocal = cfg.GetValue<bool>("Foundry:UseLocal"),
        isReady = def.IsReady,
        documentCount = def.DocumentCount,
        chunkCount = def.ChunkCount
    }));

app.MapGet("/api/definition", (GoodDefinition def) =>
{
    if (!def.IsReady)
        return Results.Problem(
            "Definition is still being generated — please try again shortly.",
            statusCode: 503);

    return Results.Text(def.MarkdownContent, "text/plain; charset=utf-8");
});

app.MapPost("/api/improve", async (
    HttpRequest request,
    GoodDefinition def,
    DocumentLoader loader,
    SoWImproverService improver,
    DiffService diffService) =>
{
    if (!def.IsReady)
        return Results.Problem(
            "Definition is still being generated — please try again shortly.",
            statusCode: 503);

    if (!request.HasFormContentType)
        return Results.BadRequest("Request must be multipart/form-data.");

    var form = await request.ReadFormAsync();
    var file = form.Files.FirstOrDefault();
    if (file is null)
        return Results.BadRequest("No file was uploaded.");

    // Save to a temp file so pymupdf4llm can read it by path
    var tempPath = Path.Combine(Path.GetTempPath(), $"sow_{Guid.NewGuid():N}.pdf");
    string originalText;
    try
    {
        using (var fs = File.Create(tempPath))
            await file.OpenReadStream().CopyToAsync(fs);

        originalText = (await loader.ExtractTextAsync(tempPath)).Trim();
    }
    catch (Exception ex)
    {
        return Results.BadRequest($"Failed to extract text from PDF: {ex.Message}");
    }
    finally
    {
        if (File.Exists(tempPath)) File.Delete(tempPath);
    }

    if (string.IsNullOrWhiteSpace(originalText))
        return Results.BadRequest(
            "No text could be extracted — PDF may be scanned or image-based.");

    try
    {
        var result = await improver.ImproveAsync(originalText, def);
        var (normOriginal, normImproved) = diffService.Prepare(result.Original, result.Improved);

        return Results.Ok(new ImprovementResult
        {
            Original = normOriginal,
            Improved = normImproved,
            Annotations = result.Annotations,
            ChunksUsed = result.ChunksUsed
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to improve document: {ex.Message}", statusCode: 500);
    }
});

app.Run();
