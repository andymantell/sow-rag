# Evaluation Summary Panel — Design Spec

## Problem

The Results page shows per-section Ragas evaluation badges, but there is no document-level summary that tells the user what the scores mean collectively. Users must mentally aggregate 12 metrics across up to 15 sections to understand whether RAG is helping.

## Solution

A GOV.UK notification banner at the top of the Results page that shows a progressive LLM-generated summary of evaluation scores. The summary updates after each section completes evaluation, is marked as partial until all sections finish, and uses the evaluation model (already loaded for Ragas scoring) to produce a short, grounded analysis. The summary is persisted to the database so it survives page reloads.

## Architecture

### New service: `EvaluationSummaryService`

**File:** `Services/EvaluationSummaryService.cs`

**Responsibility:** Takes completed section data + scores, calls the evaluation LLM, returns a markdown summary.

**Constructor dependencies:** `IConfiguration`, `ILogger<EvaluationSummaryService>`

**Lifetime:** Singleton (matches codebase convention — all services are registered as singletons).

**LLM client:** Creates its own `OpenAI.Chat.ChatClient` from `Evaluation:Endpoint` and `Evaluation:ModelName` config, independent of `FoundryClientFactory` (which manages the app model). The evaluation model is already loaded by the time this service is called (Ragas evaluation unloads the app model via `GpuMemoryManager.PrepareForEvaluationAsync`).

Construction follows the `/v1` endpoint rule from CLAUDE.md:

```csharp
// CLAUDE.md: OpenAIClientOptions.Endpoint must include /v1 — SDK appends chat/completions
var endpoint = new Uri(configuration["Evaluation:Endpoint"]!); // e.g. http://localhost:11434/v1
var options = new OpenAIClientOptions { Endpoint = endpoint };
var client = new OpenAIClient(new ApiKeyCredential("ollama"), options);
_chatClient = client.GetChatClient(configuration["Evaluation:ModelName"]!);
```

**Interface:** `IEvaluationSummaryService` for testability, following the `IChatService`/`IEmbeddingService` pattern:

```csharp
public interface IEvaluationSummaryService
{
    Task<string> GenerateSummaryAsync(
        List<SectionSummaryInput> completedSections,
        int totalSectionCount,
        CancellationToken ct);
}
```

**Public API:**

```csharp
public record SectionSummaryInput
{
    public string Title { get; init; } = "";
    public string OriginalContent { get; init; } = "";
    public string RagImprovedContent { get; init; } = "";
    public int? OriginalQualityScore { get; init; }
    public int? BaselineQualityScore { get; init; }
    public int? RagQualityScore { get; init; }
    public double? BaselineFaithfulnessScore { get; init; }
    public double? RagFaithfulnessScore { get; init; }
    public double? ContextPrecisionScore { get; init; }
    public double? ContextRecallScore { get; init; }
    public double? BaselineFactualCorrectnessScore { get; init; }
    public double? RagFactualCorrectnessScore { get; init; }
    public double? BaselineResponseRelevancyScore { get; init; }
    public double? RagResponseRelevancyScore { get; init; }
    public double? NoiseSensitivityScore { get; init; }
}
```

**Content truncation:** Each section's `OriginalContent` and `RagImprovedContent` are truncated to 2000 characters before being included in the prompt. This prevents exceeding the evaluation model's context window (qwen2.5:14b defaults to 32K tokens on Ollama) when many large sections are present. Truncation happens inside the service (not the caller) with a `" [truncated]"` suffix when applied.

**Prompt design:**

- System context: explains the tool evaluates RAG-based SoW improvements
- Sends all completed sections with original text, RAG-improved text (truncated if needed), and all scores
- If `completedSections.Count < totalSectionCount`, notes this is a partial evaluation
- Asks for:
  1. A 1-2 sentence overall verdict
  2. A bullet list of noteworthy findings, referencing specific sections by name and grounding observations in the actual content differences (not just numbers)
- Instructs the LLM to keep the response under ~150 words
- Uses `$$"""..."""` raw string for the prompt template (project convention for prompts with JSON/braces)

**Returns:** Raw markdown string from the LLM response, cleaned via `LlmOutputHelper.StripCodeFence`.

### Database changes

**File:** `Models/DocumentEntity.cs`

Add a nullable string property to persist the summary:

```csharp
public string? EvaluationSummary { get; set; }
```

This is set after each summary LLM call and loaded on page revisit. No migration needed (PoC uses `EnsureCreated()`; SQLite will add the column automatically on next startup after deleting the DB, or we add it via raw SQL for existing DBs).

### Results page changes

**File:** `Components/Pages/Results.razor`

**New injected service:** `IEvaluationSummaryService`

**New state fields:**

```csharp
private string? _evaluationSummary;
private bool _summaryIsPartial = true;
private bool _summaryLoading;
private int _sectionsEvaluated;
private int _totalToEvaluate;
private CancellationTokenSource? _summaryCts;
```

**Page load path (OnInitializedAsync):**

Load `_evaluationSummary` from the `DocumentEntity.EvaluationSummary` field. If it's non-null, set `_summaryIsPartial = false`. This means revisiting a completed evaluation shows the persisted summary immediately with no LLM call.

**Live evaluation path (RunEvaluationAsync):**

1. Set `_totalToEvaluate = toEvaluate.Count`
2. After each section's scores are persisted and removed from `_evaluatingSections`, increment `_sectionsEvaluated`
3. Trigger summary refresh: if `_summaryLoading` is true (a call is already in flight), set a `_summaryStale` flag. When the in-flight call completes and `_summaryStale` is true, immediately retrigger. This closes the concurrency gap where sections complete while a summary call is in progress.
4. Summary refresh: set `_summaryLoading = true`, collect all sections that have scores, build `SectionSummaryInput` list, call `IEvaluationSummaryService.GenerateSummaryAsync`, store result in `_evaluationSummary`, persist to `DocumentEntity.EvaluationSummary` via `IDbContextFactory`, set `_summaryIsPartial = true`, `_summaryLoading = false`, `StateHasChanged`
5. After the final section completes, one last call with all sections. Set `_summaryIsPartial = false`. Persist final summary.
6. `_summaryCts` is independently tracked from `_evalCts` and cancelled/disposed in `DisposeAsync`

**New parameters passed to `ResultsPanel`:**

```csharp
EvaluationSummary="_evaluationSummary"
SummaryIsPartial="_summaryIsPartial"
SummaryLoading="_summaryLoading"
SectionsEvaluated="_sectionsEvaluated"
TotalEvaluatingSections="_totalToEvaluate"
```

### ResultsPanel UI changes

**File:** `Components/Shared/ResultsPanel.razor`

**New parameters:**

```csharp
[Parameter] public string? EvaluationSummary { get; set; }
[Parameter] public bool SummaryIsPartial { get; set; }
[Parameter] public bool SummaryLoading { get; set; }
[Parameter] public int SectionsEvaluated { get; set; }
[Parameter] public int TotalEvaluatingSections { get; set; }
```

**New banner markup**, inserted above the `diff-header-row`, shown when `EvaluationSummary` is not null or `SummaryLoading` is true:

```html
<div class="govuk-notification-banner @(SummaryIsPartial ? "app-summary-partial" : "")"
     role="region"
     aria-labelledby="eval-summary-title"
     aria-live="polite">
    <div class="govuk-notification-banner__header">
        <h2 class="govuk-notification-banner__title" id="eval-summary-title">
            Evaluation Summary
        </h2>
    </div>
    <div class="govuk-notification-banner__content">
        @if (SummaryIsPartial)
        {
            <p class="govuk-body-s app-summary-status">
                @SectionsEvaluated of @TotalEvaluatingSections sections evaluated
            </p>
        }
        @if (EvaluationSummary is not null)
        {
            @MarkdownRenderer.ToMarkupString(EvaluationSummary)
        }
        @if (SummaryLoading)
        {
            <span class="app-badge-spinner" aria-label="Updating summary"></span>
        }
    </div>
</div>
```

### CSS changes

**File:** `wwwroot/app.css`

- `.app-summary-partial` — reduced opacity (0.85), dashed left border to indicate provisional state
- `.app-summary-status` — small muted text for the "X of Y sections" indicator
- Reuses existing `.app-badge-spinner` for loading state

### Registration

**File:** `Program.cs`

```csharp
builder.Services.AddSingleton<IEvaluationSummaryService, EvaluationSummaryService>();
```

## Testing

### Unit tests for `EvaluationSummaryService`

- **Prompt construction:** Verify the prompt includes section titles, content, and scores. Mock the ChatClient layer.
- **Partial vs complete:** Verify the prompt mentions partial state when `completedSections.Count < totalSectionCount`.
- **Content truncation:** Verify sections with content exceeding 2000 chars are truncated with `" [truncated]"` suffix.
- **Empty input:** Verify graceful handling of an empty `completedSections` list.
- **Response cleaning:** Verify `StripCodeFence` is applied to the LLM response.
- **Error handling:** Verify graceful failure (returns null or empty) if the LLM call fails.

### Rendering tests for summary banner

- Banner not shown when no evaluation data exists
- Banner shown with partial indicator during evaluation
- Banner shown without partial indicator when complete (including page reload with persisted summary)
- Loading spinner shown during LLM call
- Markdown content rendered safely (Markdig `.DisableHtml()`)

## Files to create/modify

| File | Action |
|------|--------|
| `Services/EvaluationSummaryService.cs` | New |
| `Services/IEvaluationSummaryService.cs` | New (interface for testability) |
| `Models/DocumentEntity.cs` | Modify (add `EvaluationSummary` property) |
| `Components/Pages/Results.razor` | Modify |
| `Components/Shared/ResultsPanel.razor` | Modify |
| `wwwroot/app.css` | Modify |
| `Program.cs` | Modify (register service) |
| Test file for `EvaluationSummaryService` | New |
| Test file for summary banner rendering | New |

## Out of scope

- Customising the summary prompt via config
- Showing the summary on the Home page document history
- Separate feature flag for the summary (implicitly gated by `FeatureManagement:Evaluation`)
