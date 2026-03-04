namespace SoWImprover.Services;

public class DiffService
{
    /// <summary>
    /// Normalises line endings in both strings so they are consistent before client-side diffing.
    /// </summary>
    public (string Original, string Improved) Prepare(string original, string improved)
        => (Normalise(original), Normalise(improved));

    private static string Normalise(string text)
        => text.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
}
