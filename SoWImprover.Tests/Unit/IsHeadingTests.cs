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

    [Theory]
    [InlineData("#hashtag")]
    [InlineData("#nosuchheading")]
    [InlineData("#123")]
    public void HashWithoutSpace_NotDetectedAsHeading(string line)
    {
        Assert.False(SoWImproverService.IsHeading(line));
    }

    [Theory]
    [InlineData("123 456")]
    [InlineData("-- --")]
    public void NonLetterAllCaps_NotDetectedAsHeadings(string line)
    {
        Assert.False(SoWImproverService.IsHeading(line));
    }

    [Theory]
    [InlineData("**Annex 1 (Template Statement of Work)**")]
    [InlineData("**Buyer Requirements – SoW Deliverables**")]
    [InlineData("**Risks:**")]
    [InlineData("**Assumptions:**")]
    [InlineData("2 **Buyer Requirements – SoW Deliverables**")]
    [InlineData("3 **Charges**")]
    [InlineData("1. **Statement of Works (SoW) Details**")]
    public void BoldHeadings_DetectedAsHeadings(string line)
    {
        Assert.True(SoWImproverService.IsHeading(line));
    }

    [Theory]
    [InlineData("**Date of SoW:** 16 June 2025")]
    [InlineData("**Buyer:** Department for Business and Trade")]
    [InlineData("**SoW Start Date:** 17 June 2025")]
    public void BoldFieldLabels_NotDetectedAsHeadings(string line)
    {
        Assert.False(SoWImproverService.IsHeading(line));
    }
}
