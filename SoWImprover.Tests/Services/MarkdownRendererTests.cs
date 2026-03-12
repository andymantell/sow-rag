using SoWImprover.Services;

namespace SoWImprover.Tests.Services;

public class MarkdownRendererTests
{
    // ── XSS protection (DisableHtml) ─────────────────────────────

    [Fact]
    public void ToMarkupString_HtmlTags_AreEscaped()
    {
        var result = MarkdownRenderer.ToMarkupString("<script>alert('xss')</script>");
        var html = result.Value;

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void ToMarkupString_InlineHtml_IsEscaped()
    {
        var result = MarkdownRenderer.ToMarkupString("Hello <b>bold</b> world");
        var html = result.Value;

        Assert.DoesNotContain("<b>bold</b>", html);
        Assert.Contains("&lt;b&gt;", html);
    }

    [Fact]
    public void ToMarkupString_ImgTag_IsEscaped()
    {
        var result = MarkdownRenderer.ToMarkupString("<img src=x onerror=alert(1)>");
        var html = result.Value;

        Assert.DoesNotContain("<img", html);
    }

    // ── GOV.UK class injection ───────────────────────────────────

    [Fact]
    public void ToMarkupString_Heading1_GetsGovUkClass()
    {
        var result = MarkdownRenderer.ToMarkupString("# Title");
        Assert.Contains("<h1 class=\"govuk-heading-xl\">", result.Value);
    }

    [Fact]
    public void ToMarkupString_Heading2_GetsGovUkClass()
    {
        var result = MarkdownRenderer.ToMarkupString("## Subtitle");
        Assert.Contains("<h2 class=\"govuk-heading-l\">", result.Value);
    }

    [Fact]
    public void ToMarkupString_Heading3_GetsGovUkClass()
    {
        var result = MarkdownRenderer.ToMarkupString("### Section");
        Assert.Contains("<h3 class=\"govuk-heading-m\">", result.Value);
    }

    [Fact]
    public void ToMarkupString_Paragraph_GetsGovUkClass()
    {
        var result = MarkdownRenderer.ToMarkupString("Some body text.");
        Assert.Contains("<p class=\"govuk-body\">", result.Value);
    }

    [Fact]
    public void ToMarkupString_BulletList_GetsGovUkClasses()
    {
        var result = MarkdownRenderer.ToMarkupString("- Item one\n- Item two");
        Assert.Contains("<ul class=\"govuk-list govuk-list--bullet\">", result.Value);
    }

    [Fact]
    public void ToMarkupString_NumberedList_GetsGovUkClasses()
    {
        var result = MarkdownRenderer.ToMarkupString("1. First\n2. Second");
        Assert.Contains("<ol class=\"govuk-list govuk-list--number\">", result.Value);
    }

    [Fact]
    public void ToMarkupString_Table_GetsGovUkClasses()
    {
        var md = "| Col A | Col B |\n|-------|-------|\n| 1     | 2     |";
        var result = MarkdownRenderer.ToMarkupString(md);
        var html = result.Value;

        Assert.Contains("class=\"govuk-table\"", html);
        Assert.Contains("class=\"govuk-table__head\"", html);
        Assert.Contains("class=\"govuk-table__body\"", html);
        Assert.Contains("class=\"govuk-table__row\"", html);
        Assert.Contains("class=\"govuk-table__header\"", html);
        Assert.Contains("class=\"govuk-table__cell\"", html);
    }

    [Fact]
    public void ToMarkupString_TableWithAlignment_PreservesStyleAttribute()
    {
        var md = "| Left | Right |\n|:-----|------:|\n| a    | b     |";
        var result = MarkdownRenderer.ToMarkupString(md);
        var html = result.Value;

        // th/td should have both govuk class and style attribute
        Assert.Contains("govuk-table__header", html);
        Assert.Contains("style=", html);
    }

    // ── ToInlineMarkupString ─────────────────────────────────────

    [Fact]
    public void ToInlineMarkupString_StripsOuterParagraphWrapper()
    {
        var result = MarkdownRenderer.ToInlineMarkupString("Improved clarity");
        var html = result.Value;

        Assert.DoesNotContain("<p", html);
        Assert.Contains("Improved clarity", html);
    }

    [Fact]
    public void ToInlineMarkupString_PreservesInlineFormatting()
    {
        var result = MarkdownRenderer.ToInlineMarkupString("Added **bold** text");
        var html = result.Value;

        Assert.Contains("<strong>bold</strong>", html);
        Assert.DoesNotContain("<p", html);
    }

    [Fact]
    public void ToInlineMarkupString_HtmlInjection_IsEscaped()
    {
        var result = MarkdownRenderer.ToInlineMarkupString("<script>alert(1)</script>");
        Assert.DoesNotContain("<script>", result.Value);
    }

    // ── Null / empty input ───────────────────────────────────────

    [Fact]
    public void ToMarkupString_NullInput_ReturnsEmptyHtml()
    {
        var result = MarkdownRenderer.ToMarkupString(null!);
        Assert.NotNull(result.Value);
    }

    [Fact]
    public void ToMarkupString_EmptyString_ReturnsEmptyHtml()
    {
        var result = MarkdownRenderer.ToMarkupString("");
        Assert.Equal("", result.Value.Trim());
    }

    [Fact]
    public void ToInlineMarkupString_NullInput_ReturnsEmptyHtml()
    {
        var result = MarkdownRenderer.ToInlineMarkupString(null!);
        Assert.NotNull(result.Value);
    }

    // ── Markdown features ────────────────────────────────────────

    [Fact]
    public void ToMarkupString_BoldAndItalic_Rendered()
    {
        var result = MarkdownRenderer.ToMarkupString("**bold** and *italic*");
        var html = result.Value;

        Assert.Contains("<strong>bold</strong>", html);
        Assert.Contains("<em>italic</em>", html);
    }

    [Fact]
    public void ToMarkupString_AutoLinks_Rendered()
    {
        var result = MarkdownRenderer.ToMarkupString("Visit https://example.com today");
        Assert.Contains("href=\"https://example.com\"", result.Value);
    }
}
