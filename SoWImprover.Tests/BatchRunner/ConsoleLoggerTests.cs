using SoWImprover.BatchRunner;

namespace SoWImprover.Tests.BatchRunner;

public class ConsoleLoggerTests
{
    [Fact]
    public void Log_IncludesTimestamp()
    {
        var writer = new StringWriter();
        var logger = new ConsoleLogger(writer);

        logger.Log("Test message");

        var output = writer.ToString().TrimEnd();
        Assert.Matches(@"^\[\d{2}:\d{2}:\d{2}\] Test message$", output);
    }

    [Fact]
    public void Log_WithIndent_AddsSpaces()
    {
        var writer = new StringWriter();
        var logger = new ConsoleLogger(writer);

        logger.Log("Indented", indent: 1);

        var output = writer.ToString().TrimEnd();
        Assert.Matches(@"^\[\d{2}:\d{2}:\d{2}\]   Indented$", output);
    }
}
