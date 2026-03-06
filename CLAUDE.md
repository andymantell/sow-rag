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
  FoundryClientFactory.cs        # Chat + embedding clients (local/cloud)
  EmbeddingService.cs            # Wraps EmbeddingClient; sequential calls only (no client cache)
  EmbeddingRetriever.cs          # Cosine similarity retrieval + section matching
  DefinitionBuilder.cs           # 2-pass LLM: per-doc analysis → per-section synthesis
  SoWImproverService.cs          # Per-section RAG improvement pipeline
  DocumentLoader.cs              # Python subprocess PDF extraction + chunking
  LlmOutputHelper.cs             # Shared: StripCodeFence for cleaning LLM output
  PdfExportService.cs            # QuestPDF-based PDF generation (markdown tables → native tables)
Data/
  SoWDbContext.cs                # EF Core DbContext (SQLite); registered via AddDbContextFactory
Models/
  GoodDefinition.cs              # Singleton: sections, EmbeddingRetriever, volatile IsReady,
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

- **Ask before refactoring** — when a fix involves replacing one approach with a different one (e.g. swapping embedding-based matching for string-based matching), stop and discuss the trade-offs with the user before implementing. Don't assume the simpler approach is acceptable.
- **Favour framework-native patterns** — prefer Blazor collocated JS modules (`.razor.js`) over global scripts, `IDbContextFactory` over injected `DbContext`, etc.

## Critical Don'ts

- **Never use `FoundryLocalManager.StartWebServiceAsync()`** — it starts an in-process Foundry service that cannot find models downloaded by the CLI. Always connect via `foundry service status` subprocess.
- **Never send multiple texts in one `GenerateEmbeddingsAsync` call to Ollama** — Ollama concatenates them and exceeds context limits. `EmbedBatchAsync` loops sequentially.
- **`OpenAIClientOptions.Endpoint` must include `/v1`** — the SDK appends `chat/completions` (not `/v1/chat/completions`), so the endpoint must be `http://host:PORT/v1`.
- **Never inject `SoWDbContext` directly in Blazor components** — use `IDbContextFactory<SoWDbContext>` and create short-lived contexts. Scoped DbContext leaks memory over Blazor Server circuit lifetime.

## Key Implementation Notes

**Database:** SQLite via EF Core + `AddDbContextFactory`. All Blazor components create short-lived `DbContext` instances via `IDbContextFactory<SoWDbContext>`. `Results.razor` uses `db.Attach(entity).Property(...).IsModified = true` for detached entity updates. `EnsureCreated()` at startup (PoC only — use migrations in production).

**FoundryClientFactory threading:** Three semaphores — `_lock` (chat), `_embeddingLock` (embedding), `_rootLock` (Foundry service URL). Service URL cached in `_cachedServiceRoot` so `foundry service status` subprocess runs once.

**Model ID resolution (chat only):** Config alias (e.g. `phi-4`) resolved to actual loaded model ID (e.g. `Phi-4-trtrtx-gpu:1`) by querying `/v1/models`. Ollama embedding model name is passed directly — no resolution needed.

**Embedding cache:** `{corpus}/embeddings-cache.json`. Valid when SHA256 fingerprint of `{filename}|{size}` for all PDFs and model name both match. Cache entries use `GlobalIndex` (not per-document `ChunkIndex`) for correct ordering across multiple documents.

**Definition cache:** `{corpus}/definition-cache.json`. Valid when corpus fingerprint and chat model name both match. Avoids re-running multi-document LLM analysis on restart.

**Chunking:** `text.Split([' ', '\n', '\r', '\t'], ...)` — must split on all whitespace. Splitting on space only causes markdown table rows to become single oversized tokens that exceed Ollama's context limit. `EmbedAsync` also truncates at 8000 chars as a safety net.

**Canonical sections:** Defined in both `DefinitionBuilder.CanonicalSections` and `DefinitionGeneratorService.CanonicalSections` — must be kept in sync (15 sections).

**GoodDefinition threading:** `IsReady` is `volatile bool` — acts as release fence. `Retriever` and `Sections` are safe to read after `IsReady == true`.

**Per-section LLM prompts:** Use `$$"""..."""` raw strings (double-dollar) to allow literal `{` in JSON examples alongside `{{interpolations}}`.

**PDF export:** QuestPDF Community license set once in `Program.cs`. `PdfExportService.Generate` is static and thread-safe. Callers must snapshot mutable state (e.g. `_suppressed.ToHashSet()`) before passing to `Task.Run`.

**Tiptap editor:** Loaded at runtime from esm.sh CDN (no vendored bundles, no npm). JS interop via collocated `ResultsPanel.razor.js`. Editor instances tracked by element ID in a JS `Map`. Toolbar uses `data-cmd` attributes with document-level event delegation (survives Blazor re-renders). Uses `tiptap-markdown@0.8.10` for markdown round-trip.

**Version history:** Append-only `SectionVersionEntity` table. Restoring old versions creates a new version (never overwrites). `SectionEntity.ImprovedContent` always reflects the latest version. Version data loaded via `Include(s => s.Versions)` in Results.razor. `SectionResult.ImprovedContent` is `{ get; set; }` (mutable) so ResultsPanel can update it after edits.

**`RuntimeIdentifier = win-x64`** set in `.csproj` — app targets Windows only (Foundry Local CLI is Windows-only). `Microsoft.AI.Foundry.Local` and `UglyToad.PdfPig` NuGets have been removed (unused).

**`<base href="/">`** in `App.razor` is required — without it, nested routes like `/results/{guid}` break CSS/JS/Blazor circuit URL resolution.

## Current State
App is working end-to-end with SQLite persistence, PDF export, and section suppression. Three rounds of code review applied. Key fixes include:
- XSS: Markdig `.DisableHtml()` on all markdown pipelines
- Accessibility: `aria-live`, `aria-labelledby`, `aria-hidden` on relevant elements
- Security: server-side file type validation, exception messages not exposed to UI
- Blazor: `NavigationException` re-thrown, Results page uses `prerender: false`
- Architecture: polling replaced with `GoodDefinition.OnChanged` event subscription; `IDbContextFactory` for proper DbContext lifetime
- Correctness: `StripCodeFences` and heading-strip regex fixed (`\A` anchor, no Multiline)
- Thread-safety: `_suppressed` HashSet snapshot before `Task.Run` in PDF generation
- `EmbeddingService` no longer caches client (factory handles caching)
- `DiffService` and `ResultState` deleted (dead code, replaced by persistence)
- `StripCodeFence` consolidated into shared `LlmOutputHelper` (was duplicated)
- `FoundryClientFactory` implements `IDisposable` for semaphore cleanup
- `DocumentLoader`: thread-safe `_pythonExe` (lock) and `ConcurrentDictionary` for extracted texts
- PDF magic byte validation before Python subprocess
- Responsive CSS breakpoint for diff panels on narrow screens
