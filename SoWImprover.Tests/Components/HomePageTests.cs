using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SoWImprover.Components.Pages;
using SoWImprover.Data;
using SoWImprover.Models;
using SoWImprover.Services;

namespace SoWImprover.Tests.Components;

public class HomePageTests : BunitContext
{
    private Microsoft.Data.Sqlite.SqliteConnection? _connection;

    private void RegisterServices()
    {
        Services.AddSingleton(new GoodDefinition());

        var configPairs = new Dictionary<string, string?>
        {
            ["Docs:ChunkSize"] = "500",
            ["Docs:ChunkOverlap"] = "50"
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configPairs)
            .Build();
        Services.AddSingleton<IConfiguration>(config);
        Services.AddSingleton(new DocumentLoader(config, NullLogger<DocumentLoader>.Instance));

        var chat = Substitute.For<IChatService>();
        Services.AddSingleton(chat);
        Services.AddSingleton(new SoWImproverService(chat, NullLogger<SoWImproverService>.Instance));

        _connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        _connection.Open();
        Services.AddDbContextFactory<SoWDbContext>(opts => opts.UseSqlite(_connection));

        using var db = new SoWDbContext(
            new DbContextOptionsBuilder<SoWDbContext>().UseSqlite(_connection).Options);
        db.Database.EnsureCreated();

        Services.AddLogging();
    }

    [Fact]
    public void Home_RendersUploadPanel()
    {
        RegisterServices();
        var cut = Render<Home>();

        Assert.Contains("Upload SoW PDF", cut.Markup);
        Assert.Contains("Improve document", cut.Markup);
    }

    [Fact]
    public void Home_NoDocuments_HidesPreviousDocumentsTable()
    {
        RegisterServices();
        var cut = Render<Home>();

        Assert.DoesNotContain("Previous documents", cut.Markup);
    }

    [Fact]
    public async Task Home_WithDocuments_ShowsPreviousDocumentsTable()
    {
        RegisterServices();

        var factory = Services.GetRequiredService<IDbContextFactory<SoWDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        db.Documents.Add(new DocumentEntity
        {
            Id = Guid.NewGuid(),
            FileName = "test-sow.pdf",
            OriginalText = "Some text",
            UploadedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var cut = Render<Home>();

        Assert.Contains("Previous documents", cut.Markup);
        Assert.Contains("test-sow.pdf", cut.Markup);
        Assert.Contains("View results", cut.Markup);
    }

    protected override void Dispose(bool disposing)
    {
        _connection?.Dispose();
        base.Dispose(disposing);
    }
}
