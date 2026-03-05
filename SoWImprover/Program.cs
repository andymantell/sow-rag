using SoWImprover.Components;
using SoWImprover.Models;
using SoWImprover.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Scoped (per Blazor circuit / user session) ───────────────────────────────
builder.Services.AddScoped<ResultState>();

// ── Singletons ──────────────────────────────────────────────────────────────
builder.Services.AddSingleton<GoodDefinition>();
builder.Services.AddSingleton<DocumentLoader>();
builder.Services.AddSingleton<FoundryClientFactory>();
builder.Services.AddSingleton<DefinitionBuilder>();
builder.Services.AddSingleton<EmbeddingService>();
builder.Services.AddSingleton<DiffService>();
builder.Services.AddSingleton<SoWImproverService>();

// BackgroundService: generates the definition of good at startup
builder.Services.AddHostedService<DefinitionGeneratorService>();

// ── Blazor ───────────────────────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ── Build ────────────────────────────────────────────────────────────────────
var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
