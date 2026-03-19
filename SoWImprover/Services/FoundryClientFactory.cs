using Azure;
using Azure.AI.OpenAI;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;
using System.ClientModel;

namespace SoWImprover.Services;

/// <summary>
/// Creates <see cref="ChatClient"/> and <see cref="EmbeddingClient"/> instances.
/// In local mode (<c>Foundry:UseLocal = true</c>), connects to Ollama's OpenAI-compatible API.
/// In cloud mode, connects to Azure AI Foundry.
/// </summary>
public class FoundryClientFactory(
    IConfiguration config,
    ILogger<FoundryClientFactory> logger) : IDisposable
{
    private ChatClient? _cached;
    private EmbeddingClient? _cachedEmbedding;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly SemaphoreSlim _embeddingLock = new(1, 1);

    /// <summary>
    /// Returns a <see cref="ChatClient"/> configured for either Ollama (local) or Azure AI (cloud),
    /// based on the <c>Foundry:UseLocal</c> configuration value. The client is created once and cached.
    /// </summary>
    public async Task<ChatClient> GetChatClientAsync(CancellationToken ct = default)
    {
        if (_cached is not null) return _cached;

        await _lock.WaitAsync(ct);
        try
        {
            if (_cached is not null) return _cached;
            _cached = config.GetValue<bool>("Foundry:UseLocal")
                ? CreateOllamaChatClient()
                : CreateCloudClient();
            return _cached;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Returns an <see cref="EmbeddingClient"/> for embeddings.
    /// When <c>Foundry:UseLocal</c> is true, connects to Ollama (<c>Ollama:Endpoint</c>).
    /// When false, connects to Azure AI Foundry cloud (<c>Foundry:Cloud*</c> settings).
    /// </summary>
    public async Task<EmbeddingClient> GetEmbeddingClientAsync(CancellationToken ct = default)
    {
        if (_cachedEmbedding is not null) return _cachedEmbedding;

        await _embeddingLock.WaitAsync(ct);
        try
        {
            if (_cachedEmbedding is not null) return _cachedEmbedding;

            _cachedEmbedding = config.GetValue<bool>("Foundry:UseLocal")
                ? CreateOllamaEmbeddingClient()
                : CreateAzureEmbeddingClient();

            return _cachedEmbedding;
        }
        finally
        {
            _embeddingLock.Release();
        }
    }

    private ChatClient CreateOllamaChatClient()
    {
        var endpoint = config["Ollama:Endpoint"] ?? "http://localhost:11434/v1";
        var modelName = config["Ollama:ChatModelName"]
            ?? throw new InvalidOperationException("Ollama:ChatModelName is not configured.");

        logger.LogInformation("Chat client (Ollama) — endpoint: {Url}, model: {Model}",
            endpoint, modelName);

        var openAiClient = new OpenAIClient(
            new ApiKeyCredential("ollama"),
            new OpenAIClientOptions { Endpoint = new Uri(endpoint) });

        return openAiClient.GetChatClient(modelName);
    }

    private EmbeddingClient CreateOllamaEmbeddingClient()
    {
        var endpoint = config["Ollama:Endpoint"] ?? "http://localhost:11434/v1";
        var modelName = config["Ollama:EmbeddingModelName"] ?? "nomic-embed-text";

        logger.LogInformation("Embedding client (Ollama) — endpoint: {Url}, model: {Model}",
            endpoint, modelName);

        var openAiClient = new OpenAIClient(
            new ApiKeyCredential("ollama"),
            new OpenAIClientOptions { Endpoint = new Uri(endpoint) });

        return openAiClient.GetEmbeddingClient(modelName);
    }

    private EmbeddingClient CreateAzureEmbeddingClient()
    {
        var endpoint = config["Foundry:CloudEndpoint"]
            ?? throw new InvalidOperationException("Foundry:CloudEndpoint is not configured.");
        var apiKey = config["Foundry:CloudApiKey"]
            ?? throw new InvalidOperationException("Foundry:CloudApiKey is not configured.");
        var deployment = config["Foundry:CloudEmbeddingDeployment"]
            ?? throw new InvalidOperationException("Foundry:CloudEmbeddingDeployment is not configured.");

        logger.LogInformation("Embedding client (Azure) — endpoint: {Endpoint}, deployment: {Deployment}",
            endpoint, deployment);

        var azureClient = new AzureOpenAIClient(
            new Uri(endpoint),
            new AzureKeyCredential(apiKey));

        return azureClient.GetEmbeddingClient(deployment);
    }

    private ChatClient CreateCloudClient()
    {
        var endpoint = config["Foundry:CloudEndpoint"]
            ?? throw new InvalidOperationException("Foundry:CloudEndpoint is not configured.");
        var apiKey = config["Foundry:CloudApiKey"]
            ?? throw new InvalidOperationException("Foundry:CloudApiKey is not configured.");
        var modelName = config["Foundry:CloudModelName"]
            ?? throw new InvalidOperationException("Foundry:CloudModelName is not configured.");

        logger.LogInformation("Using Azure AI Foundry cloud — endpoint: {Endpoint}, model: {Model}",
            endpoint, modelName);

        var azureClient = new AzureOpenAIClient(
            new Uri(endpoint),
            new AzureKeyCredential(apiKey));

        return azureClient.GetChatClient(modelName);
    }

    public void Dispose()
    {
        _lock.Dispose();
        _embeddingLock.Dispose();
    }
}
