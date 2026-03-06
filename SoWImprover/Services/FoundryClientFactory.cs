using Azure;
using Azure.AI.OpenAI;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;
using System.ClientModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SoWImprover.Services;

/// <summary>
/// Resolves the local Foundry Local CLI service endpoint or an Azure AI Foundry cloud endpoint
/// and returns a configured <see cref="ChatClient"/>.
/// </summary>
public class FoundryClientFactory(IConfiguration config, ILogger<FoundryClientFactory> logger) : IDisposable
{
    // Long-lived HttpClient instance — avoids socket exhaustion from per-call instantiation.
    private static readonly HttpClient _http = new();

    private ChatClient? _cached;
    private EmbeddingClient? _cachedEmbedding;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _cachedServiceRoot;
    private readonly SemaphoreSlim _rootLock = new(1, 1);
    private readonly SemaphoreSlim _embeddingLock = new(1, 1);

    /// <summary>
    /// Returns a <see cref="ChatClient"/> configured for either local Foundry or Azure AI cloud,
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
                ? await CreateLocalClientAsync(ct)
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

    private async Task<string> GetServiceRootAsync(CancellationToken ct)
    {
        if (_cachedServiceRoot is not null) return _cachedServiceRoot;

        await _rootLock.WaitAsync(ct);
        try
        {
            _cachedServiceRoot ??= await DiscoverFoundryEndpointAsync(ct);
            return _cachedServiceRoot;
        }
        finally
        {
            _rootLock.Release();
        }
    }

    private async Task<ChatClient> CreateLocalClientAsync(CancellationToken ct)
    {
        var alias = config["Foundry:LocalModelName"]
            ?? throw new InvalidOperationException("Foundry:LocalModelName is not configured.");

        logger.LogInformation("Connecting to Foundry Local CLI service, model: {Alias}", alias);

        // serviceRoot = http://host:PORT  (used to query /v1/models)
        // sdkEndpoint = http://host:PORT/v1  (OpenAI SDK appends /chat/completions from here)
        var serviceRoot = await GetServiceRootAsync(ct);
        var sdkEndpoint = serviceRoot + "/v1";

        // Resolve the config alias (e.g. "phi-4") to the actual model ID the service expects
        // (e.g. "Phi-4-trtrtx-gpu:1") by querying /v1/models.
        var modelId = await ResolveModelIdAsync(serviceRoot, alias, ct);

        logger.LogInformation("Foundry Local ready — endpoint: {Url}, modelId: {ModelId}",
            sdkEndpoint, modelId);

        var openAiClient = new OpenAIClient(
            new ApiKeyCredential("OPENAI_API_KEY"),
            new OpenAIClientOptions { Endpoint = new Uri(sdkEndpoint) });

        return openAiClient.GetChatClient(modelId);
    }

    /// <summary>
    /// Returns the base URL of the running Foundry Local CLI service, starting it if needed.
    /// </summary>
    private async Task<string> DiscoverFoundryEndpointAsync(CancellationToken ct)
    {
        var output = await RunFoundryCliAsync("service status", ct);
        var url = ParseServiceBaseUrl(output);
        if (url is not null) return url;

        logger.LogInformation("Foundry Local service not detected, attempting to start...");
        await RunFoundryCliAsync("service start", ct);
        await Task.Delay(4000, ct);

        output = await RunFoundryCliAsync("service status", ct);
        url = ParseServiceBaseUrl(output);

        return url ?? throw new InvalidOperationException(
            "Foundry Local service could not be started. " +
            "Install it with: winget install Microsoft.FoundryLocal\n" +
            $"foundry output: {output.Trim()}");
    }

    // Extracts http://host:PORT from "🟢 Model management service is running on http://host:PORT/..."
    private static string? ParseServiceBaseUrl(string output)
    {
        var match = Regex.Match(output, @"(http://[\d.]+:\d+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Queries /v1/models and returns the first model ID whose name starts with
    /// <paramref name="alias"/> (case-insensitive). Prefers GPU variants.
    /// </summary>
    private async Task<string> ResolveModelIdAsync(string baseUrl, string alias, CancellationToken ct)
    {
        string json;
        try
        {
            json = await _http.GetStringAsync($"{baseUrl}/v1/models", ct);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Could not reach Foundry Local service at {baseUrl}/v1/models: {ex.Message}", ex);
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Foundry Local service returned an unexpected response from {baseUrl}/v1/models. " +
                $"Response (first 200 chars): {json[..Math.Min(200, json.Length)]}", ex);
        }

        using (doc)
        {
            var ids = doc.RootElement.GetProperty("data")
                .EnumerateArray()
                .Select(m => m.GetProperty("id").GetString()!)
                .ToList();

            // Match: model ID starts with the alias (with optional separator), prefer GPU variants
            var match = ids
                .Where(id => id.StartsWith(alias, StringComparison.OrdinalIgnoreCase) ||
                             id.StartsWith(alias.Replace("-", ""), StringComparison.OrdinalIgnoreCase))
                .OrderBy(id => id.Contains("cpu", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .FirstOrDefault();

            if (match is null)
                throw new InvalidOperationException(
                    $"No loaded model matching '{alias}' found in Foundry Local service. " +
                    $"Available: {string.Join(", ", ids)}. " +
                    $"Download and load with: foundry model download {alias}");

            return match;
        }
    }

    private async Task<string> RunFoundryCliAsync(string args, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo("foundry", args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Could not start 'foundry' process.");

            var stdout = await process.StandardOutput.ReadToEndAsync(ct);
            var stderr = await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            logger.LogDebug("foundry {Args} → exit {Code}: {Output}",
                args, process.ExitCode, (stdout + stderr).Trim());

            return stdout + stderr;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                "Could not run the 'foundry' CLI. Is Foundry Local installed? " +
                "Install with: winget install Microsoft.FoundryLocal", ex);
        }
    }

    public void Dispose()
    {
        _lock.Dispose();
        _rootLock.Dispose();
        _embeddingLock.Dispose();
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
}
