# Batch Runner & Experiment Report — Design Spec

## Objective

Build a console application that batch-processes a folder of test SoW PDFs through the existing improvement pipeline (baseline + RAG), runs Ragas evaluation on each, exports structured results, and provides a Claude CLI slash command to generate an analytical report from the export.

## Deliverables

1. **`CorpusInitialisationService`** — extracted from `DefinitionGeneratorService` (shared by both the main app and batch runner)
2. **`SoWImprover.BatchRunner`** — console project in the solution
3. **`.claude/commands/experiment-report.md`** — slash command for report generation
4. **README update** — how to run the experiment
5. **Tests** — unit/integration tests for new code

---

## 1. Console App: `SoWImprover.BatchRunner`

### Project Setup

- New console project targeting .NET 8, added to `SoWImprover.slnx`
- References `SoWImprover` project (to reuse all services, models, DI registrations)
- `RuntimeIdentifier = win-x64` in `.csproj` (matches main app — Foundry CLI is Windows-only)
- Single entry point: `Program.cs`

### Arguments

```
SoWImprover.BatchRunner <test-folder-path>
```

One required positional argument: the path to a folder containing test SoW PDFs. These must be documents **not** in the corpus folder.

### DI Wiring

Reuses the main app's service registrations with these modifications:

- **Remove `DefinitionGeneratorService`** (the hosted background service) — we orchestrate corpus initialisation manually so we can log each stage
- **Register all singletons** as in `Program.cs`: `GoodDefinition`, `DocumentLoader`, `FoundryClientFactory`, `IChatService`, `IEmbeddingService`, `DefinitionBuilder`, `SoWImproverService`, `EvaluationService`, `IEvaluationSummaryService`, `GpuMemoryManager`
- **Register `IDbContextFactory<SoWDbContext>`** with SQLite (same DB path as the main app so results are visible in the UI too)
- **Load configuration** from the main app's `appsettings.json` (set content root to `SoWImprover/` directory)
- **Python scripts** (`pdf_to_markdown.py`, `ragas_evaluate.py`): services locate these via `AppContext.BaseDirectory`. Include them as `<Content>` items in the batch runner's `.csproj` (copy to output), or set the working directory to `SoWImprover/` at startup
- **Evaluation feature flag**: The batch runner always runs evaluation — ignore the `FeatureManagement:Evaluation` flag

### Pipeline Flow

```
Phase 1: Corpus Initialisation
  1. DocumentLoader.LoadFolder(corpusPath) — extract + chunk all corpus PDFs
  2. Embed all chunks via IEmbeddingService.EmbedBatchAsync() (uses cache if valid)
  3. Redact chunks — regex + LLM (uses cache if valid)
  4. Build section definitions via DefinitionBuilder (uses cache if valid)
  5. Set GoodDefinition ready

Phase 2: Per-Document Processing
  For each PDF in test folder:
    1. GpuMemoryManager.PrepareForImprovementAsync() — ensure VRAM is free for improvement model
       (NOTE: PrepareForEvaluationAsync is NOT called here — EvaluationService calls it internally)
    2. DocumentLoader.ExtractTextAsync(pdfPath) → raw text
    3. SoWImproverService.ImproveAsync(text, definition, progress, ct) → ImprovementResult
       - Signature: (string originalText, GoodDefinition definition, IProgress<string>? progress, CancellationToken ct)
       - Batch runner provides a custom IProgress<string> that writes timestamped console output
       - Internally runs baseline (no chunks) then RAG (top-K chunks) per section
    4. Capture RetrievedScores and RetrievedContexts from in-memory SectionResult
       (these are [JsonIgnore] and not persisted — must be captured before DB round-trip)
    5. Persist DocumentEntity + SectionEntities to SQLite
       - Store RetrievedContextsJson and DefinitionOfGoodText on each section (needed for evaluation)
    6. Build List<SectionInput> for evaluation:
       - SectionInput { Original, Baseline, RagImproved, RetrievedContexts, DefinitionOfGood }
       - Same mapping as Results.razor lines 176-191
    7. EvaluationService.EvaluateStreamingAsync(List<SectionInput>, ct) → stream (Index, SectionScores)
       - GpuMemoryManager.PrepareForEvaluationAsync() is called internally
       - Persist each SectionScores to the SectionEntity as it arrives
    8. IEvaluationSummaryService.GenerateSummaryAsync(List<SectionSummaryInput>, totalSectionCount, ct)
       - Persist to DocumentEntity.EvaluationSummary

Phase 3: Data Export
  1. Build export from in-memory results (not DB round-trip) — preserves RetrievedScores
  2. Write experiment-results.json (see format below)
  3. Print instructions for report generation
```

### Corpus Initialisation

#### Prerequisite refactoring: extract `CorpusInitialisationService`

The cache DTOs (`DefinitionCacheFile`, `EmbeddingCacheFile`, `RedactionCacheFile`) and the corpus init logic (`GetOrComputeChunkVectorsAsync`, `RedactChunksAsync`, `ComputeCorpusFingerprint`, etc.) are currently `file`-scoped inside `DefinitionGeneratorService.cs` — inaccessible to other code.

**Before building the batch runner**, extract this into a shared `CorpusInitialisationService` that both `DefinitionGeneratorService` and the batch runner consume. This service encapsulates:

1. `DocumentLoader.LoadFolder(corpusPath)` — returns `List<DocumentChunk>`
2. `ComputeCorpusFingerprint()` (SHA256 of sorted `filename|size` pairs) — check against cache
3. Embed chunks: `IEmbeddingService.EmbedBatchAsync()` with cache check
4. Redact chunks: regex pass via `ChunkRedactor.Redact()`, then LLM pass via `RedactWithLlmAsync()` with cache check
5. Build definitions: `DefinitionBuilder.BuildDefinitionAsync()` with cache check
6. Construct `EmbeddingRetriever` from chunk vectors
7. Call `GoodDefinition.SetReady(sections, retriever, docCount, chunkCount)`

The extracted service should accept an `Action<string>` progress callback so both the hosted service and the batch runner can log in their own way.

After extraction, `DefinitionGeneratorService` becomes a thin wrapper that calls `CorpusInitialisationService` and maps progress to `GoodDefinition.SetProgress()`.

### Database Persistence

Uses `IDbContextFactory<SoWDbContext>` to create short-lived contexts (same pattern as `Results.razor`):

```csharp
await using var db = await dbFactory.CreateDbContextAsync();
// Create DocumentEntity, add SectionEntities, save
// Later: attach and update individual properties for scores
```

Key patterns from `Results.razor` to replicate:
- Create `DocumentEntity` with `FileName`, `OriginalText`, `UploadedAt`
- Create `SectionEntity` per section with all fields from `ImprovementResult.SectionResult`
- Store `RetrievedContextsJson` as serialised JSON array of chunk texts
- Store `DefinitionOfGoodText` for evaluation
- Update scores via `db.Attach(section).Property(x => x.RagQualityScore).IsModified = true` pattern
- Create initial `SectionVersionEntity` for each section

### Console Output

Verbose, timestamped, hierarchical output. Every stage logs progress:

```
[HH:mm:ss] === SoW Improver Batch Runner ===
[HH:mm:ss] Test folder: <path> (N PDFs found)
[HH:mm:ss] Corpus:      <path> (M PDFs)
[HH:mm:ss] Chat model:  <name> | Embedding model: <name>
[HH:mm:ss]
[HH:mm:ss] --- Corpus Initialisation ---
[HH:mm:ss] Loading corpus PDFs...
[HH:mm:ss] Embedding N chunks...
[HH:mm:ss] Redacting chunks...
[HH:mm:ss] Building section definitions...
[HH:mm:ss] Corpus ready. M documents, N chunks, 15 sections defined.
[HH:mm:ss]
[HH:mm:ss] === Document 1/K: filename.pdf ===
[HH:mm:ss] Extracting text... W words
[HH:mm:ss] Improving sections (baseline + RAG)...
[HH:mm:ss]   [1/S] Section Name
[HH:mm:ss]     Baseline... done (Xs)
[HH:mm:ss]     RAG (C chunks, best=0.XX)... done (Xs)
[HH:mm:ss]   [2/S] Section Name
...
[HH:mm:ss] Improvement complete. S sections, U unrecognised (skipped).
[HH:mm:ss] Running evaluation...
[HH:mm:ss]   [1/E] Section Name
[HH:mm:ss]     Original: N | Baseline: N | RAG: N
[HH:mm:ss]     Faithfulness — baseline: 0.XX | RAG: 0.XX
[HH:mm:ss]     Factual correctness — baseline: 0.XX | RAG: 0.XX
[HH:mm:ss]     Response relevancy — baseline: 0.XX | RAG: 0.XX
[HH:mm:ss]     Context — precision: 0.XX | recall: 0.XX | noise: 0.XX
...
[HH:mm:ss] Evaluation complete.
[HH:mm:ss] Generating summary... done.
[HH:mm:ss]
[HH:mm:ss] === All documents processed ===
[HH:mm:ss] Results exported to: experiment-results.json
[HH:mm:ss] Database at: sow-improver.db
[HH:mm:ss]
[HH:mm:ss] To generate the analysis report, run:
[HH:mm:ss]   claude /experiment-report
```

### Error Handling

- If a single document fails (extraction, improvement, or evaluation), log the error and continue to the next document. Don't abort the batch.
- If corpus initialisation fails, abort with a clear error message (nothing useful can be done without the corpus).
- Log all exceptions with full detail to console.

---

## 2. Data Export Format

File: `experiment-results.json` written to the current working directory.

```json
{
  "exportedAt": "2026-03-17T14:30:00Z",
  "corpus": {
    "folder": "sample-sows",
    "documents": ["file1.pdf", "file2.pdf"],
    "totalChunks": 42,
    "embeddingModel": "nomic-embed-text",
    "chatModel": "phi-4"
  },
  "testDocuments": [
    {
      "fileName": "test-sow.pdf",
      "sectionCount": 8,
      "evaluatedSectionCount": 6,
      "evaluationSummary": "LLM-generated markdown summary...",
      "sections": [
        {
          "sectionName": "Scope of Work",
          "matchedCanonicalSection": "Scope of Work",
          "unrecognised": false,
          "scores": {
            "originalQuality": 2,
            "baselineQuality": 3,
            "ragQuality": 4,
            "baselineFaithfulness": 0.85,
            "ragFaithfulness": 0.82,
            "baselineFactualCorrectness": 0.90,
            "ragFactualCorrectness": 0.88,
            "baselineResponseRelevancy": 0.75,
            "ragResponseRelevancy": 0.80,
            "contextPrecision": 0.60,
            "contextRecall": 0.45,
            "noiseSensitivity": 0.30
          },
          "retrievedChunkCount": 5,
          "retrievedScores": [0.72, 0.65, 0.51, 0.42, 0.35],
          "retrievedContexts": ["chunk text 1...", "chunk text 2..."],
          "originalContent": "full original text...",
          "baselineContent": "full baseline improvement...",
          "ragContent": "full RAG improvement..."
        }
      ]
    }
  ]
}
```

Content fields (originalContent, baselineContent, ragContent) included in full so Claude can do qualitative analysis — reading what actually changed, not just scores.

---

## 3. Claude Slash Command: `/experiment-report`

File: `.claude/commands/experiment-report.md`

A prompt template that instructs Claude to:

1. Find and read the most recent `experiment-results.json` in the repo
2. Read `experiment-plan-rag-vs-baseline.md` for the analysis framework
3. Read the actual corpus PDFs in `sample-sows/` (extract via `pdf_to_markdown.py` or read cached text) — form an independent judgement on their quality, specificity, vocabulary, and appropriateness as RAG source material
4. Write the report following this structure:
   - **Executive Summary** — 2-3 sentences: does RAG help, is the corpus sufficient?
   - **Methodology** — experiment setup, metrics, LLM configuration, corpus description
   - **Results** — aggregate comparison tables, per-section breakdown, statistical tests where sample size permits
   - **Retrieval Analysis** — what the retriever found, similarity score distributions, chunk relevance
   - **Corpus Assessment** — independent qualitative review of each corpus document: what it actually contains after redaction, how specific vs generic the content is, whether it provides information an LLM wouldn't already know from pretraining, vocabulary diversity, structural coverage across the 15 canonical sections
   - **Discussion** — interpretation, limitations (evaluator bias, sample size, local LLM ceiling, single-run variance)
   - **Recommendations** — concrete next steps for corpus improvement, RAG configuration, or alternative approaches
5. Save to `experiment-report-YYYY-MM-DD.md` at repo root

The slash command should produce a frank, technical internal report — no sugar-coating, with specific evidence from the data.

---

## 4. README Update

Add a section to the existing README:

```markdown
## Running the RAG vs Baseline Experiment

### Prerequisites
- Local LLM running (Foundry or Ollama) with chat and embedding models
- Claude CLI installed (for report generation)

### Step 1: Run the batch evaluation
```
dotnet run --project SoWImprover.BatchRunner -- "C:\path\to\test-pdfs"
```

Processes every PDF in the folder through baseline and RAG improvement,
runs Ragas evaluation, and exports results to `experiment-results.json`.
This takes a long time with local models — expect ~15-30 minutes per document.

### Step 2: Generate the analysis report
```
claude /experiment-report
```

Reads the exported results and writes a technical analysis report
comparing RAG vs baseline performance.
```

---

## 5. Tests

### Unit Tests

- **Data export serialisation** — verify the JSON export format is correct given known input data
- **Console output formatting** — verify timestamp format and message structure (test the logging helper, not stdout directly)
- **Pipeline orchestration** — verify the batch runner processes documents in order, skips failures, and continues

### Integration Tests

- **End-to-end with mocked services** — wire up DI with stubbed `IChatService`, `IEmbeddingService`, `EvaluationService`. Verify:
  - Documents are persisted to SQLite with correct structure
  - Scores are persisted as they stream in
  - Export JSON contains all expected fields
  - Evaluation summary is generated and persisted
- **Error resilience** — verify that a failing document doesn't abort the batch

### What NOT to test

- The existing services (`SoWImproverService`, `EvaluationService`, etc.) — they have their own tests
- The slash command content — it's a prompt, not code

---

## 6. Known Constraints

- **Execution time**: Local LLM processing is slow. A batch of 5 documents with 15 sections each could take hours. The verbose output lets users monitor progress.
- **Same database**: The batch runner writes to the same `sow-improver.db` as the main app. Results are visible in the UI after batch completion. **Do not run the batch runner while the main app is running** — SQLite does not handle concurrent writers well.
- **Corpus caches**: If caches exist and are valid (fingerprint + model match), they're reused. Delete cache files to force recomputation.
- **Evaluator bias**: Ragas evaluation uses the same local LLM that performed the improvement. The report should note this limitation.
- **Sensitive data**: `experiment-results.json` contains full document content (even after redaction). Add to `.gitignore`. Similarly for generated report files.
