# Evaluation Summary Panel Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a progressive LLM-generated evaluation summary banner at the top of the Results page that aggregates Ragas scores, explains noteworthy findings, and persists to the database.

**Architecture:** New `EvaluationSummaryService` creates its own `ChatClient` from `Evaluation:*` config to call the evaluation LLM. `Results.razor` triggers summary generation after each section completes Ragas evaluation, passing section content and scores. The summary is persisted on `DocumentEntity.EvaluationSummary` and displayed in a GOV.UK notification banner in `ResultsPanel.razor`.

**Tech Stack:** ASP.NET Core 8 / Blazor Server, OpenAI .NET SDK, xUnit + NSubstitute + bUnit, SQLite via EF Core

**Spec:** `docs/superpowers/specs/2026-03-16-evaluation-summary-panel-design.md`

---

## File Structure

| File | Action | Responsibility |
|------|--------|----------------|
| `Services/IEvaluationSummaryService.cs` | Create | Interface for testability |
| `Services/EvaluationSummaryService.cs` | Create | Builds prompt, calls evaluation LLM, returns markdown summary |
| `Models/DocumentEntity.cs` | Modify | Add `EvaluationSummary` property |
| `Program.cs` | Modify | Register singleton |
| `Components/Shared/ResultsPanel.razor` | Modify | Add parameters + render summary banner |
| `Components/Pages/Results.razor` | Modify | Orchestrate summary generation + persistence |
| `wwwroot/app.css` | Modify | Partial/loading styles |
| `SoWImprover.Tests/Services/EvaluationSummaryServiceTests.cs` | Create | Unit tests for prompt building |
| `SoWImprover.Tests/Components/ResultsPanelSummaryTests.cs` | Create | bUnit rendering tests for banner |

---

## Chunk 1: EvaluationSummaryService + Tests

### Task 1: Interface and model

**Files:**
- Create: `Services/IEvaluationSummaryService.cs`

- [ ] **Step 1: Create the interface and input record**

```csharp
namespace SoWImprover.Services;

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

public interface IEvaluationSummaryService
{
    Task<string> GenerateSummaryAsync(
        List<SectionSummaryInput> completedSections,
        int totalSectionCount,
        CancellationToken ct);
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build SoWImprover/SoWImprover.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```
git add Services/IEvaluationSummaryService.cs
git commit -m "feat: add IEvaluationSummaryService interface and SectionSummaryInput record"
```

---

### Task 2: Write failing tests for EvaluationSummaryService

**Files:**
- Create: `SoWImprover.Tests/Services/EvaluationSummaryServiceTests.cs`

The service will have two testable concerns: prompt construction (via an `internal static` method) and content truncation. The actual LLM call will go through `ChatClient` which we can't easily mock (sealed class), so we test the prompt-building and truncation logic as static/internal methods.

- [ ] **Step 1: Write tests for prompt building**

```csharp
using SoWImprover.Services;

namespace SoWImprover.Tests.Services;

public class EvaluationSummaryServiceTests
{
    [Fact]
    public void BuildPrompt_IncludesSectionTitlesAndScores()
    {
        var sections = new List<SectionSummaryInput>
        {
            new()
            {
                Title = "Scope of Work",
                OriginalContent = "Original scope text",
                RagImprovedContent = "Improved scope text",
                OriginalQualityScore = 2,
                RagQualityScore = 4,
                BaselineFaithfulnessScore = 0.8,
                RagFaithfulnessScore = 0.9,
                ContextPrecisionScore = 0.7
            }
        };

        var prompt = EvaluationSummaryService.BuildPrompt(sections, totalSectionCount: 3);

        Assert.Contains("Scope of Work", prompt);
        Assert.Contains("Original scope text", prompt);
        Assert.Contains("Improved scope text", prompt);
        Assert.Contains("2", prompt); // original quality
        Assert.Contains("4", prompt); // rag quality
        Assert.Contains("0.7", prompt); // context precision
    }

    [Fact]
    public void BuildPrompt_IndicatesPartialWhenNotAllSectionsComplete()
    {
        var sections = new List<SectionSummaryInput>
        {
            new() { Title = "Introduction", OriginalContent = "x", RagImprovedContent = "y", RagQualityScore = 3 }
        };

        var prompt = EvaluationSummaryService.BuildPrompt(sections, totalSectionCount: 5);

        Assert.Contains("1 of 5", prompt);
    }

    [Fact]
    public void BuildPrompt_DoesNotIndicatePartialWhenAllSectionsComplete()
    {
        var sections = new List<SectionSummaryInput>
        {
            new() { Title = "Introduction", OriginalContent = "x", RagImprovedContent = "y", RagQualityScore = 3 }
        };

        var prompt = EvaluationSummaryService.BuildPrompt(sections, totalSectionCount: 1);

        Assert.DoesNotContain("of 1", prompt);
    }

    [Fact]
    public void BuildPrompt_TruncatesLongContent()
    {
        var longText = new string('A', 3000);
        var sections = new List<SectionSummaryInput>
        {
            new() { Title = "Long Section", OriginalContent = longText, RagImprovedContent = longText, RagQualityScore = 3 }
        };

        var prompt = EvaluationSummaryService.BuildPrompt(sections, totalSectionCount: 1);

        Assert.DoesNotContain(longText, prompt);
        Assert.Contains("[truncated]", prompt);
        Assert.Contains(new string('A', 2000), prompt);
    }

    [Fact]
    public void BuildPrompt_DoesNotTruncateShortContent()
    {
        var shortText = new string('B', 500);
        var sections = new List<SectionSummaryInput>
        {
            new() { Title = "Short Section", OriginalContent = shortText, RagImprovedContent = shortText, RagQualityScore = 3 }
        };

        var prompt = EvaluationSummaryService.BuildPrompt(sections, totalSectionCount: 1);

        Assert.Contains(shortText, prompt);
        Assert.DoesNotContain("[truncated]", prompt);
    }

    [Fact]
    public void BuildPrompt_HandlesEmptySectionList()
    {
        var sections = new List<SectionSummaryInput>();

        var prompt = EvaluationSummaryService.BuildPrompt(sections, totalSectionCount: 5);

        Assert.Contains("0 of 5", prompt);
    }

    [Fact]
    public void BuildPrompt_IncludesNullScoresAsNotAvailable()
    {
        var sections = new List<SectionSummaryInput>
        {
            new()
            {
                Title = "Delivery",
                OriginalContent = "text",
                RagImprovedContent = "improved",
                RagQualityScore = 4,
                ContextPrecisionScore = null,
                NoiseSensitivityScore = null
            }
        };

        var prompt = EvaluationSummaryService.BuildPrompt(sections, totalSectionCount: 1);

        Assert.Contains("Delivery", prompt);
        Assert.Contains("N/A", prompt);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SoWImprover.Tests --filter "FullyQualifiedName~EvaluationSummaryServiceTests" --no-restore -v minimal`
Expected: FAIL — `EvaluationSummaryService.BuildPrompt` does not exist yet

- [ ] **Step 3: Commit failing tests**

```
git add SoWImprover.Tests/Services/EvaluationSummaryServiceTests.cs
git commit -m "test: add failing tests for EvaluationSummaryService prompt building"
```

---

### Task 3: Implement EvaluationSummaryService

**Files:**
- Create: `Services/EvaluationSummaryService.cs`
- Modify: `Program.cs` (add registration)

- [ ] **Step 1: Implement the service**

```csharp
using System.ClientModel;
using OpenAI;
using OpenAI.Chat;

namespace SoWImprover.Services;

public class EvaluationSummaryService : IEvaluationSummaryService
{
    private const int MaxContentLength = 2000;
    private readonly ChatClient _chatClient;
    private readonly ILogger<EvaluationSummaryService> _logger;

    public EvaluationSummaryService(IConfiguration configuration, ILogger<EvaluationSummaryService> logger)
    {
        _logger = logger;

        var endpoint = configuration["Evaluation:Endpoint"]
            ?? throw new InvalidOperationException("Evaluation:Endpoint not configured.");
        var modelName = configuration["Evaluation:ModelName"]
            ?? throw new InvalidOperationException("Evaluation:ModelName not configured.");

        // CLAUDE.md: OpenAIClientOptions.Endpoint must include /v1 — SDK appends chat/completions
        var options = new OpenAIClientOptions { Endpoint = new Uri(endpoint) };
        var client = new OpenAIClient(new ApiKeyCredential("ollama"), options);
        _chatClient = client.GetChatClient(modelName);
    }

    // Internal constructor for unit testing (inject a mock-friendly delegate instead of ChatClient)
    internal EvaluationSummaryService(
        Func<string, int, CancellationToken, Task<string>> completeFunc,
        ILogger<EvaluationSummaryService> logger)
    {
        _logger = logger;
        _completeFunc = completeFunc;
        _chatClient = null!; // not used when _completeFunc is set
    }

    private readonly Func<string, int, CancellationToken, Task<string>>? _completeFunc;

    public async Task<string> GenerateSummaryAsync(
        List<SectionSummaryInput> completedSections,
        int totalSectionCount,
        CancellationToken ct)
    {
        var prompt = BuildPrompt(completedSections, totalSectionCount);

        try
        {
            string response;
            if (_completeFunc is not null)
            {
                response = await _completeFunc(prompt, 1024, ct);
            }
            else
            {
                var opts = new ChatCompletionOptions { MaxOutputTokenCount = 1024 };
                var completion = await _chatClient.CompleteChatAsync(
                    [new UserChatMessage(prompt)], opts, cancellationToken: ct);
                var content = completion.Value.Content;
                response = content.Count > 0 ? content[0].Text ?? "" : "";
            }

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
        sb.AppendLine("Score definitions:");
        sb.AppendLine("- Quality (1-5): rubric score against the definition of good SoW content");
        sb.AppendLine("- Faithfulness (0-1): did the output stay true to the original content?");
        sb.AppendLine("- Factual correctness (0-1 F1): precision and recall of factual claims vs original");
        sb.AppendLine("- Response relevancy (0-1): did the output stay on-task?");
        sb.AppendLine("- Context precision (0-1): were the retrieved RAG chunks relevant?");
        sb.AppendLine("- Context recall (0-1): did retrieval find all relevant material?");
        sb.AppendLine("- Noise sensitivity (0-1, LOWER is better): did irrelevant chunks harm the output?");
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
```

- [ ] **Step 2: Register the service in Program.cs**

Add after the existing `EvaluationService` registration (line 23 of `Program.cs`):

```csharp
builder.Services.AddSingleton<IEvaluationSummaryService, EvaluationSummaryService>();
```

- [ ] **Step 3: Run the tests**

Run: `dotnet test SoWImprover.Tests --filter "FullyQualifiedName~EvaluationSummaryServiceTests" --no-restore -v minimal`
Expected: All 7 tests PASS

- [ ] **Step 4: Run full test suite to check for regressions**

Run: `dotnet test SoWImprover.Tests --no-restore -v minimal`
Expected: All existing tests still pass (147+)

- [ ] **Step 5: Commit**

```
git add Services/EvaluationSummaryService.cs Program.cs
git commit -m "feat: implement EvaluationSummaryService with prompt building and content truncation"
```

---

## Chunk 2: Database + UI changes

### Task 4: Add EvaluationSummary to DocumentEntity

**Files:**
- Modify: `Models/DocumentEntity.cs`

- [ ] **Step 1: Add the property**

Add after line 8 of `Models/DocumentEntity.cs` (`public DateTime UploadedAt { get; set; }`):

```csharp
public string? EvaluationSummary { get; set; }
```

- [ ] **Step 2: Delete the SQLite database so EnsureCreated picks up the new column**

Run: `rm SoWImprover/sow-improver.db`

Note: This is a PoC database. In production, use a migration.

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build SoWImprover/SoWImprover.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```
git add Models/DocumentEntity.cs
git commit -m "feat: add EvaluationSummary property to DocumentEntity"
```

---

### Task 5: Add summary parameters and banner to ResultsPanel

**Important:** This task is done BEFORE modifying `Results.razor` so that every commit compiles.

**Files:**
- Modify: `Components/Shared/ResultsPanel.razor`

- [ ] **Step 1: Add new parameters**

Add after the existing `EvaluationAttemptedSections` parameter (line 327 of `ResultsPanel.razor`):

```csharp
[Parameter] public string? EvaluationSummary { get; set; }
[Parameter] public bool SummaryIsPartial { get; set; }
[Parameter] public bool SummaryLoading { get; set; }
[Parameter] public int SectionsEvaluated { get; set; }
[Parameter] public int TotalEvaluatingSections { get; set; }
```

- [ ] **Step 2: Add banner markup**

Insert the banner inside the `else` block (where `Result` is not null), between the `@* Column headers *@` comment (line 12) and `<div class="diff-panels">` (line 13):

```html
@if (EvaluationSummary is not null || SummaryLoading)
{
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
            @if (SummaryIsPartial && TotalEvaluatingSections > 0)
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
}
```

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build SoWImprover/SoWImprover.csproj`
Expected: Build succeeded (new parameters exist but no caller passes them yet — Blazor parameters are optional)

- [ ] **Step 4: Run full test suite**

Run: `dotnet test SoWImprover.Tests --no-restore -v minimal`
Expected: All tests pass

- [ ] **Step 5: Commit**

```
git add Components/Shared/ResultsPanel.razor
git commit -m "feat: add evaluation summary banner and parameters to ResultsPanel"
```

---

### Task 6: Add CSS for partial/loading states

**Files:**
- Modify: `wwwroot/app.css`

- [ ] **Step 1: Add styles**

Append to the end of `app.css`:

```css
/* ── Evaluation summary banner ─────────────────────────────────────────────── */
.app-summary-partial {
    opacity: 0.85;
    border-left: 4px dashed #b1b4b6;
}

.app-summary-status {
    color: #505a5f;
    margin-bottom: 0.5rem;
}
```

- [ ] **Step 2: Commit**

```
git add wwwroot/app.css
git commit -m "feat: add CSS for evaluation summary partial/loading states"
```

---

### Task 7: Wire summary generation into Results.razor

**Files:**
- Modify: `Components/Pages/Results.razor`

This is the most complex task. The changes are:
1. Inject `IEvaluationSummaryService`
2. Add new state fields
3. Load persisted summary on page init
4. Trigger summary generation after each section evaluation completes
5. Persist summary after each generation
6. Pass new parameters to `ResultsPanel`

- [ ] **Step 1: Add the inject and new state fields**

Add to the inject block at the top (after line 7 `@inject EvaluationService Evaluator`):

```csharp
@inject IEvaluationSummaryService SummaryService
```

Add to the `@code` block after the existing evaluation state fields (after line 66 `private CancellationTokenSource? _evalCts;`):

```csharp
// Summary state
private string? _evaluationSummary;
private bool _summaryIsPartial = true;
private bool _summaryLoading;
private int _sectionsEvaluated;
private int _totalToEvaluate;
private bool _summaryStale;
private CancellationTokenSource? _summaryCts;
private DocumentEntity? _document;
```

- [ ] **Step 2: Load persisted summary in OnInitializedAsync**

In `OnInitializedAsync`, after the early return check (after line 84 `return;`), add the document load:

```csharp
_document = await db.Documents.FindAsync(DocumentId);
```

Then after building `_result` (after line 111), before the `foreach` that loads `_suppressed` (line 113), add:

```csharp
if (_document?.EvaluationSummary is not null)
{
    _evaluationSummary = _document.EvaluationSummary;
    _summaryIsPartial = false;
}
```

- [ ] **Step 3: Add summary trigger and persist methods**

Add new methods in the `@code` block (e.g. after `ClearPreview` method):

```csharp
private async Task RefreshSummaryAsync()
{
    if (_result is null || _sections is null) return;

    _summaryLoading = true;
    _summaryStale = false;
    await InvokeAsync(StateHasChanged);

    _summaryCts?.Cancel();
    _summaryCts?.Dispose();
    _summaryCts = new CancellationTokenSource();

    try
    {
        var inputs = new List<SectionSummaryInput>();
        for (var i = 0; i < _result.Sections.Count; i++)
        {
            var sec = _result.Sections[i];
            if (sec.Unrecognised || !sec.RagQualityScore.HasValue) continue;
            inputs.Add(new SectionSummaryInput
            {
                Title = sec.OriginalTitle,
                OriginalContent = sec.OriginalContent,
                RagImprovedContent = sec.ImprovedContent ?? "",
                OriginalQualityScore = sec.OriginalQualityScore,
                BaselineQualityScore = sec.BaselineQualityScore,
                RagQualityScore = sec.RagQualityScore,
                BaselineFaithfulnessScore = sec.BaselineFaithfulnessScore,
                RagFaithfulnessScore = sec.RagFaithfulnessScore,
                ContextPrecisionScore = sec.ContextPrecisionScore,
                ContextRecallScore = sec.ContextRecallScore,
                BaselineFactualCorrectnessScore = sec.BaselineFactualCorrectnessScore,
                RagFactualCorrectnessScore = sec.RagFactualCorrectnessScore,
                BaselineResponseRelevancyScore = sec.BaselineResponseRelevancyScore,
                RagResponseRelevancyScore = sec.RagResponseRelevancyScore,
                NoiseSensitivityScore = sec.NoiseSensitivityScore
            });
        }

        var summary = await SummaryService.GenerateSummaryAsync(
            inputs, _totalToEvaluate, _summaryCts.Token);

        if (!string.IsNullOrEmpty(summary))
        {
            _evaluationSummary = summary;
            await PersistSummaryAsync(summary);
        }
    }
    catch (OperationCanceledException) { }
    catch (Exception ex)
    {
        Logger.LogWarning(ex, "Summary generation failed");
    }
    finally
    {
        _summaryLoading = false;
        await InvokeAsync(StateHasChanged);

        // If another section completed while we were generating, retrigger
        if (_summaryStale)
            _ = RefreshSummaryAsync();
    }
}

private async Task PersistSummaryAsync(string summary)
{
    if (_document is null) return;
    _document.EvaluationSummary = summary;
    await using var db = await DbFactory.CreateDbContextAsync();
    db.Attach(_document).Property(d => d.EvaluationSummary).IsModified = true;
    await db.SaveChangesAsync();
}
```

- [ ] **Step 4: Modify RunEvaluationAsync to trigger summary after each section**

After the `foreach` loop that marks sections as evaluating (line 174, after `_evaluationAttempted.Add(idx);`), add:

```csharp
_totalToEvaluate = toEvaluate.Count;
```

In the streaming loop, after `_evaluatingSections.Remove(prevResultIdx);` and `completed++;` (after line 195), add:

```csharp
_sectionsEvaluated = completed;
_summaryIsPartial = true;
if (_summaryLoading)
    _summaryStale = true;
else
    _ = RefreshSummaryAsync();
```

After the final section persist block, after `_evaluatingSections.Remove(lastResultIdx);` (line 224), add:

```csharp
_sectionsEvaluated = toEvaluate.Count;
_summaryIsPartial = false;
if (_summaryLoading)
    _summaryStale = true;
else
    _ = RefreshSummaryAsync();
```

- [ ] **Step 5: Update DisposeAsync to cancel summary CTS**

In `DisposeAsync`, add before the `_module` disposal (before line 382):

```csharp
_summaryCts?.Cancel();
_summaryCts?.Dispose();
```

- [ ] **Step 6: Pass new parameters to ResultsPanel**

Update the `<ResultsPanel>` component tag (around line 26-40) to include the new parameters after the existing ones:

```csharp
EvaluationSummary="_evaluationSummary"
SummaryIsPartial="_summaryIsPartial"
SummaryLoading="_summaryLoading"
SectionsEvaluated="_sectionsEvaluated"
TotalEvaluatingSections="_totalToEvaluate"
```

- [ ] **Step 7: Verify it compiles**

Run: `dotnet build SoWImprover/SoWImprover.csproj`
Expected: Build succeeded

- [ ] **Step 8: Run full test suite**

Run: `dotnet test SoWImprover.Tests --no-restore -v minimal`
Expected: All tests pass

- [ ] **Step 9: Commit**

```
git add Components/Pages/Results.razor
git commit -m "feat: wire summary generation into Results.razor with progressive updates and persistence"
```

---

## Chunk 3: Rendering tests

### Task 8: Write bUnit rendering tests for the summary banner

**Files:**
- Create: `SoWImprover.Tests/Components/ResultsPanelSummaryTests.cs`

These tests verify the banner renders correctly in different states. They test the `ResultsPanel` component in isolation using bUnit, providing minimal `Result` data and varying the summary parameters.

- [ ] **Step 1: Write the rendering tests**

```csharp
using Bunit;
using SoWImprover.Components.Shared;
using SoWImprover.Models;

namespace SoWImprover.Tests.Components;

public class ResultsPanelSummaryTests : TestContext
{
    private static ImprovementResult MinimalResult => new()
    {
        Sections =
        [
            new SectionResult
            {
                OriginalTitle = "Test Section",
                OriginalContent = "Original text",
                ImprovedContent = "Improved text",
                BaselineContent = "Baseline text"
            }
        ]
    };

    [Fact]
    public void Banner_NotShown_WhenNoSummaryAndNotLoading()
    {
        var cut = RenderComponent<ResultsPanel>(p => p
            .Add(x => x.Result, MinimalResult)
            .Add(x => x.EvaluationSummary, null)
            .Add(x => x.SummaryLoading, false));

        Assert.Empty(cut.FindAll("#eval-summary-title"));
    }

    [Fact]
    public void Banner_Shown_WhenSummaryPresent()
    {
        var cut = RenderComponent<ResultsPanel>(p => p
            .Add(x => x.Result, MinimalResult)
            .Add(x => x.EvaluationSummary, "RAG improved quality across all sections.")
            .Add(x => x.SummaryIsPartial, false)
            .Add(x => x.SummaryLoading, false));

        var banner = cut.Find("#eval-summary-title");
        Assert.NotNull(banner);
        Assert.Contains("RAG improved quality across all sections.", cut.Markup);
    }

    [Fact]
    public void Banner_ShowsPartialIndicator_WhenPartial()
    {
        var cut = RenderComponent<ResultsPanel>(p => p
            .Add(x => x.Result, MinimalResult)
            .Add(x => x.EvaluationSummary, "Partial summary.")
            .Add(x => x.SummaryIsPartial, true)
            .Add(x => x.SectionsEvaluated, 3)
            .Add(x => x.TotalEvaluatingSections, 10)
            .Add(x => x.SummaryLoading, false));

        Assert.Contains("3 of 10 sections evaluated", cut.Markup);
        Assert.Contains("app-summary-partial", cut.Markup);
    }

    [Fact]
    public void Banner_NoPartialIndicator_WhenComplete()
    {
        var cut = RenderComponent<ResultsPanel>(p => p
            .Add(x => x.Result, MinimalResult)
            .Add(x => x.EvaluationSummary, "Final summary.")
            .Add(x => x.SummaryIsPartial, false)
            .Add(x => x.SummaryLoading, false));

        Assert.DoesNotContain("sections evaluated", cut.Markup);
        Assert.DoesNotContain("app-summary-partial", cut.Markup);
    }

    [Fact]
    public void Banner_ShowsSpinner_WhenLoading()
    {
        var cut = RenderComponent<ResultsPanel>(p => p
            .Add(x => x.Result, MinimalResult)
            .Add(x => x.EvaluationSummary, null)
            .Add(x => x.SummaryLoading, true));

        var spinner = cut.Find(".app-badge-spinner");
        Assert.NotNull(spinner);
    }
}
```

- [ ] **Step 2: Run the rendering tests**

Run: `dotnet test SoWImprover.Tests --filter "FullyQualifiedName~ResultsPanelSummaryTests" --no-restore -v minimal`
Expected: All 5 tests PASS

- [ ] **Step 3: Run full test suite**

Run: `dotnet test SoWImprover.Tests --no-restore -v minimal`
Expected: All tests pass (154+ total)

- [ ] **Step 4: Commit**

```
git add SoWImprover.Tests/Components/ResultsPanelSummaryTests.cs
git commit -m "test: add bUnit rendering tests for evaluation summary banner"
```

---

## Chunk 4: Final verification

### Task 9: Full build and test verification

- [ ] **Step 1: Clean build**

Run: `dotnet build SoWImprover/SoWImprover.csproj`
Expected: Build succeeded with no warnings related to our changes

- [ ] **Step 2: Run full test suite**

Run: `dotnet test SoWImprover.Tests --no-restore -v minimal`
Expected: All tests pass (previous 147 + 7 service tests + 5 rendering tests = 159+)

- [ ] **Step 3: Verify the app starts**

Run: `dotnet run --project SoWImprover/SoWImprover.csproj`
Expected: App starts without errors. Results page should not show the banner until evaluation runs (or if a persisted summary exists from a previous run).

- [ ] **Step 4: Final commit if any adjustments were needed**

Only commit if fixes were required during verification.
