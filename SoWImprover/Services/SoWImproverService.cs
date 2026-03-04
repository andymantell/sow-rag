using OpenAI.Chat;
using SoWImprover.Models;
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
        var annotations = new List<SectionAnnotation>();
        var allChunks = new List<DocumentChunk>();

        foreach (var section in sections)
        {
            logger.LogInformation("Improving section: {Title}", section.Title);
            var chunks = retriever.Retrieve(section.Body);
            allChunks.AddRange(chunks);

            var improved = await ImproveSectionAsync(client, section, chunks, definition.MarkdownContent, ct);
            improvedParts.Add(improved);
            originalParts.Add($"## {section.Title}\n\n{section.Body}");

            var explanation = await ExplainChangesAsync(client, section, improved, ct);
            if (!string.IsNullOrWhiteSpace(explanation))
                annotations.Add(new SectionAnnotation { SectionTitle = section.Title, Explanation = explanation });
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
            Annotations = annotations,
            ChunksUsed = chunksUsed
        };
    }

    private static async Task<string> ImproveSectionAsync(
        ChatClient client,
        DocumentSection section,
        List<DocumentChunk> chunks,
        string definition,
        CancellationToken ct)
    {
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

            Write the improved section. Start with the section heading as a markdown ## heading.
            No preamble, no explanation — just the improved section text.
            """;

        var opts = new ChatCompletionOptions { MaxOutputTokenCount = 2048 };
        var completion = await client.CompleteChatAsync(
            [new UserChatMessage(prompt)], opts, cancellationToken: ct);

        var text = completion.Value.Content[0].Text.Trim();
        if (text.StartsWith("```"))
        {
            text = Regex.Replace(text, @"^```[a-z]*\n?", "", RegexOptions.Multiline);
            text = text.TrimEnd('`', '\n', ' ');
        }
        return text;
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

        var opts = new ChatCompletionOptions { MaxOutputTokenCount = 300 };
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
