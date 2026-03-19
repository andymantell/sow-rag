namespace SoWImprover.Services;

public class EvaluationSummaryService : IEvaluationSummaryService
{
    private const int MaxContentLength = 4000;
    private readonly IChatService _chatService;
    private readonly ILogger<EvaluationSummaryService> _logger;

    public EvaluationSummaryService(IChatService chatService, ILogger<EvaluationSummaryService> logger)
    {
        _chatService = chatService;
        _logger = logger;
    }

    public async Task<string> GenerateSummaryAsync(
        List<SectionSummaryInput> completedSections,
        int totalSectionCount,
        CancellationToken ct)
    {
        var prompt = BuildPrompt(completedSections, totalSectionCount);

        try
        {
            var response = await _chatService.CompleteAsync(prompt, 1024, ct, think: false);
            return LlmOutputHelper.StripCodeFence(response);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Evaluation summary generation failed");
            return "";
        }
    }

    internal static string BuildPrompt(List<SectionSummaryInput> sections, int totalSectionCount)
    {
        var isPartial = sections.Count < totalSectionCount;
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("You are analysing RAG evaluation scores for a Statement of Work improvement tool.");
        sb.AppendLine("The tool takes original SoW sections, improves them using an LLM with RAG (retrieval-augmented generation from a corpus of good SoW examples), and evaluates the results.");
        sb.AppendLine();

        if (isPartial)
        {
            sb.AppendLine($"NOTE: This is a partial evaluation — {sections.Count} of {totalSectionCount} sections have been evaluated so far. More results are coming.");
            sb.AppendLine();
        }

        sb.AppendLine("Below are the evaluated sections with their original content, RAG-improved content, and scores.");
        sb.AppendLine("Score definitions (pay close attention to which direction is GOOD vs BAD for each metric):");
        sb.AppendLine("- Quality (1-5): rubric score against the definition of good SoW content. Higher is better. 5 = excellent, 1 = poor.");
        sb.AppendLine("- Faithfulness (0-1): did the output stay true to the source content without hallucinating? Higher is better. 1.0 = perfectly faithful, 0.0 = heavily hallucinated.");
        sb.AppendLine("- Factual correctness (0-1 F1): precision and recall of factual claims vs original. Higher is better. 1.0 = all facts preserved accurately.");
        sb.AppendLine("- Response relevancy (0-1): did the output stay on-task and address the section's purpose? Higher is better. 1.0 = fully relevant.");
        sb.AppendLine("- Context precision (0-1): were the retrieved RAG chunks relevant to this section? Higher is better. Low = retriever returned irrelevant material.");
        sb.AppendLine("- Context recall (0-1): did retrieval find all the relevant material from the corpus? Higher is better. Low = retriever missed useful guidance.");
        sb.AppendLine("- Noise sensitivity (0-1): **LOWER is better. 0.0 = ideal (unaffected by noise), 1.0 = worst (irrelevant retrieved chunks significantly harmed the output).** A score of 1.0 means the output was badly degraded by noisy context.");
        sb.AppendLine();

        foreach (var sec in sections)
        {
            sb.AppendLine($"### {sec.Title}");
            sb.AppendLine();
            sb.AppendLine("**Original content:**");
            sb.AppendLine(Truncate(sec.OriginalContent));
            sb.AppendLine();
            sb.AppendLine("**RAG-improved content:**");
            sb.AppendLine(Truncate(sec.RagImprovedContent));
            sb.AppendLine();
            sb.AppendLine("**Scores:**");
            sb.AppendLine($"- Original quality: {FormatScore(sec.OriginalQualityScore)}");
            sb.AppendLine($"- Baseline quality (no RAG): {FormatScore(sec.BaselineQualityScore)}");
            sb.AppendLine($"- RAG quality: {FormatScore(sec.RagQualityScore)}");
            sb.AppendLine($"- Baseline faithfulness: {FormatScore(sec.BaselineFaithfulnessScore)}");
            sb.AppendLine($"- RAG faithfulness: {FormatScore(sec.RagFaithfulnessScore)}");
            sb.AppendLine($"- Baseline factual correctness: {FormatScore(sec.BaselineFactualCorrectnessScore)}");
            sb.AppendLine($"- RAG factual correctness: {FormatScore(sec.RagFactualCorrectnessScore)}");
            sb.AppendLine($"- Baseline response relevancy: {FormatScore(sec.BaselineResponseRelevancyScore)}");
            sb.AppendLine($"- RAG response relevancy: {FormatScore(sec.RagResponseRelevancyScore)}");
            sb.AppendLine($"- Context precision: {FormatScore(sec.ContextPrecisionScore)}");
            sb.AppendLine($"- Context recall: {FormatScore(sec.ContextRecallScore)}");
            sb.AppendLine($"- Noise sensitivity: {FormatScore(sec.NoiseSensitivityScore)}");
            sb.AppendLine();
        }

        sb.AppendLine("Provide a short summary (under 150 words) with:");
        sb.AppendLine("1. A 1-2 sentence overall verdict on whether RAG is improving the SoW sections");
        sb.AppendLine("2. A bullet list of noteworthy findings — reference specific sections by name and explain WHY scores are the way they are by comparing the original and improved content");
        sb.AppendLine();
        sb.AppendLine("Focus on what is surprising, concerning, or encouraging. Do not explain what each metric means — the user already knows.");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT RULES:");
        sb.AppendLine("- Only cite score values that EXACTLY match the numbers shown above. Do not round, infer, or fabricate scores.");
        sb.AppendLine("- Remember: for noise sensitivity, 0.00 is GOOD (no noise impact) and 1.00 is BAD (severe noise impact). Do not describe high noise sensitivity as positive.");
        sb.AppendLine("- A score of 'N/A' means it was not computed — do not discuss it.");

        return sb.ToString();
    }

    private static string Truncate(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (text.Length <= MaxContentLength) return text;
        return text[..MaxContentLength] + " [truncated]";
    }

    private static string FormatScore(int? score) => score.HasValue ? score.Value.ToString() : "N/A";
    private static string FormatScore(double? score) => score.HasValue ? score.Value.ToString("F2") : "N/A";
}
