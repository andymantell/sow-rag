using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SoWImprover.Components.Shared;
using SoWImprover.Data;
using SoWImprover.Models;
using SoWImprover.Services;

namespace SoWImprover.Tests.Components;

public class UploadPanelTests : TestContext
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
    public void Submit_NoFileSelected_ShowsError()
    {
        RegisterServices();
        var cut = Render<UploadPanel>();

        cut.Find("form").Submit();

        Assert.Contains("Select a PDF to upload", cut.Markup);
    }

    [Fact]
    public void Submit_DefinitionNotReady_ShowsError()
    {
        RegisterServices();
        var cut = Render<UploadPanel>();

        cut.Find("form").Submit();

        Assert.Contains("still loading", cut.Markup);
    }

    [Fact]
    public void Renders_UploadInput()
    {
        RegisterServices();
        var cut = Render<UploadPanel>();

        Assert.Contains("Upload SoW PDF", cut.Markup);
        Assert.NotNull(cut.Find("#file-input"));
        Assert.NotNull(cut.Find("button[type='submit']"));
    }

    protected override void Dispose(bool disposing)
    {
        _connection?.Dispose();
        base.Dispose(disposing);
    }
}
