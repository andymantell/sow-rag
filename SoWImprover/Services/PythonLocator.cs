using System.Diagnostics;

namespace SoWImprover.Services;

/// <summary>
/// Locates a usable Python executable on the system PATH.
/// Tries py (Windows launcher), python3, and python in order.
/// </summary>
public static class PythonLocator
{
    private static readonly object Lock = new();
    private static string? _cached;

    public static string Find()
    {
        if (_cached is not null) return _cached;
        lock (Lock)
        {
            return _cached ??= Probe();
        }
    }

    private static string Probe()
    {
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
            catch
            {
                // Candidate not available — try next
            }
        }
        throw new InvalidOperationException(
            "Python not found. Install Python 3 and run: pip install pymupdf4llm ragas");
    }
}
