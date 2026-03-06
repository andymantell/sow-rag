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

        // 6 words, chunk size 3, overlap 1 → advance by 2 → chunks at [0,1,2], [2,3,4], [4,5]
        Assert.Equal(3, chunks.Count);
        Assert.Equal(0, chunks[0].ChunkIndex);
        Assert.Equal(1, chunks[1].ChunkIndex);
        Assert.Equal(2, chunks[2].ChunkIndex);

        Assert.Equal("one two three", chunks[0].Text);
        Assert.Equal("three four five", chunks[1].Text);
        Assert.Equal("five six", chunks[2].Text);
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

    [Fact]
    public void ChunkText_ChunkOverlapEqualToChunkSize_Throws()
    {
        var loader = CreateLoader(chunkSize: 10, chunkOverlap: 10);
        Assert.Throws<InvalidOperationException>(() => loader.ChunkText("word1 word2", "test.pdf"));
    }

    [Fact]
    public void ChunkText_ChunkOverlapGreaterThanChunkSize_Throws()
    {
        var loader = CreateLoader(chunkSize: 5, chunkOverlap: 10);
        Assert.Throws<InvalidOperationException>(() => loader.ChunkText("word1 word2", "test.pdf"));
    }

    [Fact]
    public void ChunkText_ChunkOverlapZero_ProducesNonOverlappingChunks()
    {
        var loader = CreateLoader(chunkSize: 2, chunkOverlap: 0);
        var chunks = loader.ChunkText("one two three four", "test.pdf");

        Assert.Equal(2, chunks.Count);
        Assert.Equal("one two", chunks[0].Text);
        Assert.Equal("three four", chunks[1].Text);
    }
}
