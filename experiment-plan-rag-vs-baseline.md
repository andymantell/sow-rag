# Experiment: RAG vs Baseline for SoW Improvement

## Objective

Determine whether RAG-augmented LLM improvement of Statement of Work documents produces measurably better results than baseline (prompt-only) LLM improvement, given the current corpus.

Secondary objective: assess whether the corpus documents are sufficient to make RAG worthwhile, or whether the LLM's pretraining knowledge already covers what the corpus provides.

## Hypothesis

The current corpus (4 redacted SoWs from large consultancies) may not provide enough novel, domain-specific information to justify RAG. Redaction strips the concrete detail that RAG is best at surfacing, leaving generic structural prose the LLM already knows. Expected signal: RAG quality ≈ baseline quality, with elevated noise sensitivity.

## Corpus Under Test

Located in `sample-sows/`. List the PDFs, their sizes, and characteristics (source organisations, redaction status, count) at execution time — the corpus may change between runs.

## Test Documents

**TODO**: Specify the folder of SoW PDFs to evaluate. These must be documents *not* in the corpus — otherwise we're testing the system on its own training data.

## Setup

1. **Delete the SQLite database** for a clean slate:
   ```
   del SoWImprover\sow-improver.db
   ```

2. **Keep existing corpus caches** (embeddings, definitions, redactions) — they represent the current system state and avoid expensive recomputation. Delete them only if the corpus has changed.

3. **Ensure evaluation is enabled** in `appsettings.json`:
   ```json
   "FeatureManagement": {
     "Evaluation": true
   }
   ```

4. **Confirm local LLM is running** — check `foundry service status` or equivalent.

## Execution

For each test PDF:

1. Upload via the app UI (Home page)
2. Wait for improvement to complete — the app runs:
   - **Baseline pass**: same improvement prompt, no retrieved chunks
   - **RAG pass**: same prompt with top-5 retrieved chunks injected
3. Wait for Ragas evaluation to complete (12 metrics × all matched sections)
4. Evaluation summary is generated and persisted automatically
5. Note the document ID from the URL (`/results/{id}`) for data collection

## Metrics Collected

### Per-section (12 metrics)

| Metric | Range | Applies to | Notes |
|--------|-------|-----------|-------|
| Original Quality | 1–5 | Original | Baseline quality of the input |
| Baseline Quality | 1–5 | Baseline | Prompt-only improvement |
| RAG Quality | 1–5 | RAG | RAG-enhanced improvement |
| Baseline Faithfulness | 0–1 | Baseline | Did it stay true to original? |
| RAG Faithfulness | 0–1 | RAG | Did it stay true to original? |
| Baseline Factual Correctness | 0–1 | Baseline | F1 score for fact preservation |
| RAG Factual Correctness | 0–1 | RAG | F1 score for fact preservation |
| Baseline Response Relevancy | 0–1 | Baseline | Did it stay on-task? |
| RAG Response Relevancy | 0–1 | RAG | Did it stay on-task? |
| Context Precision | 0–1 | RAG only | Were retrieved chunks relevant? |
| Context Recall | 0–1 | RAG only | Did retrieval find all useful material? |
| Noise Sensitivity | 0–1 | RAG only | Did irrelevant chunks harm output? **Lower is better** |

### Per-document
- LLM-generated evaluation summary (markdown, stored in `DocumentEntity.EvaluationSummary`)

## Data Collection

After all documents are processed, extract from SQLite (`sow-improver.db`):

```sql
-- Per-section scores
SELECT d.FileName, s.SectionName,
       s.OriginalQuality, s.BaselineQuality, s.RagQuality,
       s.BaselineFaithfulness, s.RagFaithfulness,
       s.BaselineFactualCorrectness, s.RagFactualCorrectness,
       s.BaselineResponseRelevancy, s.RagResponseRelevancy,
       s.ContextPrecision, s.ContextRecall, s.NoiseSensitivity
FROM SectionEntities s
JOIN DocumentEntities d ON s.DocumentEntityId = d.Id
ORDER BY d.FileName, s.SectionName;

-- Per-document summaries
SELECT FileName, EvaluationSummary FROM DocumentEntities;
```

## Analysis Plan

### 1. Aggregate RAG vs Baseline Comparison

For each paired metric (quality, faithfulness, factual correctness, relevancy):
- Compute mean, median, and standard deviation across all sections
- Count sections where RAG > baseline, RAG = baseline, RAG < baseline
- Statistical significance if sample size permits (paired t-test or Wilcoxon signed-rank)

Key question: **Does RAG consistently improve quality, or is the effect within noise?**

### 2. RAG-Specific Metrics

Across all sections:
- **Context Precision**: Are the retrieved chunks actually relevant to the section being improved? Low precision = retriever returning off-topic material.
- **Context Recall**: Is the retriever finding the useful corpus material? Low recall = good material exists but isn't being surfaced.
- **Noise Sensitivity**: Are irrelevant chunks actively harming the output? High values = the LLM is being distracted by bad context.

Key question: **Is the retrieval pipeline finding good material, or is it injecting noise?**

### 3. Per-Section Breakdown

Group results by canonical section name. Hypotheses:
- **Structural/boilerplate sections** (Change Control, Termination, Warranties) — RAG unlikely to help; LLM already knows these patterns.
- **Content-heavy sections** (Scope of Work, Project Requirements, Deliverables) — RAG *might* help if corpus has relevant domain examples. But redaction may have stripped the useful detail.
- **Process sections** (Risk Management, Project Management) — could go either way depending on corpus specificity.

Key question: **Which section types benefit from RAG, if any?**

### 4. Corpus Sufficiency Assessment

Qualitative analysis:
- What do the 4 corpus documents actually provide after redaction?
- How much vocabulary diversity exists across the corpus?
- Do the chunks contain actionable, specific content — or generic prose?
- How well do the 15 canonical sections map to the corpus documents' actual structure?
- What percentage of retrieved chunks score above the 0.3 similarity threshold?

Key question: **Does the corpus contain information the LLM doesn't already know?**

### 5. Retrieval Quality Deep-Dive

From the application logs (check chunk scores logged by `SoWImproverService`):
- Distribution of retrieval similarity scores
- How many chunks per section exceed threshold?
- Are the same chunks being retrieved for many different sections? (would indicate low specificity)

### 6. Recommendations

Based on findings, assess:
- **Is RAG worth keeping** for this use case with the current corpus?
- **What would make it worthwhile?** More docs? Unredacted docs? Domain-specific docs? Different chunking? Higher similarity threshold?
- **Alternative approaches** — would few-shot examples in the prompt work better than retrieval? Would a larger/different corpus change the calculus?

## Report Structure

The final report should be a technical internal document covering:

1. **Executive Summary** — 2-3 sentences: does RAG help, and is the corpus sufficient?
2. **Methodology** — experiment setup, metrics, LLM configuration
3. **Results** — aggregate tables, per-section breakdown, statistical tests
4. **Retrieval Analysis** — what the retriever is actually finding and injecting
5. **Corpus Assessment** — what the corpus provides vs what the LLM already knows
6. **Discussion** — interpretation, limitations, confounding factors (e.g. local LLM capability ceiling, evaluation by the same LLM that did the improvement)
7. **Recommendations** — concrete next steps

## Known Limitations

- **Evaluator bias**: Ragas evaluation uses the same local LLM that performed the improvement. The evaluator may systematically favour its own output style.
- **Small sample size**: With few test documents and 15 sections each, statistical power is limited.
- **Redaction artefacts**: Redaction may introduce noise in both the corpus chunks and the evaluation scores.
- **Local LLM capability ceiling**: A small local model may not be capable enough for the evaluation task itself to be reliable. Consider re-running with a cloud model for validation.
- **Single-run variance**: Local LLM output is non-deterministic. A single run per document doesn't capture variance. Multiple runs would improve confidence but multiply execution time.
