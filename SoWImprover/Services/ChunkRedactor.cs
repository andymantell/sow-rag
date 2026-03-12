using System.Text.RegularExpressions;

namespace SoWImprover.Services;

/// <summary>
/// Redacts identifying details from RAG corpus chunks. Uses a two-pass approach:
/// 1. Fast regex pass for unambiguous patterns (emails, postcodes, money, dates, phones, refs).
/// 2. LLM pass for contextual entities (company names, government bodies, person names, addresses).
/// The redacted text retains structure, tone, and phrasing so the LLM can still
/// learn style from the examples.
/// </summary>
internal static partial class ChunkRedactor
{
    private const int RedactionMaxTokens = 1024;

    /// <summary>
    /// Fast regex-only redaction for unambiguous patterns: emails, postcodes, money,
    /// references, dates, phones, and government abbreviations.
    /// </summary>
    public static string Redact(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        // Order matters — more specific patterns before general ones

        // ── Email addresses ─────────────────────────────────────────
        text = EmailRegex().Replace(text, "[EMAIL]");

        // ── UK postcodes (e.g. SW1A 2BQ, EC3M 3BD) ─────────────────
        text = PostcodeRegex().Replace(text, "[POSTCODE]");

        // ── Monetary values (£1,500,000.00, £500) ───────────────────
        text = MoneyRegex().Replace(text, "[AMOUNT]");

        // ── Contract / reference numbers ────────────────────────────
        // SoW-4, RM6187, SR1391673897, CN-123456, etc.
        text = SowRefRegex().Replace(text, "[REFERENCE]");
        text = AlphaNumRefRegex().Replace(text, "[REFERENCE]");

        // ── Dates ───────────────────────────────────────────────────
        // DD/MM/YYYY, DD-MM-YYYY, DD.MM.YYYY
        text = NumericDateRegex().Replace(text, "[DATE]");
        // "1st January 2024", "15th of October 2024"
        text = OrdinalDateRegex().Replace(text, "[DATE]");
        // "January 2024", "March 2025"
        text = MonthYearRegex().Replace(text, "[DATE]");

        // ── Phone numbers ───────────────────────────────────────────
        text = PhoneRegex().Replace(text, "[PHONE]");

        // ── Government abbreviations (explicit list — reliable) ─────
        text = GovAbbrevRegex().Replace(text, "[ORGANISATION]");

        return text;
    }

    /// <summary>
    /// Full redaction: regex pre-pass for unambiguous patterns, then LLM for contextual
    /// entities (company names, government departments, person names, addresses).
    /// </summary>
    public static async Task<string> RedactWithLlmAsync(
        string text, IChatService chatService, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        // Fast pre-pass strips the obvious patterns so the LLM has less work
        var preRedacted = Redact(text);

        var prompt = $$"""
            You are a redaction tool. Your ONLY job is to replace identifying details in the
            text below with placeholder tokens. Keep ALL other text — structure, phrasing,
            tone, and meaning — exactly the same.

            Replace these types of identifying details with the corresponding placeholder:
            - Organisation/company names → [ORGANISATION]
            - Government department names (e.g. "HM Revenue and Customs", "Ministry of Defence") → [ORGANISATION]
            - Person names (with or without titles) → [PERSON]
            - Street addresses (e.g. "100 Parliament Street") → [ADDRESS]
            - City/town names when part of an address → [ADDRESS]
            - Any other identifying proper nouns specific to a particular contract → [REDACTED]

            Do NOT replace:
            - Generic SoW/contract terms (e.g. "the Buyer", "the Supplier", "Call-Off Contract")
            - Role titles without names (e.g. "Programme Director", "Service Manager")
            - Placeholders already present (text in square brackets like [EMAIL], [DATE], etc.)
            - Section headings, bullet structures, or formatting

            Output ONLY the redacted text. No preamble, no explanation, no markdown fences.

            TEXT TO REDACT:
            {{preRedacted}}
            """;

        var result = (await chatService.CompleteAsync(prompt, RedactionMaxTokens, ct)).Trim();
        return LlmOutputHelper.StripCodeFence(result);
    }

    /// <summary>
    /// Replaces source file names with anonymous labels: [Example 1], [Example 2], etc.
    /// </summary>
    public static string AnonymiseSourceLabel(int index) => $"[Example {index + 1}]";

    // ── Compiled regex patterns (unambiguous only) ────────────────────

    [GeneratedRegex(@"\b[\w.+-]+@[\w.-]+\.\w+\b")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"\b[A-Z]{1,2}\d[A-Z\d]?\s*\d[A-Z]{2}\b")]
    private static partial Regex PostcodeRegex();

    [GeneratedRegex(@"£[\d,]+(?:\.\d{1,2})?(?:\s*(?:million|billion|m|bn|k|per\s+\w+))?", RegexOptions.IgnoreCase)]
    private static partial Regex MoneyRegex();

    [GeneratedRegex(@"\bSoW[- ]\d+\b", RegexOptions.IgnoreCase)]
    private static partial Regex SowRefRegex();

    // 2-4 uppercase letters followed by 4+ digits (SR1391673897, RM6187, CN123456)
    [GeneratedRegex(@"\b[A-Z]{2,4}\d{4,}\b")]
    private static partial Regex AlphaNumRefRegex();

    [GeneratedRegex(@"\b\d{1,2}[/.-]\d{1,2}[/.-]\d{2,4}\b")]
    private static partial Regex NumericDateRegex();

    [GeneratedRegex(
        @"\b\d{1,2}(?:st|nd|rd|th)?\s+(?:of\s+)?(?:January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{4}\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex OrdinalDateRegex();

    [GeneratedRegex(
        @"\b(?:January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{4}\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex MonthYearRegex();

    [GeneratedRegex(@"(?:\b0\d{2,4}\s?\d{3,4}\s?\d{3,4}\b|\+44\s?\d[\d\s]{8,12}\d)")]
    private static partial Regex PhoneRegex();

    // Common abbreviations: HMRC, DVLA, MOD, DWP, NHS, MoJ, MoD, BEIS, DCMS, etc.
    [GeneratedRegex(@"\b(?:HMRC|DVLA|MOD|MoD|DWP|NHS|MoJ|BEIS|DCMS|DEFRA|DfE|DfT|DLUHC|FCO|FCDO|GDS|ONS|UKHO)\b")]
    private static partial Regex GovAbbrevRegex();
}
