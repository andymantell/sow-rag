Read the experiment results and write a technical analysis report comparing RAG vs baseline SoW improvement.

## Steps

1. Read `experiment-results.json` in the repo root. If not found, tell the user to run the batch runner first:
   `dotnet run --project SoWImprover.BatchRunner -- "<test-folder-path>"`

2. Run `python analyse_experiment.py` to generate the quantitative analysis tables (aggregates, head-to-head, per-section breakdown, retrieval stats, qualitative samples). Use this output as the primary data source for the report.

3. Read `experiment-plan-rag-vs-baseline.md` for the analysis framework and report structure.

4. Read each corpus PDF in `SoWImprover/sample-sows/` — extract via `python SoWImprover/pdf_to_markdown.py <path>` and read the output. Form your own independent judgement on each document's quality, specificity, vocabulary, and appropriateness as RAG source material. Consider: what does this document actually contain after redaction? Is the content specific enough to add value beyond what an LLM already knows? How well does it cover the 15 canonical sections?

5. Do qualitative analysis — read the actual original, baseline, and RAG content from the qualitative samples in step 2. What changed? Was the RAG version genuinely better, or just different?

6. **Compare with previous report.** Check `reports/` for the most recent prior `experiment-report-*.md`. If one exists:
   - Read it in full
   - Compare: what changed between runs? Look at aggregate metrics, head-to-head win/loss counts, retrieval quality, corpus composition, and recommendations
   - Note improvements, regressions, and things that stayed the same
   - If the methodology changed (different model, more test docs, different corpus), call that out explicitly
   - If no previous report exists, skip this section

7. Write the report following this structure:
   - **Executive Summary** — 2-3 sentences: does RAG help, is the corpus sufficient?
   - **Changes Since Last Report** — (only if a previous report exists) a focused comparison: what moved, what didn't, and why. Include a side-by-side table of key aggregate metrics (previous vs current). Call out any methodology changes that make direct comparison invalid.
   - **Methodology** — experiment setup, metrics, LLM configuration, corpus description
   - **Results** — aggregate comparison tables, per-section breakdown, statistical tests where sample size permits
   - **Retrieval Analysis** — what the retriever found, similarity score distributions, chunk relevance
   - **Corpus Assessment** — your independent qualitative review of each corpus document and its RAG suitability
   - **Discussion** — interpretation, limitations (evaluator bias, sample size, local LLM ceiling, single-run variance)
   - **Recommendations** — concrete next steps (flag which recommendations from the previous report were addressed, if any)

8. Save to `reports/experiment-report-YYYY-MM-DD.md` (use today's date). If a report with today's date already exists, append an incrementing number: `experiment-report-YYYY-MM-DD-2.md`, `experiment-report-YYYY-MM-DD-3.md`, etc. Never overwrite a previous report.

## Tone

Frank, technical, internal report. No sugar-coating. Ground every claim in specific data from the results. If RAG isn't helping, say so directly and explain why.
