using System.Diagnostics;

namespace SoWImprover.Services;

/// <summary>
/// Frees GPU VRAM by unloading models from Ollama and Foundry Local.
/// Used before switching between improvement (Foundry phi-4) and evaluation (Ollama qwen) phases.
/// </summary>
public class GpuMemoryManager(
    IConfiguration configuration,
    ILogger<GpuMemoryManager> logger)
{
    /// <summary>
    /// Unloads Ollama evaluation model and Foundry chat model to free VRAM for evaluation.
    /// </summary>
    public async Task PrepareForEvaluationAsync(CancellationToken ct = default)
    {
        await UnloadFoundryModelAsync(ct);
        await UnloadOllamaModelAsync(configuration["Ollama:EmbeddingModelName"], ct);
    }

    /// <summary>
    /// Unloads Ollama evaluation model to free VRAM for improvement (Foundry + embeddings).
    /// </summary>
    public async Task PrepareForImprovementAsync(CancellationToken ct = default)
    {
        await UnloadOllamaModelAsync(configuration["Evaluation:ModelName"], ct);
    }

    private async Task UnloadFoundryModelAsync(CancellationToken ct)
    {
        var model = configuration["Foundry:LocalModelName"];
        if (string.IsNullOrEmpty(model)) return;

        try
        {
            logger.LogInformation("Unloading Foundry model '{Model}' to free VRAM", model);
            await RunProcessAsync("foundry", ["model", "unload", model], ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to unload Foundry model '{Model}'", model);
        }
    }

    private async Task UnloadOllamaModelAsync(string? model, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(model)) return;

        try
        {
            logger.LogInformation("Stopping Ollama model '{Model}' to free VRAM", model);
            await RunProcessAsync("ollama", ["stop", model], ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to stop Ollama model '{Model}'", model);
        }
    }

    private static async Task RunProcessAsync(string fileName, string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start '{fileName}'");

        await process.WaitForExitAsync(ct);
    }
}
