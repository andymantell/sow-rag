# SoW Improver

A locally-hosted ASP.NET Core 8 web application that uses RAG and a local LLM to improve Statements of Work (SoW) documents. It analyses a corpus of known-good SoW PDFs to derive a reusable "definition of good", then uses that to generate an improved version of any uploaded SoW — presented as a side-by-side diff view in the browser.

---

## Prerequisites

### 1. .NET 8 SDK
Download from https://dotnet.microsoft.com/download/dotnet/8

### 2. Microsoft Foundry Local

Install the Foundry Local CLI:

```powershell
winget install Microsoft.FoundryLocal
```

Or download from: https://aka.ms/foundry-local

### 3. Download a model

The app is configured to use **phi-4** by default. Download it with:

```bash
foundry model download phi-4
```

#### Model recommendation for RTX 5090

The RTX 5090 has 32 GB of VRAM. Recommended options:

| Model | VRAM (Q4) | Notes |
|---|---|---|
| **phi-4** (default) | ~8 GB | Fast, excellent reasoning, fits comfortably |
| **llama3.3-70b-instruct** | ~40 GB Q4 (too large solo) | Requires `--device cpu` or split |
| **mistral-small-3.1-24b** | ~14 GB Q4 | Good balance of quality and speed |
| **qwen2.5-72b-instruct** | ~41 GB Q4 | Exceeds VRAM solo |

**Recommendation:** Start with `phi-4` — it fits easily in 32 GB, starts fast, and produces high-quality structured output. If you need stronger reasoning, try `mistral-small-3.1-24b`:

```bash
foundry model download mistral-small-3.1-24b
```

Then update `appsettings.json`:
```json
"LocalModelName": "mistral-small-3.1-24b"
```

---

## Setup

### 1. Populate the known-good SoW corpus

Drop 2–10 representative SoW PDFs into the `sample-sows/` folder:

```
SoWImprover/
└── sample-sows/
    ├── sow-example-1.pdf
    ├── sow-example-2.pdf
    └── ...
```

The app will not start without at least one PDF in this folder.

> **Note:** The PDFs must be text-based (not scanned/image-only). PdfPig is used for text extraction; OCR is not supported.

### 2. Configure the model (optional)

Edit `SoWImprover/appsettings.json` to change the model or folder:

```json
{
  "Foundry": {
    "UseLocal": true,
    "LocalModelName": "phi-4"
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

Open your browser at `http://localhost:5000`.

> **Startup time:** On first run the app makes one LLM call per document in your corpus, then a synthesis call. With phi-4 and 3 documents this typically takes 1–3 minutes. The status badge in the header shows progress. `/api/improve` returns 503 until the definition is ready.

> **Improvement time:** Improving a document makes one LLM call per detected section sequentially. A 10-section SoW may take 5–15 minutes depending on the model. The browser shows a spinner while waiting.

---

## Switching to Azure AI Foundry Cloud

No code changes are required — only configuration:

```json
{
  "Foundry": {
    "UseLocal": false,
    "CloudEndpoint": "https://<your-resource>.services.ai.azure.com/",
    "CloudApiKey": "<your-api-key>",
    "CloudModelName": "gpt-4o"
  }
}
```

The app uses `AzureOpenAIClient` when `UseLocal` is `false`. The `CloudEndpoint` must be an Azure AI Foundry (or Azure OpenAI) endpoint.

For production deployments, use environment variables or Azure Key Vault rather than placing secrets in `appsettings.json`:

```bash
export Foundry__CloudApiKey="your-key"
```

---

## Architecture notes

- **No database or vector store** — all chunks are held in memory; restart clears them
- **TF-IDF retrieval** — per-section retrieval from the corpus; top-k chunks passed as context for each section's LLM call
- **Section detection** — all-caps lines and Markdown `#` headings treated as section boundaries; content before the first heading becomes an "Introduction" section
- **Definition of good** — generated once at startup from the corpus; evaluated across: clarity of deliverables, milestone/acceptance criteria, payment terms, IP ownership, scope boundaries, risk and change control
- **Multi-user** — 2–5 concurrent users supported; definition is a shared singleton; each `/api/improve` request is stateless
