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

> **Startup time:** On first run the app embeds all corpus chunks via Ollama (cached to `sample-sows/embeddings-cache.json` for subsequent runs), then makes one LLM call per document plus a synthesis call to build the definition of good. With phi-4 and 4 documents this takes 2–5 minutes.

> **Improvement time:** One LLM call per section sequentially. A 10-section SoW may take 5–15 minutes.

### Clearing the embedding cache

If you add or remove PDFs from `sample-sows/`, delete the cache so it regenerates:

```bash
del SoWImprover\sample-sows\embeddings-cache.json
```

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

- **Frontend:** Blazor Server (interactive components, no JS framework)
- **PDF extraction:** Python subprocess using `pymupdf4llm` — produces clean markdown from PDFs
- **Embeddings:** nomic-embed-text via Ollama (local) or Azure OpenAI (cloud) — cached to disk on first run
- **Retrieval:** Semantic similarity (cosine) over nomic-embed-text vectors; top-k chunks per section
- **Section matching:** Uploaded section titles matched to 15 canonical SoW sections by embedding similarity
- **LLM inference:** Microsoft Foundry Local (local) or Azure AI Foundry (cloud); one call per section sequentially
- **Definition of good:** Generated once at startup from the corpus; covers deliverables, milestones/acceptance criteria, payment terms, IP ownership, scope boundaries, risk/change control
- **No database or vector store** — all state is in-memory; restart clears it (embedding cache persists on disk)
- **Multi-user:** 2–5 concurrent users; definition is a shared singleton; each improvement request is stateless
