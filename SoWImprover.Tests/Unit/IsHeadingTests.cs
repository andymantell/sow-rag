using SoWImprover.Services;

namespace SoWImprover.Tests.Unit;

public class IsHeadingTests
{
    [Theory]
    [InlineData("# Heading")]
    [InlineData("## Sub Heading")]
    [InlineData("### Deep Heading")]
    public void MarkdownHeadings_DetectedAsHeadings(string line)
    {
        Assert.True(SoWImproverService.IsHeading(line));
    }

    [Theory]
    [InlineData("SCOPE OF WORK")]
    [InlineData("PROJECT TIMELINE AND MILESTONES")]
    [InlineData("ROLES AND RESPONSIBILITIES")]
    public void AllCapsMultiWord_DetectedAsHeadings(string line)
    {
        Assert.True(SoWImproverService.IsHeading(line));
    }

    [Theory]
    [InlineData("UK")]
    [InlineData("SLA")]
    [InlineData("IT")]
    [InlineData("API")]
    public void ShortAbbreviations_NotDetectedAsHeadings(string line)
    {
        Assert.False(SoWImproverService.IsHeading(line));
    }

    [Theory]
    [InlineData("| HEADER | COLUMN |")]
    [InlineData("| DATA |")]
    public void TableCells_NotDetectedAsHeadings(string line)
    {
        Assert.False(SoWImproverService.IsHeading(line));
    }

    [Theory]
    [InlineData("- LIST ITEM ONE")]
    [InlineData("* BULLET POINT")]
    public void ListItems_NotDetectedAsHeadings(string line)
    {
        Assert.False(SoWImproverService.IsHeading(line));
    }

    [Theory]
    [InlineData("This is a normal sentence.")]
    [InlineData("Mixed Case Title")]
    [InlineData("")]
    [InlineData("AB")]
    public void RegularText_NotDetectedAsHeadings(string line)
    {
        Assert.False(SoWImproverService.IsHeading(line));
    }

    [Fact]
    public void SingleUppercaseWord_NotDetectedAsHeading()
    {
        // Single word, even if long, should not be a heading
        Assert.False(SoWImproverService.IsHeading("INTRODUCTION"));
    }
}
