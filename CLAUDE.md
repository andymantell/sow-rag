# SoW Improver — CLAUDE.md

> AI context file. For human-readable documentation see `README.md`.

## What This Is
ASP.NET Core 8 / Blazor Server app for RAG-based SoW document improvement.
Corpus PDFs → embedding cache → `EmbeddingRetriever` → per-section LLM rewrite.
See README for architecture overview, prerequisites, and configuration.

## Project Root
`C:\goaco\AI\SOW-RAG\SoWImprover\`

## Key Files
```
Services/
  DefinitionGeneratorService.cs  # BackgroundService — startup orchestrator + definition/embedding caching
  FoundryClientFactory.cs        # Chat + embedding clients (Ollama local / Azure cloud)
  EmbeddingService.cs            # Wraps EmbeddingClient; sequential calls only (no client cache)
  EmbeddingRetriever.cs          # Cosine similarity retrieval + section matching
  DefinitionBuilder.cs           # 2-pass LLM: per-doc analysis → per-section synthesis
  SoWImproverService.cs          # Per-section RAG improvement pipeline
  DocumentLoader.cs              # Python subprocess PDF extraction + chunking
  PythonLocator.cs               # Shared Python executable discovery (cached, thread-safe)
  LlmOutputHelper.cs             # Shared: StripCodeFence for cleaning LLM output
  PdfExportService.cs            # QuestPDF-based PDF generation (markdown tables → native tables)
Data/
  SoWDbContext.cs                # EF Core DbContext (SQLite); registered via AddDbContextFactory
Models/
  GoodDefinition.cs              # Singleton: sections, EmbeddingRetriever, Volatile.Read/Write for IsReady,
                                 # OnChanged event (fired by SetProgress + SetReady)
  DocumentEntity.cs              # Persisted document (Id, FileName, OriginalText, UploadedAt)
  SectionEntity.cs               # Persisted section (per-document, includes Suppressed flag)
  SectionVersionEntity.cs        # Append-only version history per section
  ImprovementResult.cs           # In-memory DTO for section results
Components/Pages/Home.razor      # Upload page + document history table
Components/Pages/Results.razor   # Diff results page (prerender: false); PDF download + section suppression
Components/Pages/Results.razor.js  # Collocated JS module for file download (no global scope)
Components/Shared/ResultsPanel.razor.js  # Tiptap JS interop (createEditor, getMarkdown, toolbar commands)
Components/Layout/MainLayout.razor  # GOV.UK layout; subscribes to GoodDefinition.OnChanged
Components/Shared/               # DefinitionSidebar, UploadPanel, ResultsPanel
sample-sows/embeddings-cache.json  # Auto-generated; delete to force recompute
sample-sows/definition-cache.json  # Auto-generated; delete to force rebuild
```

## Workflow Rules

- **All code changes require tests** — every bug fix, feature, and behavioural change must include new or updated tests covering the change. Unit tests for logic/services, E2E tests for user-facing behaviour. Do not consider work complete until tests pass.
- **Ask before refactoring** — when a fix involves replacing one approach with a different one (e.g. swapping embedding-based matching for string-based matching), stop and discuss the trade-offs with the user before implementing. Don't assume the simpler approach is acceptable.
- **Favour framework-native patterns** — prefer Blazor collocated JS modules (`.razor.js`) over global scripts, `IDbContextFactory` over injected `DbContext`, etc.

## Critical Don'ts

- **Never send multiple texts in one `GenerateEmbeddingsAsync` call to Ollama** — Ollama concatenates them and exceeds context limits. `EmbedBatchAsync` loops sequentially.
- **`OpenAIClientOptions.Endpoint` must include `/v1`** — the SDK appends `chat/completions` (not `/v1/chat/completions`), so the endpoint must be `http://host:PORT/v1`.
- **Never inject `SoWDbContext` directly in Blazor components** — use `IDbContextFactory<SoWDbContext>` and create short-lived contexts. Scoped DbContext leaks memory over Blazor Server circuit lifetime.

## Key Implementation Notes

**Single model architecture:** Both SoW improvement and Ragas evaluation use the same Ollama model (`Ollama:ChatModelName`, default `qwen3.5:27b`). No GPU memory swapping needed. Embeddings use a separate model (`Ollama:EmbeddingModelName`, default `nomic-embed-text`).

**Database:** SQLite via EF Core + `AddDbContextFactory`. All Blazor components create short-lived `DbContext` instances via `IDbContextFactory<SoWDbContext>`. `Results.razor` uses `db.Attach(entity).Property(...).IsModified = true` for detached entity updates. `EnsureCreated()` at startup (PoC only — use migrations in production).

**FoundryClientFactory:** Two semaphores — `_lock` (chat), `_embeddingLock` (embedding). In local mode, connects to Ollama's OpenAI-compatible API. In cloud mode, connects to Azure AI Foundry via `AzureOpenAIClient`. No Foundry Local CLI dependency.

**Embedding cache:** `{corpus}/embeddings-cache.json`. Valid when SHA256 fingerprint of `{filename}|{size}|{lastWriteTimeUtc}` for all PDFs and model name both match. Cache entries use `GlobalIndex` (not per-document `ChunkIndex`) for correct ordering across multiple documents.

**Definition cache:** `{corpus}/definition-cache.json`. Valid when corpus fingerprint and chat model name both match. Avoids re-running multi-document LLM analysis on restart.

**Redaction cache:** `{corpus}/redactions-cache.json`. Valid when corpus fingerprint and chat model name both match. Stores LLM-redacted chunk text computed at indexing time. Two-pass approach: `ChunkRedactor.Redact()` applies fast regex for unambiguous patterns (emails, postcodes, money, dates, phones, refs, gov abbreviations), then `RedactWithLlmAsync()` sends to the LLM for contextual entities (company names, gov departments, person names, addresses). `DocumentChunk.RedactedText` holds the result; used at prompt time instead of runtime regex.

**Chunking:** `text.Split([' ', '\n', '\r', '\t'], ...)` — must split on all whitespace. Splitting on space only causes markdown table rows to become single oversized tokens that exceed Ollama's context limit. `EmbedAsync` also truncates at 8000 chars as a safety net.

**Canonical sections:** Single source of truth in `DefinitionBuilder.CanonicalSections` (15 sections). `CorpusInitialisationService` delegates to `DefinitionBuilder`.

**GoodDefinition threading:** `IsReady` uses `Volatile.Read`/`Volatile.Write` for portable memory fences (safe on both x86 and ARM). `Retriever` and `Sections` are safe to read after `IsReady == true`.

**Per-section LLM prompts:** Use `$$"""..."""` raw strings (double-dollar) to allow literal `{` in JSON examples alongside `{{interpolations}}`.

**PDF export:** QuestPDF Community license set once in `Program.cs`. `PdfExportService.Generate` is static and thread-safe. Callers must snapshot mutable state (e.g. `_suppressed.ToHashSet()`) before passing to `Task.Run`.

**Tiptap editor:** Loaded at runtime from esm.sh CDN (no vendored bundles, no npm). JS interop via collocated `ResultsPanel.razor.js`. Editor instances tracked by element ID in a JS `Map`. Toolbar uses `data-cmd` attributes with document-level event delegation (survives Blazor re-renders). Uses `tiptap-markdown@0.8.10` for markdown round-trip.

**Version history:** Append-only `SectionVersionEntity` table. Restoring old versions creates a new version (never overwrites). `SectionEntity.ImprovedContent` always reflects the latest version. Version data loaded via `Include(s => s.Versions)` in Results.razor. `SectionResult.ImprovedContent` is `{ get; set; }` (mutable) so ResultsPanel can update it after edits.

**`RuntimeIdentifier = win-x64`** set in `.csproj` — app targets Windows only. `Microsoft.AI.Foundry.Local` and `UglyToad.PdfPig` NuGets have been removed (unused).

**`<base href="/">`** in `App.razor` is required — without it, nested routes like `/results/{guid}` break CSS/JS/Blazor circuit URL resolution.

## Current State
App is working end-to-end with SQLite persistence, PDF export, and section suppression. Four rounds of code review applied. Key architectural changes:
- Unified model: single Qwen3.5-27B via Ollama for both improvement and evaluation (was phi-4 + qwen2.5:14b)
- Removed Foundry Local CLI dependency (was needed for phi-4 model resolution)
- Removed GpuMemoryManager (no longer needed with single model)
- Extracted shared PythonLocator (was duplicated in DocumentLoader and EvaluationService)
- EvaluationSummaryService uses IChatService (was creating its own OpenAI client)
- GoodDefinition uses Volatile.Read/Write (was relying on volatile keyword only)
- Corpus fingerprint includes file last-modified timestamp
