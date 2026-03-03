# SoW Improver — CLAUDE.md

## Project Overview
A locally-hosted ASP.NET Core web application that helps small teams 
improve Statements of Work (SoW) documents using RAG and a local LLM. 
It analyses a corpus of known-good SoW PDFs to derive a reusable 
"definition of good", then uses that definition to generate an improved 
version of any uploaded SoW — presenting a side-by-side diff view in 
the browser.

All AI inference runs via Microsoft Foundry Local (on-device), but the 
model client must be configurable to point at Azure AI Foundry cloud 
with no code changes — only config changes.

## Tech Stack
- **Backend:** ASP.NET Core 8, Minimal API
- **Frontend:** Vanilla JS + HTML/CSS, single index.html, no build step,
  no frameworks
- **PDF extraction:** PdfPig
- **Foundry Local:** Microsoft.AI.Foundry.Local NuGet package —
  use FoundryLocalManager to discover dynamic local endpoint at runtime
- **Model client:** `OpenAI` NuGet (v2.x) for local (OpenAI-compatible API);
  `Azure.AI.OpenAI` NuGet for cloud (`AzureOpenAIClient`). Factory has
  separate code paths for local vs cloud.
- **Markdown rendering:** marked.js (CDN, client-side)
- **Diff rendering:** jsdiff (CDN, client-side) for word-level diffing;
  both sides rendered as markdown via marked.js — no diff2html
- **No database, no vector store, no cloud dependencies by default**

## Model Configuration
The app targets a machine with an RTX 5090. Model name is a config value 
— do not hardcode it. Configure an appropriate model for the 5090's VRAM
(e.g. a quantised Llama 3.3 70B or Phi-4) and explain how to pull it via
Foundry Local CLI.

## Configuration (appsettings.json)
```json
{
  "Foundry": {
    "UseLocal": true,
    "LocalModelName": "phi-4",
    "CloudEndpoint": "",
    "CloudApiKey": "",
    "CloudModelName": ""
  },
  "Docs": {
    "KnownGoodFolder": "./sample-sows",
    "ChunkSize": 500,
    "ChunkOverlap": 50,
    "TopKChunks": 5
  }
}
```

When UseLocal is true, use FoundryLocalManager to resolve the endpoint.
When false, use CloudEndpoint + CloudApiKey directly with the OpenAI
SDK — no other code changes required.

If UseLocal is true and Foundry Local is not running at startup, throw
an exception and let the process crash. In a container deployment this
triggers a restart loop, which is the desired behaviour — the container
will not become healthy until Foundry is available.

If the KnownGoodFolder contains no PDF files at startup, throw an
exception with a clear message ("No PDFs found in KnownGoodFolder —
populate the folder before starting"). Same crash behaviour.

If an uploaded PDF yields no extractable text (scanned/image-based),
return HTTP 400: "No text could be extracted — PDF may be scanned or
image-based."

Upload size limit: 28 MB (ASP.NET Core default — no extra config needed).

## Project Structure
```
SoWImprover/
├── Program.cs                  # DI registration, minimal API endpoints
├── appsettings.json
├── Services/
│   ├── DocumentLoader.cs       # PDF + text ingestion, chunking
│   ├── SimpleRetriever.cs      # TF-IDF in-memory retrieval, top-k chunks
│   ├── DefinitionBuilder.cs    # Generates "definition of good" from corpus
│   ├── SoWImprover.cs          # Generates improved SoW using RAG
│   ├── FoundryClientFactory.cs # Resolves local vs cloud endpoint,
│   │                           # returns configured OpenAI client
│   └── DiffService.cs          # Normalises/prepares original + improved text
│                               # for the API response (diff computed client-side)
├── Models/
│   ├── DocumentChunk.cs
│   ├── GoodDefinition.cs       # Cached definition of good
│   └── ImprovementResult.cs    # Original + improved + flagged sections
├── wwwroot/
│   └── index.html              # Entire frontend — diff view,
│                               # definition panel, upload form
└── sample-sows/                # Drop real SoW PDFs here before running;
                                # folder ships with a .gitkeep only
```

## Startup Behaviour
On startup the app must:
1. Load and chunk all PDFs in the KnownGoodFolder
2. Generate the "definition of good" using the model and cache it in
   memory as a singleton (GoodDefinition). Strategy: one LLM call per
   document to extract what makes it a good SoW across these specific
   aspects: clarity of deliverables, milestone/acceptance criteria,
   payment terms, IP ownership, scope boundaries, risk/change control.
   Then a single synthesis call combines the per-doc summaries into the
   final definition. This scales as the corpus grows. (Future: trigger
   regeneration on demand — out of scope for this PoC.)
3. Log how many documents and chunks were loaded
4. Be ready to serve requests — do not accept uploads until the 
   definition is ready; return a 503 with a loading message if called early

## API Endpoints
- GET  /api/status         — returns: model name, doc count, chunk count,
                             whether definition is ready, local vs cloud mode
- GET  /api/definition     — returns the cached definition of good 
                             as markdown
- POST /api/improve        — accepts multipart PDF upload, returns
                             ImprovementResult (original text, improved
                             markdown, flagged sections, chunks used)
                             as a single JSON response

## ImprovementResult Shape
```json
{
  "original": "raw extracted text from uploaded PDF",
  "improved": "generated markdown improvement",
  "flaggedSections": [
    {
      "sectionTitle": "Deliverables",
      "reason": "Structure differs significantly from good definition"
    }
  ],
  "chunksUsed": [
    { "sourceFile": "sow-example-1.pdf", "snippet": "..." }
  ]
}
```

## Improvement Behaviour
- Preserve the original document's section structure by default
- Improve content within each section to align with the definition of good
- Flag (do not rewrite) any sections where structure itself should change,
  with a reason — surface these as warnings in the UI
- Return the full result as a single JSON response; frontend shows a
  loading spinner while waiting
- Content before the first detected heading is treated as an implicit
  "Introduction" section and processed like any other section
- Each per-section LLM call returns structured JSON:
  `{ "improved": "...", "flagged": true/false, "flagReason": "..." }`
  Parse server-side; the `improved` field is used as-is if `flagged` is
  true (section is surfaced as a warning rather than rewritten differently)
- `chunksUsed` in the response is deduplicated — each corpus chunk appears
  once even if retrieved for multiple sections

## Frontend (index.html)
Single page with three panels:

**1. Definition Panel (left sidebar)**
- Renders the definition of good as markdown using marked.js
- Visible at all times for reference
- Shows a loading spinner until /api/status reports definition is ready

**2. Upload Panel (top)**
- Simple PDF file input + "Improve" button
- Loading spinner while waiting for the response
- Shows flagged sections as yellow warning badges once complete

**3. Diff Panel (main area)**
- Side-by-side view: left = original rendered as markdown, right = improved
  rendered as markdown (both via marked.js)
- Word-level diffs highlighted using jsdiff (CDN): green for additions,
  red/strikethrough for removals — achieved by annotating the markdown
  strings with `<ins>`/`<del>` spans before rendering
- Flagged sections shown as yellow warning badges above the diff panel
  (not overlaid on the diff itself)
- Chunks used shown as a collapsible "Sources" section below the diff

## Multi-User Considerations
- Supports 2-5 concurrent users
- The GoodDefinition is a shared singleton (generated once on startup)
- Each /api/improve request is stateless and independent — no session state
- No authentication required

## Retrieval Approach
Use TF-IDF in-memory similarity search (no vector DB). SimpleRetriever
returns top-k chunks from the known-good corpus most relevant to each
section of the uploaded document. Retrieval is per-section, not
per-document, so each section gets its own grounded context.

The improvement LLM call is also per-section: each section is improved
independently with its own retrieved chunks, then sections are reassembled
into the final document. Calls are made sequentially (no parallelism),
with no cap on section count.

Sections in the uploaded document are identified by heading detection:
lines that are Markdown headings (starting with `#`) or all-caps lines
are treated as section boundaries.

## Constraints
- No external CSS frameworks
- No npm, no build step, no node_modules
- No database or file persistence — everything is in-memory, 
  ephemeral per server restart
- No cloud calls unless UseLocal is false in config

## Build Order
Implement in this order:
1. DocumentLoader + SimpleRetriever (verify chunking and retrieval first)
2. FoundryClientFactory (verify local and cloud config switching works)
3. DefinitionBuilder (verify definition generation against sample docs)
4. SoWImprover (verify improvement generation — single JSON response)
5. DiffService (verify before/after preparation)
6. API endpoints in Program.cs
7. index.html frontend

## README
Must include:
- Prerequisites (Foundry Local install, model download command)
- Model recommendation for RTX 5090 with explanation
- How to populate the known-good SoW folder
- How to switch to Azure AI Foundry cloud (config only)
- How to run with dotnet run

## Decisions (locked in)
- `/api/improve` returns a **single JSON response** (no SSE streaming)
- **No chat UI** — three panels only: definition sidebar, upload panel, diff panel
- **Sample SoWs** — user supplies real PDFs; `sample-sows/` ships with `.gitkeep` only
- **Project root** — `C:\goaco\AI\SOW-RAG\SoWImprover\` (subfolder of the repo)
- **Diff rendering** — no diff2html; both sides rendered with marked.js; word-level
  diffs via jsdiff CDN, annotated with `<ins>`/`<del>` spans before rendering
- **LLM improvement strategy** — one call per section, sequential, no section cap
- **Azure cloud client** — use `AzureOpenAIClient` from `Azure.AI.OpenAI` NuGet
  (separate code path in factory); local uses `OpenAIClient` from `OpenAI` NuGet
- **ChunkSize units** — words (not characters); ChunkOverlap likewise in words
- **Empty corpus** — crash at startup with clear error if no PDFs found
- **Scanned PDFs** — return HTTP 400 if no text extracted from upload
- **Upload size limit** — 28 MB (ASP.NET Core default, no extra config)
- **Per-section LLM response format** — structured JSON `{ "improved", "flagged", "flagReason" }`
- **Preamble** — content before first heading → implicit "Introduction" section
- **Definition focus** — deliverables, milestones/acceptance criteria, payment terms,
  IP ownership, scope boundaries, risk/change control
- **chunksUsed deduplication** — deduplicated; each chunk appears once in the list
- **Snippet length** in chunksUsed — truncate to 200 characters
- **`/api/definition` when not ready** — also returns 503 (same as `/api/improve`)
- **Frontend status polling** — poll `/api/status` every 2 seconds until ready

## Current Task
_Update this section each session to describe what to work on next._
Start with step 1: DocumentLoader and SimpleRetriever.