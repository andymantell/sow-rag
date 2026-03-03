using OpenAI.Chat;
using SoWImprover.Models;

namespace SoWImprover.Services;

public class DefinitionBuilder(FoundryClientFactory factory, ILogger<DefinitionBuilder> logger)
{
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
        // Truncate to avoid overflowing context window
        const int maxChars = 12_000;
        if (text.Length > maxChars)
            text = text[..maxChars] + "\n[truncated]";

        var prompt = $"""
            You are an expert in Statements of Work (SoW) documents.

            Analyse the SoW document below and describe what makes it a good SoW across these six aspects:
            1. Clarity of deliverables
            2. Milestone and acceptance criteria
            3. Payment terms
            4. IP ownership
            5. Scope boundaries
            6. Risk and change control

            For each aspect write a short paragraph noting what this document does well, with specific examples.
            Use the heading "## Aspect N: <name>" for each section.

            Document: {fileName}
            ---
            {text}
            """;

        var result = await client.CompleteChatAsync([new UserChatMessage(prompt)], cancellationToken: ct);
        return result.Value.Content[0].Text;
    }

    private static async Task<string> SynthesiseAsync(
        ChatClient client, List<string> summaries, CancellationToken ct)
    {
        var joined = string.Join("\n\n---\n\n",
            summaries.Select((s, i) => $"**Document {i + 1} analysis:**\n{s}"));

        var prompt = $"""
            You are an expert in Statements of Work (SoW) documents.

            Below are quality analyses of {summaries.Count} known-good SoW document(s).
            Synthesise these into a single authoritative "Definition of Good" for SoW documents.

            Write a markdown document with exactly these six sections:

            ## 1. Clarity of Deliverables
            ## 2. Milestone and Acceptance Criteria
            ## 3. Payment Terms
            ## 4. IP Ownership
            ## 5. Scope Boundaries
            ## 6. Risk and Change Control

            For each section describe the standards and best practices that characterise a high-quality SoW,
            drawing on the patterns observed across all documents. Be specific and actionable.

            ---
            {joined}
            """;

        var result = await client.CompleteChatAsync([new UserChatMessage(prompt)], cancellationToken: ct);
        return result.Value.Content[0].Text;
    }
}
