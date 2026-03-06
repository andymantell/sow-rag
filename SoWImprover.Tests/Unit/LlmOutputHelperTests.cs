using SoWImprover.Services;

namespace SoWImprover.Tests.Unit;

public class LlmOutputHelperTests
{
    [Fact]
    public void StripCodeFence_NoFence_ReturnsInputUnchanged()
    {
        var input = "Hello world";
        Assert.Equal("Hello world", LlmOutputHelper.StripCodeFence(input));
    }

    [Fact]
    public void StripCodeFence_SimpleFence_StripsIt()
    {
        var input = "```\nHello world\n```";
        Assert.Equal("Hello world", LlmOutputHelper.StripCodeFence(input));
    }

    [Fact]
    public void StripCodeFence_FenceWithLanguageTag_StripsIt()
    {
        var input = "```json\n{\"key\": \"value\"}\n```";
        Assert.Equal("{\"key\": \"value\"}", LlmOutputHelper.StripCodeFence(input));
    }

    [Fact]
    public void StripCodeFence_FenceWithWhitespace_TrimsResult()
    {
        var input = "  ```markdown\n  content here  \n```  ";
        Assert.Equal("content here", LlmOutputHelper.StripCodeFence(input));
    }

    [Fact]
    public void StripCodeFence_OnlyOpeningFence_HandlesGracefully()
    {
        var input = "```json";
        // No newline after opening fence — returns as-is
        Assert.Equal("```json", LlmOutputHelper.StripCodeFence(input));
    }

    [Fact]
    public void StripCodeFence_ContentWithInternalBackticks_PreservesThem()
    {
        var input = "```\nUse `code` here\nAnd ```nested``` too\n```";
        var result = LlmOutputHelper.StripCodeFence(input);
        Assert.Contains("`code`", result);
        Assert.Contains("```nested```", result);
    }

    [Fact]
    public void StripCodeFence_MultilineContent_PreservesAllLines()
    {
        var input = "```\nLine 1\nLine 2\nLine 3\n```";
        var result = LlmOutputHelper.StripCodeFence(input);
        Assert.Equal("Line 1\nLine 2\nLine 3", result);
    }

    [Fact]
    public void StripCodeFence_WhitespaceOnly_ReturnsEmpty()
    {
        Assert.Equal("", LlmOutputHelper.StripCodeFence("   "));
    }
}
