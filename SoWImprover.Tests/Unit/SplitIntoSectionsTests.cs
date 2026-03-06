using SoWImprover.Services;

namespace SoWImprover.Tests.Unit;

public class SplitIntoSectionsTests
{
    [Fact]
    public void NoHeadings_ReturnsSingleIntroductionSection()
    {
        var text = "This is some body text.\nAnother line.";
        var sections = SoWImproverService.SplitIntoSections(text);

        Assert.Single(sections);
        Assert.Equal("Introduction", sections[0].Title);
        Assert.Contains("This is some body text.", sections[0].Body);
    }

    [Fact]
    public void MarkdownHeadings_SplitsCorrectly()
    {
        var text = "# Section One\nBody of section one.\n## Section Two\nBody of section two.";
        var sections = SoWImproverService.SplitIntoSections(text);

        Assert.Equal(2, sections.Count);
        Assert.Equal("Section One", sections[0].Title);
        Assert.Contains("Body of section one.", sections[0].Body);
        Assert.Equal("Section Two", sections[1].Title);
        Assert.Contains("Body of section two.", sections[1].Body);
    }

    [Fact]
    public void AllCapsHeadings_DetectedAsSections()
    {
        var text = "SCOPE OF WORK\nThe contractor shall deliver.\nPROJECT TIMELINE\nPhase one begins.";
        var sections = SoWImproverService.SplitIntoSections(text);

        Assert.Equal(2, sections.Count);
        Assert.Equal("SCOPE OF WORK", sections[0].Title);
        Assert.Equal("PROJECT TIMELINE", sections[1].Title);
    }

    [Fact]
    public void EmptyText_ReturnsEmptyList()
    {
        var sections = SoWImproverService.SplitIntoSections("");
        Assert.Empty(sections);
    }

    [Fact]
    public void WhitespaceOnlyText_ReturnsEmptyList()
    {
        var sections = SoWImproverService.SplitIntoSections("   \n  \n  ");
        Assert.Empty(sections);
    }

    [Fact]
    public void BoldMarkdownInHeading_StrippedFromTitle()
    {
        var text = "# **Bold Heading**\nSome content.";
        var sections = SoWImproverService.SplitIntoSections(text);

        Assert.Single(sections);
        Assert.Equal("Bold Heading", sections[0].Title);
    }

    [Fact]
    public void SectionsWithOnlyWhitespaceBody_Excluded()
    {
        var text = "# Empty Section\n   \n# Real Section\nContent here.";
        var sections = SoWImproverService.SplitIntoSections(text);

        Assert.Single(sections);
        Assert.Equal("Real Section", sections[0].Title);
    }

    [Fact]
    public void TextBeforeFirstHeading_CapturedAsIntroduction()
    {
        var text = "Intro paragraph.\n# First Heading\nBody text.";
        var sections = SoWImproverService.SplitIntoSections(text);

        Assert.Equal(2, sections.Count);
        Assert.Equal("Introduction", sections[0].Title);
        Assert.Equal("First Heading", sections[1].Title);
    }
}
