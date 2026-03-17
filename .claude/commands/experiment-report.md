Read the experiment results and write a technical analysis report comparing RAG vs baseline SoW improvement.

## Steps

1. Read `experiment-results.json` in the repo root. If not found, tell the user to run the batch runner first:
   `dotnet run --project SoWImprover.BatchRunner -- "<test-folder-path>"`

2. Read `experiment-plan-rag-vs-baseline.md` for the analysis framework and report structure.

3. Read each corpus PDF in `SoWImprover/sample-sows/` — extract via `python SoWImprover/pdf_to_markdown.py <path>` and read the output. Form your own independent judgement on each document's quality, specificity, vocabulary, and appropriateness as RAG source material. Consider: what does this document actually contain after redaction? Is the content specific enough to add value beyond what an LLM already knows? How well does it cover the 15 canonical sections?

4. Analyse the quantitative results from the JSON export following the analysis plan sections:
   - Aggregate RAG vs baseline comparison (quality, faithfulness, factual correctness, relevancy)
   - RAG-specific metrics (context precision, recall, noise sensitivity — remember 0.00 noise is GOOD, 1.00 is BAD)
   - Per-section breakdown by canonical section type
   - Retrieval quality (similarity score distributions, chunk relevance)

5. Do qualitative analysis — read the actual original, baseline, and RAG content for representative sections. What changed? Was the RAG version genuinely better, or just different?

6. Write the report following this structure:
   - **Executive Summary** — 2-3 sentences: does RAG help, is the corpus sufficient?
   - **Methodology** — experiment setup, metrics, LLM configuration, corpus description
   - **Results** — aggregate comparison tables, per-section breakdown, statistical tests where sample size permits
   - **Retrieval Analysis** — what the retriever found, similarity score distributions, chunk relevance
   - **Corpus Assessment** — your independent qualitative review of each corpus document and its RAG suitability
   - **Discussion** — interpretation, limitations (evaluator bias, sample size, local LLM ceiling, single-run variance)
   - **Recommendations** — concrete next steps
7. Save to `experiment-report-YYYY-MM-DD.md` at repo root (use today's date).

## Tone

Frank, technical, internal report. No sugar-coating. Ground every claim in specific data from the results. If RAG isn't helping, say so directly and explain why.
