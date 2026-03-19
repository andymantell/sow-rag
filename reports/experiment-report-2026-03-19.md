# Experiment Report: RAG vs Baseline for SoW Improvement

**Date:** 2026-03-19
**Status:** Internal technical report — single-run results, 5 test documents

---

## Executive Summary

RAG provides no measurable quality improvement over prompt-only baseline. Mean quality scores are identical (1.60 vs 1.60), and head-to-head RAG wins on quality in only 10 of 60 sections (16%) — the same rate as RAG losses. Retrieval quality remains critically poor: context precision averages 0.05 and context recall 0.07, meaning the retriever finds relevant material for fewer than 1 in 10 sections. The corpus of 4 redacted HMRC SoWs (27 chunks) is too small and too homogeneous to support meaningful retrieval for general SoW improvement.

---

## Changes Since Last Report

### Methodology Changes

| Parameter | Previous (2026-03-17) | Current (2026-03-19) |
|-----------|----------------------|---------------------|
| Chat model | phi-4 (14B, Foundry Local) | qwen3.5:27b (27B, Ollama) |
| Evaluation model | qwen2.5:14b (separate) | qwen3.5:27b (same model) |
| Test documents | 2 synthetic SoWs | 5 synthetic SoWs |
| Sections evaluated | 20 | 60 |
| Infrastructure | Foundry Local + Ollama (2 models, GPU swapping) | Ollama only (single model) |
| Max improvement tokens | 2048 | 4096 |
| Max definition chars | 2000 | 4000 |
| Thinking mode | N/A (phi-4) | Disabled (reasoning_effort=none) |

**Direct comparison is limited** by the model change: phi-4 and qwen3.5:27b have different capabilities, output styles, and evaluation biases. The 3x increase in test documents provides better statistical coverage.

### Key Metric Comparison

| Metric | Previous | Current | Direction |
|--------|----------|---------|-----------|
| Quality (baseline mean) | 1.80 | 1.60 | Worse |
| Quality (RAG mean) | 1.50 | 1.60 | Better |
| Quality gap (baseline − RAG) | +0.30 | 0.00 | **Closed** |
| Faithfulness (baseline mean) | 0.79 | 0.99 | Better |
| Faithfulness (RAG mean) | 0.89 | 0.96 | Better |
| Factual correctness (baseline) | 0.86 | 0.99 | Better |
| Factual correctness (RAG) | 0.93 | 0.94 | Similar |
| Context precision | 0.095 | 0.05 | Worse |
| Context recall | 0.222 | 0.07 | Worse |
| RAG quality wins (head-to-head) | 1/20 (5%) | 10/60 (16%) | Better |
| RAG quality losses | 7/20 (35%) | 10/60 (16%) | Better |

### What Moved

1. **RAG no longer actively harms quality.** The previous report's most damning finding was that RAG degraded quality in 35% of sections. That's now 16% — matching the win rate. Qwen3.5 appears better at ignoring irrelevant context rather than being constrained by it. The "irrelevant context suppresses expansion" failure mode from the previous report is less pronounced.

2. **Faithfulness dramatically improved across the board.** Both baseline (0.79 → 0.99) and RAG (0.89 → 0.96) faithfulness jumped. This is the qwen3.5 model following the "ONLY use facts from the SECTION TO REWRITE" instruction more reliably than phi-4. The previous report's worst case (faithfulness 0.12, a hallucination) is gone.

3. **Retrieval quality got worse, not better.** Context precision dropped from 0.095 to 0.05, context recall from 0.222 to 0.07. This is surprising — the corpus and embedding model are unchanged. The likely explanation is that qwen3.5 is a stricter evaluator when judging chunk relevance, rating the same generic chunks as less relevant than phi-4 did.

4. **Quality scores are uniformly low.** All 60 original quality scores are 1. Both baseline and RAG mean quality is 1.60. The 1–5 quality scale is barely being used — scores cluster at 1 and 2 with occasional 3s. Either the test documents are genuinely poor, or the evaluator defaults to low scores. This ceiling effect makes it difficult to detect real quality differences.

### Previous Recommendations Addressed

| Recommendation | Status |
|----------------|--------|
| Re-run with cloud LLM evaluator | Not done — still using same local model for both |
| Add more test documents | **Done** — 5 documents (up from 2) |
| Run multiple trials | Not done — still single run |
| Expand corpus to 20+ documents | Not done — still 4 documents |
| Upgrade improvement LLM | **Done** — qwen3.5:27b replaces phi-4 |

---

## 1. Methodology

### Experiment Setup

| Parameter | Value |
|-----------|-------|
| Chat model | qwen3.5:27b (local, Ollama, thinking disabled) |
| Embedding model | nomic-embed-text (local, Ollama) |
| Corpus documents | 4 PDFs, 27 chunks total |
| Test documents | 5 synthetic SoW PDFs |
| Sections evaluated | 60 (of 63 total; 3 unrecognised sections skipped) |
| Retrieval | Top-5 chunks by cosine similarity per section |
| Evaluation | Ragas metrics, same local LLM as evaluator |
| Runs | 1 per document (no repeated trials) |

### Process

For each test document, the system runs two improvement passes per section:
- **Baseline**: same improvement prompt, no retrieved context
- **RAG**: same prompt with top-5 retrieved corpus chunks injected

Then Ragas evaluation scores both outputs against the original across 12 metrics.

### Test Documents

1. **SOW_01_Cyber_Security_Assessment.pdf** — 14 sections, 13 evaluated. DSIT cyber maturity assessment across 12 departments.
2. **SOW_02_HMRC_Digital_Service_Discovery_Alpha.pdf** — 12 sections, 12 evaluated. HMRC Self Assessment service redesign discovery/alpha.
3. **SOW_03_NHS_England_Data_Platform_Migration.pdf** — 13 sections, 12 evaluated. NHS Azure data platform migration.
4. **SOW_04_MoJ_HMPPS_Neurodiversity_Training.pdf** — 12 sections, 11 evaluated. MoJ neurodiversity training programme.
5. **SOW_05_FCDO_Strategic_Communications.pdf** — 12 sections, 12 evaluated. FCDO international development communications.

All are synthetic test documents not present in the corpus, spanning cyber security, digital services, data platform, training, and communications domains.

### Corpus Documents

Unchanged from the previous report:

| Document | Size | Supplier | Buyer | Domain |
|----------|------|----------|-------|--------|
| SOW UBS Redacted Good 1.pdf | 327 KB | Accenture (UK) Ltd | HMRC | Unity Programme — HR/Finance |
| SOW Accenure Redacted Good.pdf | 412 KB | Accenture (UK) Ltd | HMRC | Borders & Trade — Discovery |
| Redacted Accenture.pdf | 259 KB | Accenture (UK) Ltd | HMRC | Borders & Trade (variant) |
| Redacted CapGemini.pdf | 363 KB | Capgemini UK Plc | HMRC | ETMP — SAP payment processing |

---

## 2. Results

### 2.1 Aggregate RAG vs Baseline

| Metric | Baseline Mean | RAG Mean | Baseline Median | RAG Median |
|--------|-------------|---------|----------------|-----------|
| **Quality (1–5)** | 1.60 | 1.60 | 2.00 | 2.00 |
| Faithfulness (0–1) | **0.99** | 0.96 | 1.00 | 1.00 |
| Factual Correctness (0–1) | **0.99** | 0.94 | 1.00 | 1.00 |
| Response Relevancy (0–1) | 0.65 | 0.65 | 0.65 | 0.65 |

Quality is identical. Baseline retains a small edge on faithfulness (0.99 vs 0.96) and factual correctness (0.99 vs 0.94) — RAG's injected context occasionally causes minor drift from the original.

### 2.2 Head-to-Head (n=60 evaluated sections)

| Metric | RAG > Baseline | RAG = Baseline | RAG < Baseline |
|--------|---------------|----------------|----------------|
| Quality | 10 (16%) | 40 (66%) | 10 (16%) |
| Faithfulness | 1 (1%) | 52 (86%) | 7 (11%) |
| Factual Correctness | 3 (5%) | 50 (83%) | 7 (11%) |
| Response Relevancy | 15 (25%) | 21 (35%) | 24 (40%) |

Quality is a dead heat: RAG wins and losses are exactly equal (10 each). In 66% of sections, RAG makes no quality difference at all. Faithfulness and factual correctness slightly favour baseline — RAG's context injection introduces minor drift in 11% of sections.

### 2.3 RAG-Specific Metrics

| Metric | Mean | Median |
|--------|------|--------|
| Context Precision | 0.05 | 0.00 |
| Context Recall | 0.07 | 0.00 |
| Noise Sensitivity | 0.00 | 0.00 |

Context precision is non-zero in only 4 of 60 sections. Context recall is non-zero in 10. The retriever is returning material the evaluator considers irrelevant in over 90% of sections. Noise sensitivity is universally 0.00 — the model successfully ignores irrelevant context rather than being harmed by it (a significant improvement over the previous report).

### 2.4 Per Canonical Section Breakdown

| Canonical Section | n | Avg Baseline Q | Avg RAG Q | Notable |
|-------------------|---|---------------|-----------|---------|
| Scope of Work | 8 | 1.38 | 1.50 | Only section type where RAG consistently edges ahead |
| Budget/Payment Terms | 5 | 1.80 | 2.00 | RAG's best section — Commercials consistently score 2-3 |
| Deliverables | 5 | 2.00 | 2.00 | Tied. Highest absolute quality for both approaches |
| Project Timeline/Milestones | 15 | 1.60 | 1.67 | Large sample, near-tied |
| Project Requirements | 12 | 1.58 | 1.50 | Slight baseline advantage |
| Introduction/Background | 5 | 1.60 | 1.60 | Identical |
| Project Management/Reporting | 3 | 2.00 | 1.67 | Baseline better |
| Roles and Responsibilities | 3 | 1.33 | 1.00 | Baseline better |
| Acceptance Criteria | 3 | 1.33 | 1.00 | Baseline better |
| IP and Confidentiality | 1 | 1.00 | 2.00 | Single sample — not meaningful |

Budget/Payment Terms is the one section type where RAG shows a consistent edge. This aligns with the corpus: 3 of 4 documents contain detailed commercials sections with rate tables and payment structures, providing genuinely useful structural examples.

---

## 3. Retrieval Analysis

### 3.1 Similarity Score Distribution

300 retrieved chunks (5 per section × 60 sections):

| Statistic | Value |
|-----------|-------|
| Mean | 0.6442 |
| Median | 0.6443 |
| Min | 0.5530 |
| Max | 0.7624 |
| >= 0.70 | 25/300 (8%) |
| >= 0.60 | 258/300 (86%) |
| >= 0.80 | 0/300 (0%) |

The distribution is nearly identical to the previous report (mean 0.6589 → 0.6442, max 0.754 → 0.762). Scores remain tightly clustered in the 0.55–0.76 band. No chunk ever exceeds 0.77. This confirms the previous finding: the embeddings produce roughly the same similarity score for any chunk against any section — low discriminability.

### 3.2 Chunk Relevance

Despite 86% of chunks scoring above 0.60 on cosine similarity, the evaluator rates context precision at 0.05 (mean). The same "compliance with all laws and regulations" and "Supplier responsibilities" chunks appear as top retrievals across completely different section types — cyber security, data migration, communications, training. This is the same cross-section contamination noted in the previous report, confirming the corpus is too small and generic for discriminative retrieval.

---

## 4. Corpus Assessment

The corpus is unchanged from the previous report. The previous assessment remains valid in full:

- **Too small** — 27 chunks from 4 documents (effectively 3 unique, as two Accenture documents are near-duplicates)
- **Single client** — all HMRC, from Accenture or Capgemini only
- **Domain mismatch** — corpus covers HMRC-specific SAP, HR/Finance, and Borders & Trade; test documents span cyber security, NHS data, MoJ training, FCDO communications
- **Redaction removes value** — the concrete details that would make RAG useful (names, amounts, dates, specifications) have been stripped, leaving generic procurement prose

The one area where corpus material appears to help (Budget/Payment Terms) is also the area where the corpus has the most concrete structural examples: rate tables, SFIA categories, payment milestone formats. This supports the hypothesis that RAG only helps when the corpus provides specific, structured content that the LLM doesn't already know.

---

## 5. Qualitative Analysis

### Case 1: Phase 2 Build and Test — RAG converts list to prose (worse)

**SOW_03 / Phase 2** — Quality: baseline 1, RAG 2. Faithfulness: baseline 1, RAG 0.

The original is a clean bullet list of Azure build tasks. The **baseline** preserved the list format, making only minor wording improvements (articles, expanded abbreviations). The **RAG** version converted the list into a single dense paragraph: "Phase 2 encompasses the following activities: provision of the Azure landing zone...; development of ingestion pipelines...; construction of the medallion architecture...". Quality scored higher (2 vs 1) but faithfulness dropped to 0 — the evaluator flagged the structural transformation as unfaithful.

**Interpretation:** The RAG version borrowed a "flowing prose" style from the corpus. The quality score increase is debatable — the original list format is arguably more readable and scannable. The faithfulness score of 0 is concerning and suggests the evaluator penalises structural changes heavily. (Note: the improvement prompt has since been updated to "Preserve bullet points and lists where they aid readability.")

### Case 2: Deliverables table — RAG adds professional framing

**SOW_03 / Deliverables** — Quality: baseline 2, RAG 3. Faithfulness: baseline 1, RAG 0.83.

The original is a markdown deliverables table. The **baseline** reformatted column headers and expanded "DPIA" to its full form. The **RAG** version added an introductory sentence ("The Supplier shall deliver the following outputs in accordance with the schedule specified below:") before the table and reformatted column headers. Quality 3 — the evaluator valued the professional framing.

**Interpretation:** This is a genuine RAG contribution. The "Supplier shall deliver" phrasing is a convention visible in corpus documents. The slight faithfulness drop (0.83) reflects the added sentence, which is new text but appropriate professional framing rather than hallucination.

### Case 3: Objectives — RAG restructures prose into list

**SOW_01 / Objectives** — Quality: baseline 1, RAG 2. Faithfulness: both 1.

The original is a semicolon-separated list in a single sentence. The **baseline** added articles ("to assess", "to identify") but kept the sentence structure. The **RAG** version broke it into a bulleted list with proper heading and bullets. Quality improved from 1 to 2 with perfect faithfulness.

**Interpretation:** RAG occasionally improves formatting in both directions — converting prose to lists when appropriate, and (in Case 1) converting lists to prose. The inconsistency suggests the model is pattern-matching against corpus structure rather than making principled formatting decisions.

### Case 4: Background — RAG barely changes anything

**SOW_02 / Background** — Quality: baseline 2, RAG 1. Faithfulness: baseline 0.92, RAG 1.

The **baseline** made substantive wording improvements ("wishes to commission" → "intends to commission", "particularly for users" → "particularly affecting users"). The **RAG** version was nearly identical to the original. The retrieved chunk was about "Supplier will retain responsibility for compliance..." — completely irrelevant to an HMRC digital service background section.

**Interpretation:** When retrieval fails (as in 90%+ of sections), RAG output converges with or underperforms baseline. The model doesn't hallucinate from irrelevant context (good), but it also doesn't benefit from it (expected).

---

## 6. Discussion

### 6.1 The Model Change Effect

Switching from phi-4 to qwen3.5:27b had several measurable effects:

1. **Better constraint adherence.** Faithfulness jumped from 0.79/0.89 to 0.99/0.96. The model follows the "ONLY use facts from the SECTION TO REWRITE" instruction much more reliably. The previous report's worst case (faithfulness 0.12, a full hallucination) has no equivalent here.

2. **Less creative expansion.** The flip side of better constraint adherence is that the model is more conservative. Phi-4's baseline sometimes added substantial new content (Change Control: 1 sentence → detailed procedure, quality 3). Qwen3.5 tends to make wording-level improvements rather than structural expansions. This may explain why quality scores haven't risen despite the stronger model.

3. **Stricter evaluation.** Qwen3.5 as evaluator appears to rate more harshly — context precision/recall dropped despite identical retrieval. All 60 original quality scores are 1 (same pattern as the previous report's 20), suggesting the evaluator systematically defaults to 1 for unimproved text.

### 6.2 Why RAG Still Doesn't Help

The fundamental problem hasn't changed: the corpus is too small, too homogeneous, and too heavily redacted to provide useful retrieval context. The retriever returns the same generic Buyer/Supplier obligation chunks for every section type, and the evaluator correctly judges them as irrelevant (context precision 0.05).

The one signal in the data — Budget/Payment Terms sections scoring slightly better with RAG — points to what would make RAG work: a larger corpus with concrete, structured examples in section types where the LLM's pretraining knowledge is genuinely insufficient.

### 6.3 Noise Sensitivity Improvement

The most positive finding: noise sensitivity is universally 0.00, compared to occasional spikes (up to 0.47) in the previous report. Qwen3.5 successfully ignores irrelevant context rather than being constrained or confused by it. This means that even when retrieval fails, RAG doesn't actively harm the output — it just doesn't help.

### 6.4 Limitations

- **Evaluator bias.** The same model improves and evaluates. All 60 original quality scores are exactly 1, suggesting systematic underscoring of unimproved text.
- **Single-run variance.** No repeated trials means quality scores could shift ±1 on re-run.
- **Synthetic test documents.** All 5 test SoWs are fictional, though they span diverse government domains.
- **Quality scale compression.** Scores cluster at 1-2 with rare 3s. The 1-5 scale is effectively a 1-3 scale, reducing statistical sensitivity.
- **Thinking mode disabled.** Reasoning was disabled for speed. Enabling thinking (at significant time cost) might improve both improvement and evaluation quality.

---

## 7. Recommendations

### From the previous report — still valid

1. **Re-run evaluation with a cloud LLM** (highest priority). Using the same model for improvement and evaluation creates systematic bias. A cloud evaluator (Claude, GPT-4o) would provide an independent quality assessment. This remains the single most important step to validate the findings.

2. **Expand the corpus to 20+ documents** from multiple clients, sectors, and procurement frameworks. The current 4-document corpus is the fundamental bottleneck.

3. **Run multiple trials** (3-5 runs per document) to capture output variance and enable significance testing.

### New recommendations

4. **Add unredacted or lightly-redacted documents.** The Budget/Payment Terms signal shows RAG helps when corpus material contains concrete structural examples. Rate tables, deliverables matrices, and acceptance criteria templates are the highest-value additions — and they're the least sensitive to redaction (they're structural patterns, not client-specific data).

5. **Test with thinking enabled on improvement calls.** The qwen3.5:27b model supports boolean thinking mode. Enabling it for improvement calls (at ~2-5x time cost per call) might produce better-reasoned structural improvements. Run a comparison with thinking on vs off, keeping evaluation consistent.

6. **Investigate quality score compression.** The evaluator scores all originals at 1 and most improvements at 1-2. Either calibrate the rubric with example scores, or switch to a comparative evaluation ("is A better than B?") rather than absolute scoring. Comparative judgements are more reliable for LLM evaluators.

7. **Consider section-type-specific retrieval.** The retriever returns the same generic chunks for every section. Tagging chunks with their canonical section type at indexing time and filtering retrieval to matching types would eliminate cross-section contamination — the most visible retrieval failure mode.
