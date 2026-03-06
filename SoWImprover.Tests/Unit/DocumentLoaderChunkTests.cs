using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SoWImprover.Services;

namespace SoWImprover.Tests.Unit;

public class DocumentLoaderChunkTests
{
    private static DocumentLoader CreateLoader(int chunkSize = 10, int chunkOverlap = 2)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Docs:ChunkSize"] = chunkSize.ToString(),
                ["Docs:ChunkOverlap"] = chunkOverlap.ToString()
            })
            .Build();

        return new DocumentLoader(config, NullLogger<DocumentLoader>.Instance);
    }

    [Fact]
    public void ChunkText_ShortText_ReturnsSingleChunk()
    {
        var loader = CreateLoader(chunkSize: 100);
        var chunks = loader.ChunkText("word1 word2 word3", "test.pdf");

        Assert.Single(chunks);
        Assert.Equal("test.pdf", chunks[0].SourceFile);
        Assert.Equal("word1 word2 word3", chunks[0].Text);
        Assert.Equal(0, chunks[0].ChunkIndex);
    }

    [Fact]
    public void ChunkText_LongText_CreatesOverlappingChunks()
    {
        var loader = CreateLoader(chunkSize: 3, chunkOverlap: 1);
        // 6 words, chunk size 3, overlap 1 → advance by 2 each time
        var text = "one two three four five six";
        var chunks = loader.ChunkText(text, "test.pdf");

        Assert.True(chunks.Count >= 2);
        Assert.Equal(0, chunks[0].ChunkIndex);
        Assert.Equal(1, chunks[1].ChunkIndex);

        // First chunk should contain the first 3 words
        Assert.Equal("one two three", chunks[0].Text);
        // Second chunk starts at word index 2 (overlap = 1)
        Assert.Equal("three four five", chunks[1].Text);
    }

    [Fact]
    public void ChunkText_EmptyText_ReturnsEmptyList()
    {
        var loader = CreateLoader();
        var chunks = loader.ChunkText("", "test.pdf");
        Assert.Empty(chunks);
    }

    [Fact]
    public void ChunkText_SplitsOnAllWhitespace()
    {
        var loader = CreateLoader(chunkSize: 100);
        var text = "word1\tword2\nword3\rword4 word5";
        var chunks = loader.ChunkText(text, "test.pdf");

        Assert.Single(chunks);
        Assert.Equal("word1 word2 word3 word4 word5", chunks[0].Text);
    }
}
