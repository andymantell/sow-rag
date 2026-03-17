using Microsoft.EntityFrameworkCore;
using Microsoft.FeatureManagement;
using QuestPDF.Infrastructure;
using SoWImprover.Components;
using SoWImprover.Data;
using SoWImprover.Models;
using SoWImprover.Services;

var builder = WebApplication.CreateBuilder(args);

// Crash the host if the startup background service fails (definition generation)
builder.Services.Configure<HostOptions>(o =>
    o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost);

// ── Singletons ──────────────────────────────────────────────────────────────
builder.Services.AddSingleton<GoodDefinition>();
builder.Services.AddSingleton<DocumentLoader>();
builder.Services.AddSingleton<FoundryClientFactory>();
builder.Services.AddSingleton<IChatService, ChatService>();
builder.Services.AddSingleton<IEmbeddingService, EmbeddingService>();
builder.Services.AddSingleton<DefinitionBuilder>();
builder.Services.AddSingleton<SoWImproverService>();
builder.Services.AddSingleton<EvaluationService>();
builder.Services.AddSingleton<IEvaluationSummaryService, EvaluationSummaryService>();
builder.Services.AddSingleton<GpuMemoryManager>();

// BackgroundService: generates the definition of good at startup
builder.Services.AddHostedService<DefinitionGeneratorService>();

// ── Database ────────────────────────────────────────────────────────────────
builder.Services.AddDbContextFactory<SoWDbContext>(opts =>
    opts.UseSqlite("Data Source=sow-improver.db"));

// ── Feature flags ────────────────────────────────────────────────────────────
builder.Services.AddFeatureManagement();

// ── Blazor ───────────────────────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ── QuestPDF ──────────────────────────────────────────────────────────────────
QuestPDF.Settings.License = LicenseType.Community;

// ── Build ────────────────────────────────────────────────────────────────────
var app = builder.Build();

// Auto-migrate database (PoC only — use proper migrations in production)
using (var db = app.Services.GetRequiredService<IDbContextFactory<SoWDbContext>>().CreateDbContext())
{
    db.Database.EnsureCreated();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

/// <summary>Enables WebApplicationFactory to discover the entry point assembly.</summary>
public partial class Program { }
