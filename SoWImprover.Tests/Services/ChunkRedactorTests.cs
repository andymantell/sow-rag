using NSubstitute;
using SoWImprover.Services;

namespace SoWImprover.Tests.Services;

public class ChunkRedactorTests
{
    // ── Regex-based redaction (fast pass) ───────────────────────────

    // ── Contract / reference numbers ─────────────────────────────

    [Theory]
    [InlineData("SR1391673897")]
    [InlineData("RM6187")]
    [InlineData("CN123456")]
    public void Redact_ReferenceNumbers_Replaced(string refNum)
    {
        var text = $"Call-off reference {refNum} effective immediately.";
        var result = ChunkRedactor.Redact(text);

        Assert.Contains("[REFERENCE]", result);
        Assert.DoesNotContain(refNum, result);
    }

    [Theory]
    [InlineData("SoW-4")]
    [InlineData("SoW-12")]
    public void Redact_SoWReferences_Replaced(string sowRef)
    {
        var text = $"This {sowRef} constitutes a Call-Off Contract.";
        var result = ChunkRedactor.Redact(text);

        Assert.Contains("[REFERENCE]", result);
        Assert.DoesNotContain(sowRef, result);
    }

    // ── Dates ────────────────────────────────────────────────────

    [Theory]
    [InlineData("01/03/2024")]
    [InlineData("30/08/2024")]
    [InlineData("15-10-2024")]
    [InlineData("01.03.2024")]
    public void Redact_NumericDates_Replaced(string date)
    {
        var text = $"Effective from {date} until further notice.";
        var result = ChunkRedactor.Redact(text);

        Assert.Contains("[DATE]", result);
        Assert.DoesNotContain(date, result);
    }

    [Theory]
    [InlineData("15th of October 2024")]
    [InlineData("1st January 2024")]
    [InlineData("23rd March 2025")]
    [InlineData("2nd February 2023")]
    public void Redact_OrdinalDates_Replaced(string date)
    {
        var text = $"The start date of {date} was agreed.";
        var result = ChunkRedactor.Redact(text);

        Assert.Contains("[DATE]", result);
        Assert.DoesNotContain(date, result);
    }

    [Theory]
    [InlineData("January 2024")]
    [InlineData("March 2025")]
    public void Redact_MonthYearDates_Replaced(string date)
    {
        var text = $"Commencing {date} for a period of 12 months.";
        var result = ChunkRedactor.Redact(text);

        Assert.Contains("[DATE]", result);
        Assert.DoesNotContain(date, result);
    }

    // ── Monetary values ──────────────────────────────────────────

    [Theory]
    [InlineData("£1,500,000")]
    [InlineData("£500")]
    [InlineData("£1,234.56")]
    [InlineData("£50,000.00")]
    public void Redact_MonetaryValues_Replaced(string amount)
    {
        var text = $"The total value shall not exceed {amount} inclusive of VAT.";
        var result = ChunkRedactor.Redact(text);

        Assert.Contains("[AMOUNT]", result);
        Assert.DoesNotContain(amount, result);
    }

    [Fact]
    public void Redact_MoneyWithSuffix_Replaced()
    {
        var text = "Budget of £2.5 million for the programme.";
        var result = ChunkRedactor.Redact(text);

        Assert.Contains("[AMOUNT]", result);
    }

    // ── Email addresses ──────────────────────────────────────────

    [Fact]
    public void Redact_EmailAddresses_Replaced()
    {
        var text = "Contact john.smith@example.gov.uk for queries.";
        var result = ChunkRedactor.Redact(text);

        Assert.Contains("[EMAIL]", result);
        Assert.DoesNotContain("john.smith@example.gov.uk", result);
    }

    // ── UK postcodes ─────────────────────────────────────────────

    [Theory]
    [InlineData("SW1A 2BQ")]
    [InlineData("EC3M 3BD")]
    [InlineData("W1A 1AA")]
    public void Redact_Postcodes_Replaced(string postcode)
    {
        var text = $"Located at London, {postcode}.";
        var result = ChunkRedactor.Redact(text);

        Assert.Contains("[POSTCODE]", result);
        Assert.DoesNotContain(postcode, result);
    }

    // ── Phone numbers ────────────────────────────────────────────

    [Theory]
    [InlineData("020 7123 4567")]
    [InlineData("+44 20 7123 4567")]
    public void Redact_PhoneNumbers_Replaced(string phone)
    {
        var text = $"Telephone: {phone} during office hours.";
        var result = ChunkRedactor.Redact(text);

        Assert.Contains("[PHONE]", result);
        Assert.DoesNotContain(phone, result);
    }

    // ── Government abbreviations ─────────────────────────────────

    [Theory]
    [InlineData("HMRC")]
    [InlineData("DVLA")]
    [InlineData("MOD")]
    [InlineData("NHS")]
    [InlineData("DWP")]
    public void Redact_GovAbbreviations_Replaced(string abbrev)
    {
        var text = $"{abbrev} will be responsible for acceptance.";
        var result = ChunkRedactor.Redact(text);

        Assert.Contains("[ORGANISATION]", result);
        Assert.DoesNotContain(abbrev, result);
    }

    // ── Preservation ─────────────────────────────────────────────

    [Fact]
    public void Redact_PreservesStructuralContent()
    {
        var text = """
            The Supplier shall provide qualified resources to complete the activities
            outlined in this Statement of Work. All deliverables must meet the acceptance
            criteria defined in the relevant schedule. The Supplier is responsible for
            ensuring compliance with the Buyer's security requirements.
            """;
        var result = ChunkRedactor.Redact(text);

        // None of this should be changed — it's structural SoW language
        Assert.Contains("qualified resources", result);
        Assert.Contains("Statement of Work", result);
        Assert.Contains("acceptance", result);
        Assert.Contains("security requirements", result);
    }

    [Fact]
    public void Redact_NullOrEmpty_ReturnsAsIs()
    {
        Assert.Equal("", ChunkRedactor.Redact(""));
        Assert.Equal("  ", ChunkRedactor.Redact("  "));
        Assert.Null(ChunkRedactor.Redact(null!));
    }

    // ── Regex pass on realistic chunk ────────────────────────────

    [Fact]
    public void Redact_RealisticChunk_RedactsUnambiguousPatterns()
    {
        var chunk = """
            This SoW, identified with the reference SoW-4, constitutes a Call-Off Contract
            under framework RM6187. The Call-off reference is SR1032578788.
            The engagement runs from 01/03/2024 to 30/08/2024 with a total value of
            £1,500,000. Contact: delivery@accenture.com or 020 7123 4567.
            HMRC will be responsible for acceptance.
            """;
        var result = ChunkRedactor.Redact(chunk);

        // Unambiguous patterns should be redacted by regex
        Assert.DoesNotContain("SoW-4", result);
        Assert.DoesNotContain("RM6187", result);
        Assert.DoesNotContain("SR1032578788", result);
        Assert.DoesNotContain("01/03/2024", result);
        Assert.DoesNotContain("30/08/2024", result);
        Assert.DoesNotContain("1,500,000", result);
        Assert.DoesNotContain("accenture.com", result);
        Assert.DoesNotContain("020 7123", result);
        Assert.DoesNotContain("HMRC", result);

        // Structural content should remain
        Assert.Contains("Call-Off Contract", result);
        Assert.Contains("framework", result);
        Assert.Contains("engagement", result);
    }

    // ── LLM-based redaction ──────────────────────────────────────

    [Fact]
    public async Task RedactWithLlm_CallsLlmAfterRegexPrepass()
    {
        var chat = Substitute.For<IChatService>();
        chat.CompleteAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("The contract between [ORGANISATION] and the Buyer shall commence.");

        var text = "The contract between Accenture (UK) Limited and the Buyer shall commence.";
        var result = await ChunkRedactor.RedactWithLlmAsync(text, chat, CancellationToken.None);

        Assert.Contains("[ORGANISATION]", result);
        Assert.DoesNotContain("Accenture", result);

        // Verify LLM was called with pre-redacted text
        await chat.Received(1).CompleteAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RedactWithLlm_PreRedactsUnambiguousPatterns()
    {
        // The prompt sent to the LLM should already have regex-redacted patterns
        string capturedPrompt = "";
        var chat = Substitute.For<IChatService>();
        chat.CompleteAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                capturedPrompt = ci.ArgAt<string>(0);
                return "redacted output";
            });

        var text = "Contact john@example.com about £500,000 on 01/03/2024.";
        await ChunkRedactor.RedactWithLlmAsync(text, chat, CancellationToken.None);

        // The prompt should contain the pre-redacted text (regex already applied)
        Assert.Contains("[EMAIL]", capturedPrompt);
        Assert.Contains("[AMOUNT]", capturedPrompt);
        Assert.Contains("[DATE]", capturedPrompt);
        Assert.DoesNotContain("john@example.com", capturedPrompt);
    }

    [Fact]
    public async Task RedactWithLlm_HandlesCompanyNames()
    {
        var chat = Substitute.For<IChatService>();
        chat.CompleteAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("[ORGANISATION] shall provide the services.");

        var text = "Accenture (UK) Limited shall provide the services.";
        var result = await ChunkRedactor.RedactWithLlmAsync(text, chat, CancellationToken.None);

        Assert.Contains("[ORGANISATION]", result);
        Assert.DoesNotContain("Accenture", result);
    }

    [Fact]
    public async Task RedactWithLlm_HandlesGovDepartments()
    {
        var chat = Substitute.For<IChatService>();
        chat.CompleteAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("The [ORGANISATION], as the contracting authority.");

        var text = "The HM Revenue and Customs, as the contracting authority.";
        var result = await ChunkRedactor.RedactWithLlmAsync(text, chat, CancellationToken.None);

        Assert.Contains("[ORGANISATION]", result);
        Assert.DoesNotContain("Revenue", result);
    }

    [Fact]
    public async Task RedactWithLlm_HandlesPersonNames()
    {
        var chat = Substitute.For<IChatService>();
        chat.CompleteAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("The programme lead, [PERSON], will oversee delivery.");

        var text = "The programme lead, Mr John Smith, will oversee delivery.";
        var result = await ChunkRedactor.RedactWithLlmAsync(text, chat, CancellationToken.None);

        Assert.Contains("[PERSON]", result);
        Assert.DoesNotContain("John Smith", result);
    }

    [Fact]
    public async Task RedactWithLlm_HandlesStreetAddresses()
    {
        var chat = Substitute.For<IChatService>();
        chat.CompleteAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("with offices at [ADDRESS].");

        var text = "with offices at 100 Parliament Street.";
        var result = await ChunkRedactor.RedactWithLlmAsync(text, chat, CancellationToken.None);

        Assert.Contains("[ADDRESS]", result);
        Assert.DoesNotContain("Parliament", result);
    }

    [Fact]
    public async Task RedactWithLlm_NullOrEmpty_ReturnsAsIs()
    {
        var chat = Substitute.For<IChatService>();

        Assert.Equal("", await ChunkRedactor.RedactWithLlmAsync("", chat, CancellationToken.None));
        Assert.Equal("  ", await ChunkRedactor.RedactWithLlmAsync("  ", chat, CancellationToken.None));
        Assert.Null(await ChunkRedactor.RedactWithLlmAsync(null!, chat, CancellationToken.None));

        // LLM should not be called for empty input
        await chat.DidNotReceive().CompleteAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RedactWithLlm_StripsCodeFence()
    {
        var chat = Substitute.For<IChatService>();
        chat.CompleteAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("```\nThe redacted text.\n```");

        var text = "Some text to redact.";
        var result = await ChunkRedactor.RedactWithLlmAsync(text, chat, CancellationToken.None);

        Assert.DoesNotContain("```", result);
        Assert.Contains("The redacted text.", result);
    }

    // ── Source label anonymisation ────────────────────────────────

    [Fact]
    public void AnonymiseSourceLabel_ReturnsIndexedLabel()
    {
        Assert.Equal("[Example 1]", ChunkRedactor.AnonymiseSourceLabel(0));
        Assert.Equal("[Example 2]", ChunkRedactor.AnonymiseSourceLabel(1));
        Assert.Equal("[Example 5]", ChunkRedactor.AnonymiseSourceLabel(4));
    }
}
