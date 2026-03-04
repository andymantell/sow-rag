using OpenAI.Chat;
using SoWImprover.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SoWImprover.Services;

public class SoWImproverService(
    FoundryClientFactory factory,
    SimpleRetriever retriever,
    ILogger<SoWImproverService> logger)
{
    public async Task<ImprovementResult> ImproveAsync(
        string originalText, GoodDefinition definition, CancellationToken ct = default)
    {
        var client = await factory.GetChatClientAsync(ct);
        var sections = SplitIntoSections(originalText);

        var improvedParts = new List<string>(sections.Count);
        var originalParts = new List<string>(sections.Count);
        var flagged = new List<FlaggedSection>();
        var allChunks = new List<DocumentChunk>();

        foreach (var section in sections)
        {
            logger.LogInformation("Improving section: {Title}", section.Title);
            var chunks = retriever.Retrieve(section.Body);
            allChunks.AddRange(chunks);

            var result = await ImproveSectionAsync(client, section, chunks, definition.MarkdownContent, ct);
            improvedParts.Add(result.ImprovedText);
            // Reformat original using the detected section headings so both sides are valid markdown
            originalParts.Add($"## {section.Title}\n\n{section.Body}");

            if (result.Flagged)
                flagged.Add(new FlaggedSection { SectionTitle = section.Title, Reason = result.FlagReason });
        }

        var chunksUsed = allChunks
            .GroupBy(c => $"{c.SourceFile}|{c.ChunkIndex}")
            .Select(g => g.First())
            .Select(c => new ChunkReference
            {
                SourceFile = c.SourceFile,
                Snippet = c.Text.Length > 200 ? c.Text[..200] + "…" : c.Text
            })
            .ToList();

        return new ImprovementResult
        {
            Original = string.Join("\n\n", originalParts),
            Improved = string.Join("\n\n", improvedParts),
            FlaggedSections = flagged,
            ChunksUsed = chunksUsed
        };
    }

    private static async Task<SectionResult> ImproveSectionAsync(
        ChatClient client,
        DocumentSection section,
        List<DocumentChunk> chunks,
        string definition,
        CancellationToken ct)
    {
        // Cap the definition to avoid consuming most of the context window
        const int maxDefChars = 3000;
        if (definition.Length > maxDefChars)
            definition = definition[..maxDefChars] + "\n[definition truncated]";

        var context = chunks.Count > 0
            ? string.Join("\n\n", chunks.Select(c => $"[{c.SourceFile}]: {c.Text}"))
            : "No relevant examples found.";

        var prompt = $$"""
            You are an expert in Statements of Work (SoW) documents.

            DEFINITION OF GOOD:
            {{definition}}

            RELEVANT EXAMPLES FROM KNOWN-GOOD SoWs:
            {{context}}

            SECTION TO IMPROVE:
            Title: {{section.Title}}
            Content:
            {{section.Body}}

            Instructions:
            - Improve the content to better align with the Definition of Good.
            - Preserve the section's purpose and topic.
            - Start your improved text with the section heading as a markdown ## heading.
            - If the section's STRUCTURE fundamentally needs to change (missing key sub-sections,
              wrong overall approach), set "flagged" to true with a brief reason.
            - If only content needs improving, set "flagged" to false.

            Respond with ONLY a JSON object — no markdown fences, no extra text:
            {"improved": "## {{section.Title}}\n\nImproved content here...", "flagged": false, "flagReason": ""}
            """;

        var opts = new ChatCompletionOptions { MaxOutputTokenCount = 2048 };
        var completion = await client.CompleteChatAsync(
            [new UserChatMessage(prompt)], opts, cancellationToken: ct);

        return ParseSectionResponse(completion.Value.Content[0].Text, section);
    }

    private static SectionResult ParseSectionResponse(string responseText, DocumentSection section)
    {
        var text = responseText.Trim();

        // Strip markdown code fences if the model added them despite instructions
        if (text.StartsWith("```"))
        {
            text = Regex.Replace(text, @"^```[a-z]*\n?", "", RegexOptions.Multiline);
            text = text.TrimEnd('`', '\n', ' ');
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            return new SectionResult(
                root.GetProperty("improved").GetString() ?? $"## {section.Title}\n\n{section.Body}",
                root.GetProperty("flagged").GetBoolean(),
                root.GetProperty("flagReason").GetString() ?? "");
        }
        catch
        {
            // Fallback: use raw response as improved text
            return new SectionResult($"## {section.Title}\n\n{text}", false, "");
        }
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

        // If nothing was detected, treat the whole document as one section
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
}

internal record DocumentSection(string Title, string Body);
internal record SectionResult(string ImprovedText, bool Flagged, string FlagReason);
