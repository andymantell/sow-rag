namespace SoWImprover.BatchRunner;

public class ConsoleLogger(TextWriter writer)
{
    public ConsoleLogger() : this(Console.Out) { }

    public void Log(string message, int indent = 0)
    {
        var prefix = indent > 0 ? new string(' ', indent * 2) : "";
        writer.WriteLine($"[{DateTime.Now:HH:mm:ss}] {prefix}{message}");
    }

    public IProgress<string> AsProgress(int indent = 0) =>
        new SynchronousProgress(msg => Log(msg, indent));

    private sealed class SynchronousProgress(Action<string> callback) : IProgress<string>
    {
        public void Report(string value) => callback(value);
    }
}
