namespace SoWImprover.Services;

public class DiffService
{
    public (string Original, string Improved) Prepare(string original, string improved)
        => (Normalise(original), Normalise(improved));

    private static string Normalise(string text)
        => text.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
}
