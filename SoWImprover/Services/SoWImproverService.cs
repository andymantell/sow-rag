using System.Text.Json;
using System.Text.RegularExpressions;
using OpenAI.Chat;
using SoWImprover.Models;

namespace SoWImprover.Services;

public class SoWImproverService(
    FoundryClientFactory factory,
    SimpleRetriever retriever,
    ILogger<SoWImproverService> logger)
{
    private const int MaxDefinitionChars = 2_000;
    private const int MatchingMaxTokens = 512;
    private const int ImprovementMaxTokens = 2048;
    private const int ExplanationMaxTokens = 300;
    private const int SnippetMaxChars = 200;

    public async Task<ImprovementResult> ImproveAsync(
        string originalText, GoodDefinition definition,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var client = await factory.GetChatClientAsync(ct);
        var sections = SplitIntoSections(originalText);

        // One bulk call to map uploaded section titles → canonical section names
        progress?.Report("Matching sections…");
        var uploadedTitles = sections.Select(s => s.Title).ToList();
        var canonicalNames = definition.Sections.Select(s => s.Name).ToList();
        var matching = await MatchSectionsAsync(client, uploadedTitles, canonicalNames, ct);

        var sectionResults = new List<SectionResult>(sections.Count);
        var allChunks = new List<DocumentChunk>();
        var improvedCount = 0;
        var totalToImprove = uploadedTitles.Count(t =>
        {
            matching.TryGetValue(t, out var m);
            return m is not null;
        });

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
            var chunks = retriever.Retrieve(section.Body);
            allChunks.AddRange(chunks);

            var improved = await ImproveSectionAsync(client, section, chunks, definedSection.Content, ct);
            var explanation = await ExplainChangesAsync(client, section, improved, ct);

            sectionResults.Add(new SectionResult
            {
                OriginalTitle = section.Title,
                OriginalContent = section.Body,
                ImprovedContent = improved,
                MatchedSection = matchedName,
                Explanation = explanation
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

    private static async Task<Dictionary<string, string?>> MatchSectionsAsync(
        ChatClient client,
        List<string> uploadedTitles,
        List<string> canonicalNames,
        CancellationToken ct)
    {
        var uploaded = JsonSerializer.Serialize(uploadedTitles);
        var canonical = JsonSerializer.Serialize(canonicalNames);

        var prompt = $$"""
            Match each uploaded SoW section title to the most appropriate canonical section name from the list below.
            Use null when there is no reasonable match.
            Output only a JSON object mapping each uploaded title to its canonical match or null.
            Example: {"Introduction": null, "Payment Schedule": "Payment Terms"}

            Canonical sections: {{canonical}}
            Uploaded sections: {{uploaded}}
            """;

        var opts = new ChatCompletionOptions { MaxOutputTokenCount = MatchingMaxTokens };
        var result = await client.CompleteChatAsync([new UserChatMessage(prompt)], opts, cancellationToken: ct);
        return ParseMatchingJson(result.Value.Content[0].Text, uploadedTitles);
    }

    private static Dictionary<string, string?> ParseMatchingJson(string text, List<string> uploadedTitles)
    {
        text = StripCodeFences(text);
        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(text) ?? [];
            return raw.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ValueKind == JsonValueKind.Null ? null : kvp.Value.GetString());
        }
        catch
        {
            // If parsing fails, treat all sections as unrecognised
            return uploadedTitles.ToDictionary(t => t, _ => (string?)null);
        }
    }

    private static async Task<string> ImproveSectionAsync(
        ChatClient client,
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
            You are an expert editor rewriting a Statement of Work (SoW) document.

            Your task is to rewrite the section below so that it reads as polished, professional SoW content.
            The output must be the rewritten section itself — actual contract/SoW language, not commentary,
            not a description of improvements, not guidance on how a good SoW should be written.
            Do not describe what the section should contain. Write the content directly.

            Use the QUALITY STANDARDS below as editorial guidance only. Do not reproduce them in the output.

            QUALITY STANDARDS for "{{section.Title}}" (editorial reference — do not include in output):
            {{sectionDefinition}}

            RELEVANT EXAMPLES FROM KNOWN-GOOD SoWs (editorial reference — do not include in output):
            {{context}}

            SECTION TO REWRITE:
            Title: {{section.Title}}
            Content:
            {{section.Body}}

            Output only the rewritten section body. Do NOT include the section heading.
            Do NOT add a document title or any heading at all. No preamble, no explanation.
            """;

        var opts = new ChatCompletionOptions { MaxOutputTokenCount = ImprovementMaxTokens };
        var completion = await client.CompleteChatAsync(
            [new UserChatMessage(prompt)], opts, cancellationToken: ct);

        var improved = completion.Value.Content[0].Text.Trim();
        if (improved.StartsWith("```"))
        {
            improved = Regex.Replace(improved, @"^```[a-z]*\n?", "", RegexOptions.Multiline);
            improved = improved.TrimEnd('`', '\n', ' ');
        }
        // Strip any heading the model may have hallucinated
        improved = Regex.Replace(improved, @"^#{1,2}[^\n]*\n+", "", RegexOptions.Multiline).TrimStart();
        return improved;
    }

    private static async Task<string> ExplainChangesAsync(
        ChatClient client,
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
            Be specific. No preamble.
            """;

        var opts = new ChatCompletionOptions { MaxOutputTokenCount = ExplanationMaxTokens };
        var completion = await client.CompleteChatAsync(
            [new UserChatMessage(prompt)], opts, cancellationToken: ct);

        return completion.Value.Content[0].Text.Trim();
    }

    internal static List<DocumentSection> SplitIntoSections(string text)
    {
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

                currentTitle = line.TrimStart('#').Trim();
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

    private static bool IsHeading(string line)
    {
        if (line.StartsWith('#')) return true;
        var t = line.Trim();
        return t.Length > 2 && t.Any(char.IsLetter) && t == t.ToUpperInvariant();
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

internal record DocumentSection(string Title, string Body);
