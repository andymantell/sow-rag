using OpenAI.Chat;
using SoWImprover.Models;
using System.Text.RegularExpressions;

namespace SoWImprover.Services;

public class SoWImproverService(
    FoundryClientFactory factory,
    SimpleRetriever retriever,
    ILogger<SoWImproverService> logger)
{
    // Truncation limits keep prompts within a model's context window.
    private const int MaxDefinitionChars = 3_000;

    // Token budgets for each LLM call type.
    private const int ImprovementMaxTokens = 2048;
    private const int ExplanationMaxTokens = 300;

    // Corpus chunk snippets are capped at this length in the response.
    private const int SnippetMaxChars = 200;

    /// <summary>
    /// Improves the uploaded SoW <paramref name="originalText"/> section by section,
    /// using the corpus definition and per-section TF-IDF retrieval for grounding.
    /// </summary>
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
                Snippet = c.Text.Length > SnippetMaxChars ? c.Text[..SnippetMaxChars] + "…" : c.Text
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
        // Remove the H1 title line so it doesn't bleed into the improved output
        definition = Regex.Replace(definition, @"^#(?!#)[^\n]*\n+", "", RegexOptions.Multiline).TrimStart();
        if (definition.Length > MaxDefinitionChars)
            definition = definition[..MaxDefinitionChars] + "\n[definition truncated]";

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

            QUALITY STANDARDS (editorial reference — do not include in output):
            {{definition}}

            RELEVANT EXAMPLES FROM KNOWN-GOOD SoWs (editorial reference — do not include in output):
            {{context}}

            SECTION TO REWRITE:
            Title: {{section.Title}}
            Content:
            {{section.Body}}

            Output only the rewritten section. Start with the section heading as a markdown ## heading.
            Do NOT add a document title or any # (H1) heading. No preamble, no explanation.
            """;

        var opts = new ChatCompletionOptions { MaxOutputTokenCount = ImprovementMaxTokens };
        var completion = await client.CompleteChatAsync(
            [new UserChatMessage(prompt)], opts, cancellationToken: ct);

        var text = completion.Value.Content[0].Text.Trim();
        if (text.StartsWith("```"))
        {
            text = Regex.Replace(text, @"^```[a-z]*\n?", "", RegexOptions.Multiline);
            text = text.TrimEnd('`', '\n', ' ');
        }
        // Strip any leading H1 heading the model may have hallucinated
        text = Regex.Replace(text, @"^#(?!#)[^\n]*\n+", "", RegexOptions.Multiline).TrimStart();
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
