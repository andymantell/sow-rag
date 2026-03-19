using SoWImprover.Models;

namespace SoWImprover.Services;

public class DefinitionBuilder(IChatService chatService, ILogger<DefinitionBuilder> logger)
{
    private const int MaxDocChars = 12_000;
    private const int AnalysisMaxTokens = 2048;
    private const int SynthesisMaxTokens = 2048;

    /// <summary>Canonical SoW sections used to generate the definition of good.</summary>
    private static readonly IReadOnlyList<string> CanonicalSections =
    [
        "Acceptance Criteria",
        "Deliverables",
        "Introduction/Background",
        "Project Management and Reporting",
        "Project Requirements",
        "Project Timeline and Milestones",
        "Roles and Responsibilities",
        "Scope of Work",
        "Budget and Payment Terms",
        "Change Control",
        "Intellectual Property and Confidentiality",
        "Risk Management",
        "Sign-Off and Approvals",
        "Termination",
        "Warranties and Liabilities",
    ];

    /// <summary>
    /// Generates a definition of good for each canonical SoW section by:
    /// 1. Analysing each corpus document against the canonical section list.
    /// 2. Synthesising one definition per section from the per-doc analyses.
    /// </summary>
    public async Task<IReadOnlyList<DefinedSection>> BuildDefinitionAsync(
        IReadOnlyList<(string FileName, string Text)> documents,
        Action<string> progress,
        CancellationToken ct = default)
    {
        // Pass 1: analyse each document against the canonical sections
        logger.LogInformation("Analysing {Count} document(s) against {Sections} canonical sections",
            documents.Count, CanonicalSections.Count);
        var summaries = new List<string>(documents.Count);
        for (var i = 0; i < documents.Count; i++)
        {
            var (fileName, text) = documents[i];
            progress($"Analysing document {i + 1} of {documents.Count}: {fileName}");
            logger.LogInformation("Analysing document: {FileName}", fileName);
            summaries.Add(await AnalyseDocumentAsync(fileName, text, ct));
        }

        // Pass 2: synthesise one definition per canonical section
        var sections = new List<DefinedSection>(CanonicalSections.Count);
        for (var i = 0; i < CanonicalSections.Count; i++)
        {
            var sectionName = CanonicalSections[i];
            progress($"Writing definition: {sectionName} ({i + 1} of {CanonicalSections.Count})");
            logger.LogInformation("Synthesising definition for section: {Section}", sectionName);
            var content = await SynthesiseSectionAsync(sectionName, summaries, ct);
            sections.Add(new DefinedSection(sectionName, content));
        }

        return sections;
    }

    private async Task<string> AnalyseDocumentAsync(
        string fileName, string text, CancellationToken ct)
    {
        if (text.Length > MaxDocChars)
            text = text[..MaxDocChars] + "\n[truncated]";

        var sectionList = string.Join(", ", CanonicalSections);

        var prompt = $"""
            You are an expert in Statements of Work (SoW) documents.
            Always write in British English (e.g. "organisation", "recognised", "colour", "centre").

            Analyse the SoW document below and describe what makes it a good SoW across these sections:
            {sectionList}

            For each section, write a short paragraph describing the abstract principles and patterns
            this document demonstrates. Do NOT quote specific text, names, figures, dates, or other
            content — describe only structural and editorial patterns in general terms.
            Use the heading "## <section name>" for each section.

            Document: {fileName}
            ---
            {text}
            """;

        return LlmOutputHelper.StripCodeFence(
            await chatService.CompleteAsync(prompt, AnalysisMaxTokens, ct, think: false));
    }

    private async Task<string> SynthesiseSectionAsync(
        string sectionName, List<string> summaries, CancellationToken ct)
    {
        var joined = string.Join("\n\n---\n\n",
            summaries.Select((s, i) => $"**Document {i + 1} analysis:**\n{s}"));

        var prompt = $$"""
            You are an expert in Statements of Work (SoW) documents.
            Always write in British English (e.g. "organisation", "recognised", "colour", "centre").

            Below are quality analyses of {{summaries.Count}} known-good SoW document(s).
            Focus only on the section: **{{sectionName}}**.

            Write concise, actionable guidance for this section of an authoritative "Definition of Good".
            Describe standards and best practices drawn from the patterns observed across all documents.
            Be specific. Output must be generic, reusable guidance — do not reference specific document
            names, supplier or client names, monetary values, dates, project titles, or any other
            concrete details from the source documents.
            Output only the body text for this section — no heading, no preamble, no other sections.

            ---
            {{joined}}
            """;

        return LlmOutputHelper.StripCodeFence(
            await chatService.CompleteAsync(prompt, SynthesisMaxTokens, ct, think: false));
    }
}
