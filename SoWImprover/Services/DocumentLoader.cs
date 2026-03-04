using SoWImprover.Models;
using System.Diagnostics;

namespace SoWImprover.Services;

public class DocumentLoader(IConfiguration config, ILogger<DocumentLoader> logger)
{
    private readonly int _chunkSize = config.GetValue<int>("Docs:ChunkSize", 500);
    private readonly int _chunkOverlap = config.GetValue<int>("Docs:ChunkOverlap", 50);
    private string? _pythonExe;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Loads and chunks all PDFs in the folder. Called synchronously at startup.</summary>
    public List<DocumentChunk> LoadFolder(string folderPath)
    {
        var fullPath = Path.GetFullPath(folderPath);
        var files = Directory.GetFiles(fullPath, "*.pdf", SearchOption.TopDirectoryOnly);

        if (files.Length == 0)
            throw new InvalidOperationException(
                $"No PDFs found in KnownGoodFolder '{fullPath}' — populate the folder before starting.");

        var chunks = new List<DocumentChunk>();
        foreach (var file in files)
        {
            var text = ExtractText(file);
            chunks.AddRange(ChunkText(text, Path.GetFileName(file)));
        }
        return chunks;
    }

    /// <summary>Synchronous extraction — used during corpus loading at startup.</summary>
    public string ExtractText(string filePath)
    {
        logger.LogDebug("Extracting text from {File}", Path.GetFileName(filePath));
        return RunPythonScript(filePath);
    }

    /// <summary>Async extraction — used for uploaded PDFs during a request.</summary>
    public Task<string> ExtractTextAsync(string filePath, CancellationToken ct = default)
        => Task.Run(() => RunPythonScript(filePath), ct);

    public string[] GetPdfFiles(string folderPath)
        => Directory.GetFiles(Path.GetFullPath(folderPath), "*.pdf", SearchOption.TopDirectoryOnly);

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
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        process.ErrorDataReceived  += (_, e) => { if (e.Data is not null) error.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"PDF extraction failed (exit {process.ExitCode}): {error.ToString().Trim()}");

        return output.ToString();
    }

    private string GetPythonExe() => _pythonExe ??= FindPython();

    private static string FindPython()
    {
        // py.exe is the Windows Python Launcher and is most reliable on Windows
        foreach (var candidate in new[] { "py", "python3", "python" })
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = candidate,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(3000);
                if (p?.ExitCode == 0) return candidate;
            }
            catch { /* not found, try next */ }
        }
        throw new InvalidOperationException(
            "Python not found. Install Python 3 and run: pip install pymupdf4llm");
    }

    // ── Chunking ──────────────────────────────────────────────────────────────

    private List<DocumentChunk> ChunkText(string text, string sourceFile)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
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
            i += _chunkSize - _chunkOverlap;
        }

        return chunks;
    }
}
