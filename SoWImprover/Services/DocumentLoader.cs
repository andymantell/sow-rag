using SoWImprover.Models;
using System.Diagnostics;

namespace SoWImprover.Services;

public class DocumentLoader(IConfiguration config, ILogger<DocumentLoader> logger)
{
    private readonly int _chunkSize = config.GetValue<int>("Docs:ChunkSize", 500);
    private readonly int _chunkOverlap = config.GetValue<int>("Docs:ChunkOverlap", 50);

    // Cache of raw extracted texts keyed by filename, populated during LoadFolder.
    // Allows DefinitionGeneratorService to reuse the text without a second subprocess call.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _extractedTexts = new();

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads and chunks all PDFs in the folder. Called synchronously at startup.
    /// Also caches the raw extracted text of each document for use by
    /// <see cref="GetCachedTexts"/>, avoiding duplicate subprocess calls.
    /// Throws if no PDFs are found.
    /// </summary>
    public virtual List<DocumentChunk> LoadFolder(string folderPath)
    {
        var fullPath = Path.GetFullPath(folderPath);
        var files = Directory.GetFiles(fullPath, "*.pdf", SearchOption.TopDirectoryOnly);

        if (files.Length == 0)
            throw new InvalidOperationException(
                $"No PDFs found in KnownGoodFolder '{fullPath}' — populate the folder before starting.");

        _extractedTexts.Clear();
        var chunks = new List<DocumentChunk>();
        foreach (var file in files)
        {
            var text = ExtractText(file);
            _extractedTexts[Path.GetFileName(file)] = text;
            chunks.AddRange(ChunkText(text, Path.GetFileName(file)));
        }

        return chunks;
    }

    /// <summary>
    /// Returns the raw texts extracted during the most recent <see cref="LoadFolder"/> call.
    /// Avoids re-running the Python subprocess for corpus documents already extracted at startup.
    /// </summary>
    public virtual IReadOnlyList<(string FileName, string Text)> GetCachedTexts()
        => _extractedTexts.Select(kv => (kv.Key, kv.Value)).ToList();

    /// <summary>Synchronous extraction — used during corpus loading at startup.</summary>
    public string ExtractText(string filePath)
    {
        logger.LogDebug("Extracting text from {File}", Path.GetFileName(filePath));
        return RunPythonScript(filePath);
    }

    /// <summary>Async extraction — used for uploaded PDFs during a request.</summary>
    public virtual Task<string> ExtractTextAsync(string filePath, CancellationToken ct = default)
        => Task.Run(() => RunPythonScript(filePath), ct);

    // ── PDF extraction via pymupdf4llm subprocess ─────────────────────────────

    private string RunPythonScript(string pdfPath)
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "pdf_to_markdown.py");
        if (!File.Exists(scriptPath))
            throw new FileNotFoundException(
                $"pdf_to_markdown.py not found at '{scriptPath}'. Ensure it is copied to the output directory.");

        var python = GetPythonExe();
        var psi = new ProcessStartInfo
        {
            FileName = python,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add(scriptPath);
        psi.ArgumentList.Add(pdfPath);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start Python process.");

        // Read stdout and stderr concurrently to prevent buffer deadlock
        var output = new System.Text.StringBuilder();
        var error = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null && !e.Data.Contains("Consider using the pymupdf_layout package"))
                output.AppendLine(e.Data);
        };
        process.ErrorDataReceived  += (_, e) => { if (e.Data is not null) error.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        const int TimeoutMs = 60_000;
        if (!process.WaitForExit(TimeoutMs))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            throw new InvalidOperationException(
                $"PDF extraction timed out after {TimeoutMs / 1000} seconds. The PDF may be corrupt or very large.");
        }
        // Ensure all async output read callbacks have completed.
        // The parameterless overload is required to flush BeginOutputReadLine/BeginErrorReadLine buffers.
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"PDF extraction failed (exit {process.ExitCode}): {error.ToString().Trim()}");

        return output.ToString();
    }

    private static string GetPythonExe() => PythonLocator.Find();

    // ── Chunking ──────────────────────────────────────────────────────────────

    internal List<DocumentChunk> ChunkText(string text, string sourceFile)
    {
        var step = _chunkSize - _chunkOverlap;
        if (step <= 0)
            throw new InvalidOperationException(
                $"Docs:ChunkSize ({_chunkSize}) must be greater than Docs:ChunkOverlap ({_chunkOverlap}).");

        var words = text.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<DocumentChunk>();
        int i = 0, index = 0;

        while (i < words.Length)
        {
            var take = Math.Min(_chunkSize, words.Length - i);
            chunks.Add(new DocumentChunk
            {
                SourceFile = sourceFile,
                Text = string.Join(" ", words, i, take),
                ChunkIndex = index++
            });
            i += step;
        }

        return chunks;
    }
}
