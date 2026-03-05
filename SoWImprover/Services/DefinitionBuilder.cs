using System.Text.RegularExpressions;
using OpenAI.Chat;
using SoWImprover.Models;

namespace SoWImprover.Services;

public class DefinitionBuilder(FoundryClientFactory factory, ILogger<DefinitionBuilder> logger)
{
    // Truncation limits keep prompts within a model's context window.
    private const int MaxDocChars = 12_000;

    // Token budget for each LLM call.
    private const int AnalysisMaxTokens = 2048;
    private const int SynthesisMaxTokens = 2048;

    /// <summary>
    /// Generates a "definition of good" markdown document by analysing each corpus document
    /// individually and then synthesising a section-by-section definition from those analyses.
    /// </summary>
    /// <param name="documents">Filename/text pairs for every document in the known-good corpus.</param>
    public async Task<string> BuildDefinitionAsync(
        IReadOnlyList<(string FileName, string Text)> documents,
        CancellationToken ct = default)
    {
        var client = await factory.GetChatClientAsync(ct);
        var summaries = new List<string>(documents.Count);

        foreach (var (fileName, text) in documents)
        {
            logger.LogInformation("Analysing document: {FileName}", fileName);
            summaries.Add(await AnalyseDocumentAsync(client, fileName, text, ct));
        }

        logger.LogInformation("Synthesising definition of good from {Count} document(s)", summaries.Count);
        return await SynthesiseAsync(client, summaries, ct);
    }

    private static async Task<string> AnalyseDocumentAsync(
        ChatClient client, string fileName, string text, CancellationToken ct)
    {
        if (text.Length > MaxDocChars)
            text = text[..MaxDocChars] + "\n[truncated]";

        var prompt = $"""
            You are an expert in Statements of Work (SoW) documents.

            Analyse the SoW document below and describe what makes it a good SoW across these six aspects:
            1. Clarity of deliverables
            2. Milestone and acceptance criteria
            3. Payment terms
            4. IP ownership
            5. Scope boundaries
            6. Risk and change control

            For each aspect write a short paragraph describing the abstract principles and patterns this document
            demonstrates. Do NOT quote specific text, names, figures, dates, or other content from the document —
            describe only the structural and editorial patterns in general terms.
            Use the heading "## Aspect N: <name>" for each section.

            Document: {fileName}
            ---
            {text}
            """;

        var opts = new ChatCompletionOptions { MaxOutputTokenCount = AnalysisMaxTokens };
        var result = await client.CompleteChatAsync([new UserChatMessage(prompt)], opts, cancellationToken: ct);
        return StripCodeFences(result.Value.Content[0].Text);
    }

    private static readonly (string Heading, string AspectName)[] Sections =
    [
        ("## 1. Clarity of Deliverables",          "clarity of deliverables"),
        ("## 2. Milestone and Acceptance Criteria", "milestone and acceptance criteria"),
        ("## 3. Payment Terms",                     "payment terms"),
        ("## 4. IP Ownership",                      "IP ownership"),
        ("## 5. Scope Boundaries",                  "scope boundaries"),
        ("## 6. Risk and Change Control",           "risk and change control"),
    ];

    private async Task<string> SynthesiseAsync(
        ChatClient client, List<string> summaries, CancellationToken ct)
    {
        var joined = string.Join("\n\n---\n\n",
            summaries.Select((s, i) => $"**Document {i + 1} analysis:**\n{s}"));

        var sectionTexts = new List<string>(Sections.Length);

        foreach (var (heading, aspectName) in Sections)
        {
            logger.LogInformation("Synthesising section: {Section}", heading);
            var prompt = $"""
                You are an expert in Statements of Work (SoW) documents.

                Below are quality analyses of {summaries.Count} known-good SoW document(s).
                Focus only on the aspect: **{aspectName}**.

                Write the content for this single section of an authoritative "Definition of Good" for SoW documents:

                {heading}

                Describe the standards and best practices that characterise a high-quality SoW for this aspect,
                drawing on the patterns observed across all documents. Be specific and actionable.
                The output must be generic, reusable guidance applicable to any SoW — do not reference specific
                document names, supplier or client names, monetary values, dates, project titles, or any other
                concrete details drawn from the source documents. Write as if authoring a style guide for a
                procurement team, not a summary of particular contracts.
                Output only the section heading and body — no preamble, no other sections.

                ---
                {joined}
                """;

            var opts = new ChatCompletionOptions { MaxOutputTokenCount = SynthesisMaxTokens };
            var result = await client.CompleteChatAsync([new UserChatMessage(prompt)], opts, cancellationToken: ct);
            sectionTexts.Add(StripCodeFences(result.Value.Content[0].Text));
        }

        var intro = $"""
            # Definition of Good for Statement of Work Documents

            The following sections outline the standards and best practices for high-quality Statement of Work (SoW) documents, synthesised from analyses of {summaries.Count} known-good document(s).

            """;

        return intro + string.Join("\n\n", sectionTexts);
    }

    private static string StripCodeFences(string text)
    {
        text = text.Trim();
        if (!text.StartsWith("```"))
            return text;
        text = Regex.Replace(text, @"^```[a-z]*\n?", "", RegexOptions.Multiline);
        return text.TrimEnd('`', '\n', ' ');
    }
}
