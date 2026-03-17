using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SoWImprover.Models;
using SoWImprover.Services;

namespace SoWImprover.Tests.Services;

public class CorpusInitialisationServiceTests
{
    [Fact]
    public async Task InitialiseAsync_SetsGoodDefinitionReady()
    {
        // Use a real temp directory so ComputeCorpusFingerprint can enumerate it.
        // Drop a tiny PDF-magic-byte file so the fingerprint is stable.
        var tempDir = Path.Combine(Path.GetTempPath(), $"corpus-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            await File.WriteAllBytesAsync(
                Path.Combine(tempDir, "test.pdf"),
                "%PDF-1.4 test"u8.ToArray());

            // Arrange
            var loaderConfig = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Docs:ChunkSize"] = "500",
                    ["Docs:ChunkOverlap"] = "50"
                })
                .Build();

            var loader = Substitute.For<DocumentLoader>(
                loaderConfig,
                Substitute.For<ILogger<DocumentLoader>>());

            var chunks = new List<DocumentChunk>
            {
                new() { SourceFile = "test.pdf", ChunkIndex = 0, Text = "hello world" }
            };
            loader.LoadFolder(Arg.Any<string>()).Returns(chunks);
            loader.GetCachedTexts().Returns(new List<(string, string)> { ("test.pdf", "hello world") });

            var embeddingService = Substitute.For<IEmbeddingService>();
            embeddingService.EmbedBatchAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
                .Returns(new[] { new float[] { 0.1f, 0.2f } });

            var chatService = Substitute.For<IChatService>();
            chatService.CompleteAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns("Redacted text");

            var definitionBuilder = new DefinitionBuilder(chatService, NullLogger<DefinitionBuilder>.Instance);

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Foundry:UseLocal"] = "true",
                    ["Foundry:LocalModelName"] = "phi-4",
                    ["Ollama:EmbeddingModelName"] = "nomic-embed-text",
                    ["Docs:KnownGoodFolder"] = tempDir,
                    ["Docs:TopKChunks"] = "5",
                    ["Docs:MinChunkScore"] = "0.3"
                })
                .Build();

            var sut = new CorpusInitialisationService(
                loader,
                embeddingService,
                chatService,
                definitionBuilder,
                config,
                NullLogger<CorpusInitialisationService>.Instance);

            var definition = new GoodDefinition();

            // Act
            await sut.InitialiseAsync(definition, _ => { }, CancellationToken.None);

            // Assert
            Assert.True(definition.IsReady);
            Assert.Equal(1, definition.DocumentCount);
            Assert.Equal(1, definition.ChunkCount);
            Assert.NotNull(definition.Retriever);
            Assert.NotEmpty(definition.Sections);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
