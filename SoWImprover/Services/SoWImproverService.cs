using System.Text.Json;
using System.Text.RegularExpressions;
using SoWImprover.Models;

namespace SoWImprover.Services;

public class SoWImproverService(
    IChatService chatService,
    ILogger<SoWImproverService> logger)
{
    private const int MaxDefinitionChars = 2_000;
    private const int ImprovementMaxTokens = 2048;
    private const int ExplanationMaxTokens = 300;
    private const int MatchingMaxTokens = 600;
    private const int SnippetMaxChars = 200;

    public async Task<ImprovementResult> ImproveAsync(
        string originalText, GoodDefinition definition,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var sections = SplitIntoSections(originalText);

        // One LLM call to classify all uploaded section titles against the canonical set.
        progress?.Report("Matching sections…");
        var canonicalNames = definition.Sections.Select(s => s.Name).ToList();
        var uploadedTitles = sections.Select(s => s.Title).ToList();
        var matching = await MatchSectionsAsync(uploadedTitles, canonicalNames, ct);

        var sectionResults = new List<SectionResult>(sections.Count);
        var allChunks = new List<DocumentChunk>();
        var improvedCount = 0;
        var totalToImprove = matching.Values.Count(m => m is not null);

        foreach (var section in sections)
        {
            matching.TryGetValue(section.Title, out var matchedName);
            var definedSection = matchedName is not null
                ? definition.Sections.FirstOrDefault(
                      s => string.Equals(s.Name, matchedName, StringComparison.OrdinalIgnoreCase))
                : null;

            if (definedSection is null)
            {
                logger.LogInformation("Section not recognised, skipping: {Title}", section.Title);
                sectionResults.Add(new SectionResult
                {
                    OriginalTitle = section.Title,
                    OriginalContent = section.Body,
                    Unrecognised = true
                });
                continue;
            }

            improvedCount++;
            progress?.Report($"Improving: {section.Title} ({improvedCount} of {totalToImprove})");
            logger.LogInformation("Improving section '{Title}' → '{Canonical}'", section.Title, matchedName);

            // Baseline: same prompt but no RAG context
            var baseline = await ImproveSectionAsync(section, [], definedSection.Content, ct);

            // RAG-enhanced: retrieve relevant chunks and improve
            var chunks = await definition.Retriever!.RetrieveAsync(section.Body, ct);
            allChunks.AddRange(chunks);
            var improved = await ImproveSectionAsync(section, chunks, definedSection.Content, ct);
            var explanation = await ExplainChangesAsync(section, improved, ct);

            sectionResults.Add(new SectionResult
            {
                OriginalTitle = section.Title,
                OriginalContent = section.Body,
                BaselineContent = baseline,
                ImprovedContent = improved,
                MatchedSection = matchedName,
                Explanation = explanation,
                RetrievedContexts = chunks.Select(c => c.Text).ToList(),
                DefinitionOfGoodText = definedSection.Content
            });
        }

        var chunksUsed = allChunks
            .GroupBy(c => $"{c.SourceFile}|{c.ChunkIndex}")
            .Select(g => g.First())
            .Select(c => new ChunkReference
            {
                SourceFile = c.SourceFile,
                Snippet = c.Text.Length > SnippetMaxChars ? c.Text[..SnippetMaxChars] + "…" : c.Text
            })
            .ToList();

        return new ImprovementResult
        {
            Sections = sectionResults,
            ChunksUsed = chunksUsed
        };
    }

    // ── LLM calls ─────────────────────────────────────────────────────────────

    private async Task<Dictionary<string, string?>> MatchSectionsAsync(
        IList<string> uploadedTitles,
        IList<string> canonicalNames,
        CancellationToken ct)
    {
        var titlesJson   = JsonSerializer.Serialize(uploadedTitles);
        var canonicalJson = JsonSerializer.Serialize(canonicalNames);

        var prompt = $$"""
            You are classifying sections from an uploaded Statement of Work (SoW) document.
            Respond in British English.

            UPLOADED SECTION TITLES:
            {{titlesJson}}

            CANONICAL SECTION NAMES (the only valid mapping targets):
            {{canonicalJson}}

            For each uploaded title, identify the single best-matching canonical name.
            Use semantic meaning — the wording may differ but the intent must align.
            If no canonical section is a reasonable semantic match, use null.

            Respond with a JSON object mapping each uploaded title (exactly as given) to either
            the matched canonical name (exactly as given) or null. No explanation, no markdown.
            Example: {"Title A": "Canonical X", "Title B": null}
            """;

        var raw = await chatService.CompleteAsync(prompt, MatchingMaxTokens, ct);
        var json = LlmOutputHelper.StripCodeFence(raw.Trim());

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
                      ?? [];
            var result = new Dictionary<string, string?>(parsed.Count);
            foreach (var (k, v) in parsed)
                result[k] = v.ValueKind == JsonValueKind.String ? v.GetString() : null;

            foreach (var title in uploadedTitles)
            {
                result.TryGetValue(title, out var matched);
                logger.LogInformation("Section match: '{Title}' → '{Match}'",
                    title, matched ?? "none");
            }
            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse section matching JSON; treating all as unmatched");
            return uploadedTitles.ToDictionary(t => t, _ => (string?)null);
        }
    }

    private async Task<string> ImproveSectionAsync(
        DocumentSection section,
        List<DocumentChunk> chunks,
        string sectionDefinition,
        CancellationToken ct)
    {
        if (sectionDefinition.Length > MaxDefinitionChars)
            sectionDefinition = sectionDefinition[..MaxDefinitionChars] + "\n[truncated]";

        var context = chunks.Count > 0
            ? string.Join("\n\n", chunks.Select(c => $"[{c.SourceFile}]: {c.Text}"))
            : "No relevant examples found.";

        var prompt = $$"""
            You are an expert editor improving a Statement of Work (SoW) document.
            Always write in British English (e.g. "organisation", "recognised", "colour", "centre").

            Your task is to improve the section below so that it reads as polished, professional SoW content.
            "Improve" means: better structure, clearer language, more precise wording, and proper
            SoW conventions. It does NOT mean adding new facts, figures, dates, obligations, or
            requirements that are not already present in the original.

            CRITICAL RULES:
            - Every factual claim in your output must come from the SECTION TO REWRITE below.
            - Do NOT invent specific dates, monetary values, parties, metrics, or obligations.
            - If the original is vague, keep it vague — improve the wording, not the specificity.
            - Use the QUALITY STANDARDS and EXAMPLES for style and structure guidance only.
              Do not copy facts or details from them into the output.
            - The output must be the rewritten section itself — actual contract/SoW language,
              not commentary, not a description of improvements.
            - Do not describe what the section should contain. Write the content directly.

            QUALITY STANDARDS for "{{section.Title}}" (editorial reference — do not include in output):
            {{sectionDefinition}}

            RELEVANT EXAMPLES FROM KNOWN-GOOD SoWs (style reference only — do not copy content from these):
            {{context}}

            SECTION TO REWRITE:
            Title: {{section.Title}}
            Content:
            {{section.Body}}

            Output only the rewritten section body. Do NOT include the section heading.
            Do NOT add a document title or any heading at all. No preamble, no explanation.
            """;

        var improved = LlmOutputHelper.StripCodeFence(
            (await chatService.CompleteAsync(prompt, ImprovementMaxTokens, ct)).Trim());
        // Strip a leading heading the model may have hallucinated — \A anchors to string start only
        improved = Regex.Replace(improved, @"\A#{1,2}[^\n]*\n+", "").TrimStart();
        return improved;
    }

    private async Task<string> ExplainChangesAsync(
        DocumentSection section,
        string improvedText,
        CancellationToken ct)
    {
        var prompt = $"""
            Original section "{section.Title}":
            {section.Body}

            Improved section:
            {improvedText}

            List the key improvements made, as 2-4 concise bullet points starting with a dash.
            Be specific. No preamble. Write in British English.
            """;

        return (await chatService.CompleteAsync(prompt, ExplanationMaxTokens, ct)).Trim();
    }

    internal static string StripPdfArtifacts(string text)
        => Regex.Replace(text, @"(?m)^Docusign Envelope ID:.*$\n?", "");

    internal static List<DocumentSection> SplitIntoSections(string text)
    {
        text = StripPdfArtifacts(text);
        var lines = text.Split('\n');
        var sections = new List<DocumentSection>();
        var currentTitle = "Introduction";
        var currentBody = new List<string>();

        foreach (var line in lines)
        {
            if (IsHeading(line))
            {
                if (currentBody.Any(l => !string.IsNullOrWhiteSpace(l)))
                    sections.Add(new DocumentSection(currentTitle, string.Join("\n", currentBody).Trim()));

                currentTitle = Regex.Replace(line.TrimStart('#').Trim(), @"\*{1,2}|_{1,2}", "").Trim();
                // Strip leading number prefix: "2 Buyer Requirements" → "Buyer Requirements"
                currentTitle = Regex.Replace(currentTitle, @"^\d+[\.\):]?\s+", "").Trim();
                currentBody.Clear();
            }
            else
            {
                currentBody.Add(line);
            }
        }

        if (currentBody.Any(l => !string.IsNullOrWhiteSpace(l)))
            sections.Add(new DocumentSection(currentTitle, string.Join("\n", currentBody).Trim()));

        if (sections.Count == 0 && !string.IsNullOrWhiteSpace(text))
            sections.Add(new DocumentSection("Introduction", text.Trim()));

        return sections;
    }

    internal static bool IsHeading(string line)
    {
        if (line.StartsWith('#') && line.TrimStart('#').StartsWith(' ')) return true;
        var t = line.Trim();
        if (t.Length < 3 || !t.Any(char.IsLetter)) return false;

        // Bold-formatted heading: entire line is **text** or number + **text**
        // e.g. "**Annex 1 (Template Statement of Work)**" or "2 **Buyer Requirements**"
        // Exclude bold field labels like "**Date:** value" (colon followed by non-bold text)
        if (IsBoldHeading(t)) return true;

        if (t != t.ToUpperInvariant()) return false;
        // Avoid misclassifying table cells, list items, or short abbreviations (e.g. "UK", "SLA")
        if (t.StartsWith('|') || t.StartsWith('-') || t.StartsWith('*')) return false;
        return t.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2;
    }

    private static bool IsBoldHeading(string t)
    {
        // Strip leading number prefix: "2 **Heading**" → "**Heading**"
        var s = Regex.Replace(t, @"^\d+[\.\):]?\s+", "");
        if (!s.StartsWith("**") || !s.EndsWith("**")) return false;
        // Exclude field labels: "**Label:** value" — bold text ending with :** followed by content
        // A heading may end with ":**" (e.g. "**Risks:**") but won't have non-bold text after
        var inner = s[2..^2];
        if (inner.Contains("**")) return false; // nested bold markers = not a simple heading
        return true;
    }
}

internal record DocumentSection(string Title, string Body);
