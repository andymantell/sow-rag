# SoW Improver

A locally-hosted Blazor Server web application that uses RAG and a local LLM to improve Statements of Work (SoW) documents. It analyses a corpus of known-good SoW PDFs to derive a reusable "definition of good", then uses that to generate an improved version of any uploaded SoW — presented as a side-by-side diff view in the browser.

---

## Prerequisites

### 1. .NET 8 SDK
Download from https://dotnet.microsoft.com/download/dotnet/8

### 2. Python 3 + pymupdf4llm

PDF text extraction runs via a Python subprocess:

```bash
pip install pymupdf4llm
```

### 3. Ollama (for local embeddings)

Install Ollama from https://ollama.com, then pull the embedding model:

```bash
ollama pull nomic-embed-text
```

Ollama must be running at `http://localhost:11434` before starting the app.

### 4. Microsoft Foundry Local (for local LLM inference)

Install the Foundry Local CLI:

```powershell
winget install Microsoft.FoundryLocal
```

Then download a model. The app defaults to **phi-4**:

```bash
foundry model download phi-4
```

#### Model recommendation for RTX 5090

The RTX 5090 has 32 GB of VRAM:

| Model | VRAM (Q4) | Notes |
|---|---|---|
| **phi-4** (default) | ~8 GB | Fast, excellent reasoning, comfortable fit |
| **mistral-small-3.1-24b** | ~14 GB | Good quality/speed balance |
| **llama3.3-70b-instruct** | ~40 GB | Exceeds VRAM solo |

**Recommendation:** Start with `phi-4`. If you need stronger reasoning, try `mistral-small-3.1-24b`:

```bash
foundry model download mistral-small-3.1-24b
```

Then update `appsettings.json`: `"LocalModelName": "mistral-small-3.1-24b"`

---

## Setup

### 1. Populate the known-good SoW corpus

Drop 2–10 representative SoW PDFs into the `SoWImprover/sample-sows/` folder. The app will not start without at least one PDF. PDFs must be text-based (not scanned/image-only).

### 2. Review configuration

`SoWImprover/appsettings.json` defaults:

```json
{
  "Foundry": {
    "UseLocal": true,
    "LocalModelName": "phi-4",
    "CloudEndpoint": "",
    "CloudApiKey": "",
    "CloudModelName": "",
    "CloudEmbeddingDeployment": ""
  },
  "Ollama": {
    "Endpoint": "http://localhost:11434/v1",
    "EmbeddingModelName": "nomic-embed-text"
  },
  "Docs": {
    "KnownGoodFolder": "./sample-sows",
    "ChunkSize": 500,
    "ChunkOverlap": 50,
    "TopKChunks": 5
  }
}
```

---

## Running

```bash
cd SoWImprover
dotnet run
```

Open your browser at `http://localhost:5194` (or the URL shown in the console).

> **Startup time:** On first run the app embeds all corpus chunks via Ollama (cached to `sample-sows/embeddings-cache.json` for subsequent runs) and builds the definition of good via LLM (cached to `sample-sows/definition-cache.json`). With phi-4 and 4 documents this takes 2–5 minutes. Subsequent starts load from cache in seconds.

> **Improvement time:** One LLM call per section sequentially. A 10-section SoW may take 5–15 minutes.

### Clearing caches

If you add or remove PDFs from `sample-sows/`, the caches are automatically invalidated (fingerprint mismatch). To force regeneration manually:

```bash
del SoWImprover\sample-sows\embeddings-cache.json
del SoWImprover\sample-sows\definition-cache.json
```

---

## Testing

The project has two test suites: unit/integration tests and end-to-end browser tests.

### Unit & integration tests

```bash
dotnet test SoWImprover.Tests/SoWImprover.Tests.csproj
```

These test services, models, and Blazor components in isolation using NSubstitute stubs and bUnit. No external dependencies (LLM, Ollama, Python, browser) are required — everything is stubbed or uses in-memory SQLite.

**What's covered:**
- Section splitting and heading detection (`SplitIntoSections`, `IsHeading`)
- LLM output cleaning (`StripCodeFence`)
- Document chunking and whitespace handling (`DocumentLoader.ChunkText`)
- Embedding retrieval and cosine similarity (`EmbeddingRetriever`)
- PDF export with markdown rendering, suppressed sections, and edge cases (`PdfExportService`)
- Improvement pipeline: section matching, improvement, explanation, progress reporting, error handling (`SoWImproverService`)
- Definition building: analyse/synthesise flow, code fence stripping, multi-document support (`DefinitionBuilder`)
- Full pipeline integration: definition building → `GoodDefinition` ready → improve uploaded text
- `GoodDefinition` model: state transitions, event firing, markdown generation
- Blazor components: `DefinitionSidebar` (loading/ready states), `UploadPanel` (validation errors), `Home` (previous documents table)
- App host smoke test: app starts and serves the home page

### End-to-end browser tests

```bash
dotnet test SoWImprover.E2E/SoWImprover.E2E.csproj
```

These use Playwright to drive a real Chromium browser against the app running on Kestrel with stubbed services. Playwright browsers must be installed first:

```bash
dotnet build SoWImprover.E2E/SoWImprover.E2E.csproj
node SoWImprover.E2E/bin/Debug/net8.0/.playwright/package/cli.js install --with-deps chromium
```

To run tests in headed mode (visible browser):

```bash
PLAYWRIGHT_HEADED=1 dotnet test SoWImprover.E2E/SoWImprover.E2E.csproj
```

**What's covered:**
- Home page: upload form rendering, validation errors, definition sidebar, previous documents table
- Upload flow: PDF upload → results page navigation, section rendering, document history
- Results page: section headings, original/improved diff, "what changed" explanations, back link navigation, invalid document redirect
- Section suppression: exclude/include toggle, persistence after reload
- Inline editing: editor open/save/cancel, version dropdown, version restore, persistence after reload, unsaved changes confirmation dialog
- PDF download: file download with correct filename and valid PDF bytes, excluded sections produce smaller output
- Definition loading: submit blocked when definition not ready, error message shown

### Using Make

If `make` is available, shortcuts are provided:

```bash
make test              # unit & integration tests
make e2e-test          # E2E tests (installs Playwright if needed)
make e2e-test-headed   # E2E tests with visible browser
```

---

## Features

- **Upload & improve** — upload a SoW PDF and get an AI-improved version with section-by-section rewrites
- **Side-by-side diff** — original and improved content shown side by side with "what changed" explanations
- **Inline editing** — edit improved sections with a WYSIWYG markdown editor (bold, italic, headings, lists, tables)
- **Version history** — browse and restore previous versions of each section via inline version pills
- **Section suppression** — exclude individual sections from the output with one click
- **PDF export** — download the improved document as a formatted PDF (respects suppressed sections)
- **Document history** — previous uploads are persisted and accessible from the home page
- **Definition of good** — sidebar shows the auto-generated quality standards used for improvement

---

## Database

The app uses **SQLite** via Entity Framework Core for persistence. The database file `sow-improver.db` is created automatically on first run.

### What's stored

| Entity | Purpose |
|---|---|
| `DocumentEntity` | Uploaded document metadata (filename, original text, upload timestamp) |
| `SectionEntity` | Per-section results (original, improved, explanation, suppressed flag) |
| `SectionVersionEntity` | Append-only edit history per section (version number, content, timestamp) |

### Schema management

Currently uses `EnsureCreated()` at startup (suitable for PoC). For production, switch to EF Core migrations:

```bash
cd SoWImprover
dotnet ef migrations add InitialCreate
dotnet ef database update
```

### Moving to PostgreSQL or SQL Server

The app uses EF Core with `IDbContextFactory`, so switching database providers requires only two changes:

**1. Swap the NuGet package:**

```bash
# PostgreSQL
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL

# SQL Server
dotnet add package Microsoft.EntityFrameworkCore.SqlServer
```

**2. Update `Program.cs`:**

```csharp
// PostgreSQL
builder.Services.AddDbContextFactory<SoWDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// SQL Server
builder.Services.AddDbContextFactory<SoWDbContext>(opts =>
    opts.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
```

**3. Add a connection string to `appsettings.json`:**

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Database=sow_improver;Username=app;Password=secret"
  }
}
```

No changes to the `DbContext`, entities, or Blazor components are needed — EF Core abstracts the provider.

> **Note:** Replace `EnsureCreated()` with proper EF Core migrations before going to production, as `EnsureCreated()` cannot update an existing schema.

---

## Switching to Azure AI Foundry Cloud

No code changes required — only configuration:

```json
{
  "Foundry": {
    "UseLocal": false,
    "CloudEndpoint": "https://<your-resource>.services.ai.azure.com/",
    "CloudApiKey": "<your-api-key>",
    "CloudModelName": "gpt-4o",
    "CloudEmbeddingDeployment": "text-embedding-3-small"
  }
}
```

When `UseLocal` is `false`:
- LLM calls go to Azure AI Foundry via `AzureOpenAIClient`
- Embeddings go to Azure OpenAI via `CloudEmbeddingDeployment`
- Ollama is not used

For production, use environment variables instead of secrets in `appsettings.json`:

```bash
export Foundry__CloudApiKey="your-key"
```

---

## Architecture

- **Frontend:** Blazor Server (interactive components, GOV.UK Design System styling, no JS framework)
- **PDF extraction:** Python subprocess using `pymupdf4llm` — produces clean markdown from PDFs
- **PDF export:** QuestPDF (Community license) — server-side PDF generation with markdown table rendering
- **Persistence:** SQLite via EF Core `IDbContextFactory` — documents and section results survive restarts
- **Embeddings:** nomic-embed-text via Ollama (local) or Azure OpenAI (cloud) — cached to disk on first run
- **Retrieval:** Semantic similarity (cosine) over nomic-embed-text vectors; top-k chunks per section
- **Section matching:** Uploaded section titles matched to 15 canonical SoW sections by embedding similarity
- **LLM inference:** Microsoft Foundry Local (local) or Azure AI Foundry (cloud); one call per section sequentially
- **Definition of good:** Generated once at startup from the corpus (cached to disk); covers deliverables, milestones/acceptance criteria, payment terms, IP ownership, scope boundaries, risk/change control
- **Multi-user:** 2–5 concurrent users; definition is a shared singleton; each circuit gets its own short-lived DbContext instances
