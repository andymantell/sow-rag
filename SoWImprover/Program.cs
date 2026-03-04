using SoWImprover.Components;
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

// ── Blazor ───────────────────────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ── Build ────────────────────────────────────────────────────────────────────
var app = builder.Build();

// Eagerly resolve SimpleRetriever so chunk-loading errors surface at startup,
// not on the first request.
_ = app.Services.GetRequiredService<SimpleRetriever>();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
