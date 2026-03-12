using NSubstitute;
using SoWImprover.Models;
using SoWImprover.Services;

namespace SoWImprover.Tests.Unit;

public class EmbeddingRetrieverTests
{
    [Fact]
    public void CosineSimilarity_IdenticalVectors_ReturnsOne()
    {
        var v = new float[] { 1, 2, 3 };
        var result = EmbeddingRetriever.CosineSimilarity(v, v);
        Assert.Equal(1.0f, result, precision: 5);
    }

    [Fact]
    public void CosineSimilarity_OrthogonalVectors_ReturnsZero()
    {
        var a = new float[] { 1, 0, 0 };
        var b = new float[] { 0, 1, 0 };
        Assert.Equal(0.0f, EmbeddingRetriever.CosineSimilarity(a, b), precision: 5);
    }

    [Fact]
    public void CosineSimilarity_OppositeVectors_ReturnsNegativeOne()
    {
        var a = new float[] { 1, 0, 0 };
        var b = new float[] { -1, 0, 0 };
        Assert.Equal(-1.0f, EmbeddingRetriever.CosineSimilarity(a, b), precision: 5);
    }

    [Fact]
    public void CosineSimilarity_ZeroVector_ReturnsZero()
    {
        var a = new float[] { 0, 0, 0 };
        var b = new float[] { 1, 2, 3 };
        Assert.Equal(0.0f, EmbeddingRetriever.CosineSimilarity(a, b));
    }

    [Fact]
    public void CosineSimilarity_DimensionMismatch_Throws()
    {
        var a = new float[] { 1, 2 };
        var b = new float[] { 1, 2, 3 };
        Assert.Throws<ArgumentException>(() => EmbeddingRetriever.CosineSimilarity(a, b));
    }

    [Fact]
    public void Constructor_MismatchedChunksAndVectors_Throws()
    {
        var chunks = new List<DocumentChunk>
        {
            new() { SourceFile = "a.pdf", Text = "chunk1" }
        };
        var vectors = new float[][] { [1, 0], [0, 1] }; // 2 vectors, 1 chunk

        var embedding = Substitute.For<IEmbeddingService>();

        Assert.Throws<ArgumentException>(() =>
            new EmbeddingRetriever(chunks, vectors, embedding, 3));
    }

    [Fact]
    public async Task RetrieveAsync_ReturnsTopKByCosineSimilarity()
    {
        var chunks = new List<DocumentChunk>
        {
            new() { SourceFile = "a.pdf", Text = "chunk0", ChunkIndex = 0 },
            new() { SourceFile = "a.pdf", Text = "chunk1", ChunkIndex = 1 },
            new() { SourceFile = "a.pdf", Text = "chunk2", ChunkIndex = 2 },
        };
        // Vectors: chunk2 is most similar to query vector [1,0,0]
        var vectors = new float[][]
        {
            [0, 1, 0], // chunk0 — orthogonal
            [0, 0, 1], // chunk1 — orthogonal
            [1, 0, 0], // chunk2 — identical
        };

        var embedding = Substitute.For<IEmbeddingService>();
        embedding.EmbedAsync("query", Arg.Any<CancellationToken>())
            .Returns(new float[] { 1, 0, 0 });

        var retriever = new EmbeddingRetriever(chunks, vectors, embedding, topK: 2);
        var results = await retriever.RetrieveAsync("query");

        Assert.Equal(2, results.Count);
        Assert.Equal("chunk2", results[0].Chunk.Text); // most similar first
    }

    [Fact]
    public async Task RetrieveAsync_EmptyCorpus_ReturnsEmptyList()
    {
        var embedding = Substitute.For<IEmbeddingService>();
        embedding.EmbedAsync("query", Arg.Any<CancellationToken>())
            .Returns(new float[] { 1, 0, 0 });

        var retriever = new EmbeddingRetriever([], [], embedding, topK: 3);
        var results = await retriever.RetrieveAsync("query");

        Assert.Empty(results);
    }

    [Fact]
    public async Task RetrieveAsync_TopKLargerThanCorpus_ReturnsAllChunks()
    {
        var chunks = new List<DocumentChunk>
        {
            new() { SourceFile = "a.pdf", Text = "chunk0", ChunkIndex = 0 },
            new() { SourceFile = "a.pdf", Text = "chunk1", ChunkIndex = 1 },
        };
        var vectors = new float[][]
        {
            [1, 0, 0],
            [0, 1, 0],
        };

        var embedding = Substitute.For<IEmbeddingService>();
        embedding.EmbedAsync("query", Arg.Any<CancellationToken>())
            .Returns(new float[] { 1, 0, 0 });

        var retriever = new EmbeddingRetriever(chunks, vectors, embedding, topK: 10);
        var results = await retriever.RetrieveAsync("query");

        Assert.Equal(2, results.Count);
        Assert.Equal("chunk0", results[0].Chunk.Text); // most similar first
    }

    [Fact]
    public async Task RetrieveAsync_ResultsOrderedByDescendingSimilarity()
    {
        var chunks = new List<DocumentChunk>
        {
            new() { SourceFile = "a.pdf", Text = "low", ChunkIndex = 0 },
            new() { SourceFile = "a.pdf", Text = "mid", ChunkIndex = 1 },
            new() { SourceFile = "a.pdf", Text = "high", ChunkIndex = 2 },
        };
        var vectors = new float[][]
        {
            [0.1f, 0.9f, 0],   // low similarity to [1,0,0]
            [0.7f, 0.3f, 0],   // medium similarity
            [0.95f, 0.05f, 0], // high similarity
        };

        var embedding = Substitute.For<IEmbeddingService>();
        embedding.EmbedAsync("query", Arg.Any<CancellationToken>())
            .Returns(new float[] { 1, 0, 0 });

        var retriever = new EmbeddingRetriever(chunks, vectors, embedding, topK: 3);
        var results = await retriever.RetrieveAsync("query");

        Assert.Equal(3, results.Count);
        Assert.Equal("high", results[0].Chunk.Text);
        Assert.Equal("mid", results[1].Chunk.Text);
        Assert.Equal("low", results[2].Chunk.Text);
    }

    [Fact]
    public async Task RetrieveAsync_MinScore_FiltersLowRelevanceChunks()
    {
        var chunks = new List<DocumentChunk>
        {
            new() { SourceFile = "a.pdf", Text = "irrelevant", ChunkIndex = 0 },
            new() { SourceFile = "a.pdf", Text = "relevant", ChunkIndex = 1 },
        };
        var vectors = new float[][]
        {
            [0, 1, 0],   // orthogonal to query — score ≈ 0
            [1, 0, 0],   // identical to query — score = 1
        };

        var embedding = Substitute.For<IEmbeddingService>();
        embedding.EmbedAsync("query", Arg.Any<CancellationToken>())
            .Returns(new float[] { 1, 0, 0 });

        var retriever = new EmbeddingRetriever(chunks, vectors, embedding, topK: 10, minScore: 0.5f);
        var results = await retriever.RetrieveAsync("query");

        Assert.Single(results);
        Assert.Equal("relevant", results[0].Chunk.Text);
        Assert.True(results[0].Score >= 0.5f);
    }

    [Fact]
    public async Task RetrieveAsync_AllBelowThreshold_ReturnsEmpty()
    {
        var chunks = new List<DocumentChunk>
        {
            new() { SourceFile = "a.pdf", Text = "irrelevant1", ChunkIndex = 0 },
            new() { SourceFile = "a.pdf", Text = "irrelevant2", ChunkIndex = 1 },
        };
        var vectors = new float[][]
        {
            [0, 1, 0],
            [0, 0, 1],
        };

        var embedding = Substitute.For<IEmbeddingService>();
        embedding.EmbedAsync("query", Arg.Any<CancellationToken>())
            .Returns(new float[] { 1, 0, 0 });

        var retriever = new EmbeddingRetriever(chunks, vectors, embedding, topK: 10, minScore: 0.5f);
        var results = await retriever.RetrieveAsync("query");

        Assert.Empty(results);
    }

    [Fact]
    public async Task RetrieveAsync_ReturnsScores()
    {
        var chunks = new List<DocumentChunk>
        {
            new() { SourceFile = "a.pdf", Text = "chunk", ChunkIndex = 0 },
        };
        var vectors = new float[][] { [1, 0, 0] };

        var embedding = Substitute.For<IEmbeddingService>();
        embedding.EmbedAsync("query", Arg.Any<CancellationToken>())
            .Returns(new float[] { 1, 0, 0 });

        var retriever = new EmbeddingRetriever(chunks, vectors, embedding, topK: 1);
        var results = await retriever.RetrieveAsync("query");

        Assert.Single(results);
        Assert.Equal(1.0f, results[0].Score, precision: 5);
    }

    [Fact]
    public void ChunkCount_ReturnsCorrectCount()
    {
        var chunks = new List<DocumentChunk>
        {
            new() { SourceFile = "a.pdf", Text = "c1" },
            new() { SourceFile = "a.pdf", Text = "c2" },
            new() { SourceFile = "b.pdf", Text = "c3" },
        };
        var vectors = new float[][] { [1], [1], [1] };
        var embedding = Substitute.For<IEmbeddingService>();

        var retriever = new EmbeddingRetriever(chunks, vectors, embedding, 3);
        Assert.Equal(3, retriever.ChunkCount);
    }

    [Fact]
    public void DocumentCount_ReturnsDistinctSourceFiles()
    {
        var chunks = new List<DocumentChunk>
        {
            new() { SourceFile = "a.pdf", Text = "c1" },
            new() { SourceFile = "a.pdf", Text = "c2" },
            new() { SourceFile = "b.pdf", Text = "c3" },
        };
        var vectors = new float[][] { [1], [1], [1] };
        var embedding = Substitute.For<IEmbeddingService>();

        var retriever = new EmbeddingRetriever(chunks, vectors, embedding, 3);
        Assert.Equal(2, retriever.DocumentCount);
    }
}
