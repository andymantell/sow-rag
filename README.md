# SoW Improver

An experiment framework for evaluating whether RAG (Retrieval-Augmented Generation) improves LLM-based rewriting of Statement of Work documents. A batch runner processes test SoW PDFs through both a baseline (prompt-only) and a RAG-augmented pipeline, runs Ragas evaluation metrics, and exports structured results for analysis.

A Blazor Server web UI is also included for interactive exploration — uploading individual SoWs and inspecting the section-by-section diffs, editing results, and exporting PDFs.

---

## Prerequisites

### 1. .NET 8 SDK
Download from https://dotnet.microsoft.com/download/dotnet/8

### 2. Python 3 + packages

PDF text extraction and evaluation run via Python subprocesses:

```bash
pip install pymupdf4llm pymupdf-layout ragas
```

### 3. Ollama (for local LLM inference and embeddings)

Install Ollama from https://ollama.com, then pull the required models:

```bash
ollama pull qwen3.5:27b
ollama pull nomic-embed-text
```

Ollama must be running at `http://localhost:11434` before starting. A single **Qwen3.5-27B** model handles both SoW improvement and Ragas evaluation; **nomic-embed-text** provides embeddings.

#### VRAM requirements

| Model | VRAM (Q4) | Role |
|---|---|---|
| **qwen3.5:27b** | ~17 GB | Chat (improvement + evaluation) |
| **nomic-embed-text** | ~0.3 GB | Embeddings |

Both models fit comfortably on a 32 GB GPU (e.g. RTX 5090) with headroom for context.

---

## Setup

### 1. Populate the known-good SoW corpus

Drop 2-10 representative SoW PDFs into the `SoWImprover/sample-sows/` folder. At least one PDF is required. PDFs must be text-based (not scanned/image-only).

### 2. Prepare test documents

Place the SoW PDFs you want to evaluate in a `test-sows/` folder (or any folder of your choice). These must be documents *not* in the corpus — otherwise you're testing the system on its own training data.

### 3. Review configuration

`SoWImprover/appsettings.json` defaults:

```json
{
  "Foundry": {
    "UseLocal": true,
    "CloudEndpoint": "",
    "CloudApiKey": "",
    "CloudModelName": "",
    "CloudEmbeddingDeployment": ""
  },
  "Ollama": {
    "Endpoint": "http://localhost:11434/v1",
    "ChatModelName": "qwen3.5:27b",
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

## Running the Experiment

The primary workflow: process test SoWs through both pipelines, evaluate with Ragas, then generate an analysis report.

### Step 1: Run the batch evaluation

```bash
make experiment
```

This deletes the SQLite database for a clean run, then processes every PDF in the test folder through baseline and RAG improvement, runs Ragas evaluation (12 metrics per section), and exports results to `experiment-results.json`.

By default the test folder is `test-sows/`. To use a different folder:

```bash
make experiment TEST_FOLDER=path/to/pdfs
```

Without `make`:

```bash
dotnet run --project SoWImprover.BatchRunner -- --clean
dotnet run --project SoWImprover.BatchRunner -- path/to/test-pdfs
```

Expect ~15-30 minutes per document with local models. Do not run the web UI at the same time (SQLite concurrency).

### Step 2: Generate the analysis report

```bash
claude /experiment-report
```

Reads the exported results and corpus documents, then writes a technical analysis report comparing RAG vs baseline performance. Saved to `reports/experiment-report-YYYY-MM-DD.md`.

> **Startup time:** On first run the system embeds all corpus chunks via Ollama (cached to `sample-sows/embeddings-cache.json`) and builds the definition of good via LLM (cached to `sample-sows/definition-cache.json`). With 4 documents this takes 2-5 minutes. Subsequent runs load from cache in seconds.

### Clearing caches

If you add or remove PDFs from `sample-sows/`, the caches are automatically invalidated (fingerprint mismatch). To force regeneration manually, delete `embeddings-cache.json`, `definition-cache.json`, and/or `redactions-cache.json` from the `sample-sows/` folder.

---

## Interactive Web UI

For hands-on exploration of individual documents, the Blazor Server app provides a browser-based interface.

```bash
cd SoWImprover
dotnet run
```

Open your browser at `http://localhost:5194` (or the URL shown in the console).

### Features

- **Upload & improve** — upload a SoW PDF and get an AI-improved version with section-by-section rewrites
- **Side-by-side diff** — original and improved content shown side by side with "what changed" explanations
- **Inline editing** — edit improved sections with a WYSIWYG markdown editor (bold, italic, headings, lists, tables)
- **Version history** — browse and restore previous versions of each section via inline version pills
- **Section suppression** — exclude individual sections from the output with one click
- **PDF export** — download the improved document as a formatted PDF (respects suppressed sections)
- **Document history** — previous uploads are persisted and accessible from the home page
- **Definition of good** — sidebar shows the auto-generated quality standards used for improvement

---

## Testing

### Unit & integration tests

```bash
make test
```

Or without `make`:

```bash
dotnet test SoWImprover.Tests/SoWImprover.Tests.csproj
```

These test services, models, and Blazor components in isolation using NSubstitute stubs and bUnit. No external dependencies (LLM, Ollama, Python, browser) are required.

### End-to-end browser tests

```bash
make e2e-test          # headless
make e2e-test-headed   # visible browser
```

Or without `make`:

```bash
dotnet build SoWImprover.E2E/SoWImprover.E2E.csproj
node SoWImprover.E2E/bin/Debug/net8.0/.playwright/package/cli.js install --with-deps chromium
dotnet test SoWImprover.E2E/SoWImprover.E2E.csproj
```

### Make targets summary

| Target | Description |
|---|---|
| `make experiment` | Run the full RAG vs baseline experiment (clean DB + batch runner) |
| `make test` | Unit & integration tests |
| `make e2e-test` | End-to-end browser tests (headless) |
| `make e2e-test-headed` | End-to-end browser tests (visible browser) |

---

## Database

The app uses **SQLite** via Entity Framework Core. The database file `sow-improver.db` is created automatically on first run.

| Entity | Purpose |
|---|---|
| `DocumentEntity` | Uploaded document metadata (filename, original text, upload timestamp) |
| `SectionEntity` | Per-section results (original, improved, explanation, suppressed flag) |
| `SectionVersionEntity` | Append-only edit history per section (version number, content, timestamp) |

Currently uses `EnsureCreated()` at startup (suitable for PoC). For production, switch to EF Core migrations.

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

- **Batch runner:** Console app (`SoWImprover.BatchRunner`) — orchestrates corpus init, document processing, Ragas evaluation, and JSON export
- **Web UI:** Blazor Server — interactive upload, diff view, inline editing, PDF export
- **PDF extraction:** Python subprocess using `pymupdf4llm` — produces clean markdown from PDFs
- **PDF export:** QuestPDF (Community license) — server-side PDF generation with markdown table rendering
- **Persistence:** SQLite via EF Core `IDbContextFactory`
- **LLM inference:** Qwen3.5-27B via Ollama (local) or Azure AI Foundry (cloud); single model handles both improvement and evaluation
- **Embeddings:** nomic-embed-text via Ollama (local) or Azure OpenAI (cloud) — cached to disk on first run
- **Retrieval:** Semantic similarity (cosine) over nomic-embed-text vectors; top-k chunks per section
- **Section matching:** Uploaded section titles matched to 15 canonical SoW sections by embedding similarity
- **Definition of good:** Generated once from the corpus (cached to disk); covers deliverables, milestones/acceptance criteria, payment terms, IP ownership, scope boundaries, risk/change control
