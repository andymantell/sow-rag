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
- **Model client:** OpenAI .NET SDK (v2.x) — Foundry Local exposes an
  OpenAI-compatible API. The same client must work against Azure AI 
  Foundry cloud by swapping endpoint/key in config
- **Markdown rendering:** marked.js (CDN, client-side)
- **Diff rendering:** diff2html.js (CDN, client-side) for the side-by-side
  HTML diff view
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
│   └── DiffService.cs          # Prepares before/after content for diff
├── Models/
│   ├── DocumentChunk.cs
│   ├── GoodDefinition.cs       # Cached definition of good
│   └── ImprovementResult.cs    # Original + improved + flagged sections
├── wwwroot/
│   └── index.html              # Entire frontend — chat UI, diff view,
│                               # definition panel, upload form
└── sample-sows/                # 3-4 example SoW PDF files for
                                # out-of-the-box testing
```

## Startup Behaviour
On startup the app must:
1. Load and chunk all PDFs in the KnownGoodFolder
2. Generate the "definition of good" using the model and cache it in
   memory as a singleton (GoodDefinition). Strategy: one LLM call per
   document to extract what makes it a good SoW, then a single synthesis
   call to combine those per-doc summaries into the final definition.
   This scales as the corpus grows. (Future: trigger regeneration on
   demand rather than only at startup — out of scope for this PoC.)
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
- Side-by-side view using diff2html.js
- Left: original document text
- Right: improved markdown (rendered)
- Word-level diffs highlighted (green additions, red removals)
- Flagged sections additionally highlighted with a yellow border/badge
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
4. SoWImprover (verify improvement generation with SSE streaming)
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

## Current Task
_Update this section each session to describe what to work on next._
Start with step 1: DocumentLoader and SimpleRetriever.