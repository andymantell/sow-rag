# Experiment Report: RAG vs Baseline for SoW Improvement

**Date:** 2026-03-20
**Status:** Internal technical report — partial run, 3 real redacted documents, 72 evaluated sections
**Data source:** Partial `experiment-results.json` recovered from database after cancelled batch run

---

## Executive Summary

With real government SoW documents, the improvement pipeline produces noticeably better results than with synthetic test data — quality scores reach 3 and 4 for the first time, and the quality scale is actually being used. RAG still provides no aggregate quality advantage over baseline (1.81 vs 1.83), but retrieval metrics improved substantially: context recall jumped from 0.07 to 0.28, suggesting the corpus is more relevant to real procurement documents than to the synthetic ones. The fundamental corpus bottleneck remains (4 documents, 27 chunks), but the retrieval signal is stronger with real inputs.

---

## Changes Since Last Report

### Methodology Changes

| Parameter | Previous (2026-03-19) | Current (2026-03-20) |
|-----------|----------------------|---------------------|
| Chat model | qwen3.5:27b | qwen3.5:27b (unchanged) |
| Evaluation model | qwen3.5:27b | qwen3.5:27b (unchanged) |
| Test documents | 5 synthetic SoWs | 3 real redacted SoWs (partial run) |
| Sections evaluated | 60 | 72 |
| Document type | Clean narrative SoWs | CCS framework order forms + SoW templates |
| Corpus | 4 redacted HMRC SoWs, 27 chunks | Unchanged |

**Direct comparison is limited.** The test documents changed from synthetic to real, and the run is incomplete (3 of 4 documents, with evaluation gaps within each). However, the model, corpus, and pipeline are identical, so differences in retrieval metrics are attributable to the change in test material.

### Key Metric Comparison

| Metric | Previous (synthetic) | Current (real) | Direction |
|--------|---------------------|---------------|-----------|
| Quality (baseline mean) | 1.60 | 1.83 | Better |
| Quality (RAG mean) | 1.60 | 1.81 | Better |
| Quality gap (baseline − RAG) | 0.00 | +0.02 | Similar |
| Faithfulness (baseline mean) | 0.99 | 0.92 | Worse |
| Faithfulness (RAG mean) | 0.96 | 0.94 | Similar |
| Factual correctness (baseline) | 0.99 | 0.96 | Slightly worse |
| Factual correctness (RAG) | 0.94 | 0.97 | Better |
| Context precision | 0.05 | 0.10 | **2x better** |
| Context recall | 0.07 | 0.28 | **4x better** |
| RAG quality wins (head-to-head) | 10/60 (16%) | 7/72 (9%) | Worse |
| RAG quality losses | 10/60 (16%) | 9/72 (12%) | Similar |
| Max quality score observed | 3 | **4** | Higher ceiling |

### What Moved

1. **Quality scores have a wider range.** The previous run's scores clustered at 1-2 with occasional 3s. This run has multiple 4s (Maximum Liability sections scored 4 in both baseline and RAG) and more 3s. The 1-5 scale is being used more meaningfully, suggesting real documents give the evaluator more to differentiate.

2. **Retrieval actually works sometimes.** Context recall jumped from 0.07 to 0.28 — 4x improvement. Context precision doubled from 0.05 to 0.10. The corpus (all from CCS framework contracts) is genuinely more relevant to these real CCS framework documents than to the synthetic SoWs, which lacked framework boilerplate.

3. **Faithfulness dropped for baseline.** Baseline faithfulness fell from 0.99 to 0.92. Real documents have more complex, ambiguous text (redaction gaps, framework cross-references, legal boilerplate) that's harder for the LLM to faithfully rewrite. Interestingly, RAG faithfulness held steady (0.96 → 0.94), and RAG now has *better* average faithfulness and factual correctness than baseline — a reversal from the synthetic run.

4. **RAG quality win rate dropped.** Only 7/72 (9%) sections see RAG quality exceed baseline, down from 10/60 (16%). But RAG losses also dropped to 9/72 (12%), and ties dominate at 77%. The improvement is more conservative with real documents.

### Previous Recommendations Addressed

| Recommendation | Status |
|----------------|--------|
| Re-run with cloud LLM evaluator | Not done |
| Expand corpus to 20+ documents | Not done |
| Run multiple trials | Not done |
| Add unredacted/lightly-redacted docs | Not done |
| Test with thinking enabled | Not done |
| Investigate quality score compression | **Partially addressed** — real docs show wider score range |
| Section-type-specific retrieval | Not done |
| **Re-run with real test documents** | **Done (partial)** — 3 of 4 real docs processed |

---

## 1. Methodology

### Experiment Setup

| Parameter | Value |
|-----------|-------|
| Chat model | qwen3.5:27b (local, Ollama, thinking disabled) |
| Embedding model | nomic-embed-text (local, Ollama) |
| Corpus documents | 4 PDFs, 27 chunks total |
| Test documents | 3 real redacted SoW PDFs (of 4 available) |
| Total sections | 257 |
| Sections with improvement (baseline + RAG) | 182 |
| Sections with evaluation scores | 72 |
| Retrieval | Top-5 chunks by cosine similarity per section |
| Evaluation | Ragas metrics, same local LLM as evaluator |
| Runs | 1 per document (incomplete — batch cancelled) |

### Completeness

This is a **partial run**. The batch runner was cancelled mid-execution and results were recovered from the SQLite database. Completion status:

| Document | Total Sections | Improved | Evaluated |
|----------|---------------|----------|-----------|
| CCZN21A57 (Cabinet Office / NQC) | 70 | 52 | 32 |
| RM1043 SK6 Lead Architect (MoD) | 47 | 31 | 19 |
| RM6263 UC Continuity (DWP) | 140 | 99 | 21 |
| SoW1 Comms (DBT) | — | Not started | Not started |

The 72 evaluated sections are spread across 3 documents. This is more evaluated sections than the previous run (60), but unevenly distributed. Aggregate metrics should be treated as indicative, not definitive.

### Test Documents

1. **CCZN21A57 RM1043.7-DOS5-Schedule-6-Order-Form [REDACTED].pdf** — DOS5 Lot 1 order form. Cabinet Office contracting for Contracts Finder and Find a Tender services from NQC Ltd. 70 sections, 32 evaluated. Includes both the framework order form and an embedded SoW template.
2. **RM1043CCT1033 - SK6 Lead Architect Final Redacted.pdf** — DOS5 Lot 2 order form. MoD Defence Digital contracting a single Lead Architect role for the Skynet 6 programme from People Source Consulting. 47 sections, 19 evaluated.
3. **RM6263-Order-Form-UC Continuity of Service-ecm_12644 - v1.0 - Redacted.pdf** — DSP Lot 2 order form. DWP contracting 29 development roles for Universal Credit from Astraeus Consulting. 140 sections, 21 evaluated.

### Corpus Documents

Unchanged from the previous report:

| Document | Supplier | Buyer | Domain |
|----------|----------|-------|--------|
| SOW UBS Redacted Good 1.pdf | Accenture (UK) Ltd | HMRC | Unity Programme — HR/Finance |
| SOW Accenure Redacted Good.pdf | Accenture (UK) Ltd | HMRC | Borders & Trade — Discovery |
| Redacted Accenture.pdf | Accenture (UK) Ltd | HMRC | Borders & Trade (variant) |
| Redacted CapGemini.pdf | Capgemini UK Plc | HMRC | ETMP — SAP payment processing |

---

## 2. Results

### 2.1 Aggregate RAG vs Baseline

| Metric | Baseline Mean | RAG Mean | Baseline Median | RAG Median |
|--------|-------------|---------|----------------|-----------|
| **Quality (1–5)** | 1.83 | 1.81 | 2.00 | 2.00 |
| Faithfulness (0–1) | 0.92 | **0.94** | 1.00 | 1.00 |
| Factual Correctness (0–1) | 0.96 | **0.97** | 1.00 | 1.00 |
| Response Relevancy (0–1) | 0.59 | 0.57 | 0.63 | 0.64 |

Quality is a dead heat. The notable reversal: RAG now has better faithfulness (0.94 vs 0.92) and factual correctness (0.97 vs 0.96) than baseline. In the synthetic run, baseline led on both metrics. This may indicate that retrieved context helps the LLM stay grounded when processing complex, ambiguous real text.

### 2.2 Head-to-Head (n=72 evaluated sections)

| Metric | RAG > Baseline | RAG = Baseline | RAG < Baseline |
|--------|---------------|----------------|----------------|
| Quality | 7 (9%) | 56 (77%) | 9 (12%) |
| Faithfulness | 14 (19%) | 52 (72%) | 6 (8%) |
| Factual Correctness | 8 (11%) | 53 (73%) | 11 (15%) |
| Response Relevancy | 27 (37%) | 21 (29%) | 24 (33%) |

The quality head-to-head is closer to a dead heat than the synthetic run. The faithfulness head-to-head is interesting: RAG beats baseline in 14 sections (19%) and loses in only 6 (8%). This is a clear RAG advantage on faithfulness, the opposite of the synthetic run (where RAG lost on faithfulness 11% of the time and won only 1%).

### 2.3 RAG-Specific Metrics

| Metric | Mean | Median |
|--------|------|--------|
| Context Precision | 0.10 | 0.00 |
| Context Recall | 0.28 | 0.00 |
| Noise Sensitivity | 0.00 | 0.00 |

Context recall at 0.28 means the retriever finds at least some relevant material in roughly a quarter of sections — up from 7% with synthetic docs. Medians remain 0.00 because the majority of sections still get zero relevant retrieval. Noise sensitivity remains perfect at 0.00.

### 2.4 Per Canonical Section Breakdown

| Canonical Section | n | Avg Baseline Q | Avg RAG Q | Notable |
|-------------------|---|---------------|-----------|---------|
| Warranties and Liabilities | 3 | **3.33** | **3.33** | Highest quality scores in the experiment |
| IP and Confidentiality | 8 | 2.38 | **2.50** | RAG slightly ahead |
| Budget and Payment Terms | 13 | 2.23 | 2.08 | Baseline now ahead (was RAG's best) |
| Introduction/Background | 3 | 2.00 | 2.00 | Tied |
| Scope of Work | 7 | 1.57 | 1.57 | Tied |
| Sign-Off and Approvals | 8 | 1.50 | **1.75** | RAG ahead |
| Deliverables | 2 | 2.00 | 1.50 | Baseline ahead (small sample) |
| Project Management/Reporting | 7 | 1.43 | 1.43 | Tied |
| Project Requirements | 13 | 1.46 | 1.46 | Tied |
| Risk Management | 2 | 1.50 | 1.00 | Baseline ahead (small sample) |
| Roles and Responsibilities | 4 | 1.25 | 1.00 | Baseline ahead |
| Project Timeline/Milestones | 1 | 3.00 | 3.00 | Single sample |
| Change Control | 1 | 1.00 | 1.00 | Single sample |

Warranties and Liabilities sections achieve quality 3-4 consistently — these are legal boilerplate sections where the LLM's legal training data enables substantive improvement. IP and Confidentiality sections show the only clear RAG advantage (2.50 vs 2.38), likely because the corpus contains relevant security and data handling clauses.

Budget and Payment Terms, previously RAG's strongest section type, now slightly favours baseline (2.23 vs 2.08 over a larger sample of 13 sections). The corpus's rate card and payment structure examples may be less helpful with real documents that already have detailed commercial terms.

---

## 3. Retrieval Analysis

Retrieval similarity scores were not captured in the database export (the `RetrievedContextsJson` column stores chunk text but the export script didn't recover scores). However, the Ragas evaluation provides indirect evidence:

- **Context precision 0.10** — the retriever found at least one relevant chunk in roughly 10% of cases
- **Context recall 0.28** — when relevant material exists, the retriever finds some of it about a quarter of the time
- Non-zero context recall appears in 20+ sections (vs ~10 in the synthetic run)
- The highest context precision scores (0.87, 1.00) appear in framework boilerplate sections (Material KPIs, Security Applicable to SOW) where the corpus contains directly comparable text

The improvement over the synthetic run confirms the hypothesis: the corpus (CCS framework SoWs from HMRC) is more relevant to other CCS framework documents than to synthetic narrative SoWs. This is expected — the framework order form templates share structural elements across government departments.

---

## 4. Test Document Assessment

### Summary Table

| Document | Buyer | Framework | Realism | Completeness | Baseline Quality | Suitability |
|----------|-------|-----------|---------|-------------|-----------------|-------------|
| CCZN21A57 (Contracts Finder/FTS) | Cabinet Office | DOS5 RM1043.7 | High | High (10+ canonical sections) | Adequate | High |
| RM1043 (Skynet 6 Lead Architect) | MoD Defence Digital | DOS5 RM1043.7 | High | Low (mostly order form) | Poor (as SoW) | Medium |
| RM6263 (UC Continuity) | DWP | DSP RM6263 | High | High (order form + SoW) | Adequate | High |
| SoW1 Comms (NDTP) | DBT | DSP RM6263 | High | High (full SoW template) | Good | High |

### Per-document assessment

**CCZN21A57 — Contracts Finder/FTS (Cabinet Office / NQC Ltd).** A DOS5 Lot 1 Digital Outcomes order form for maintaining and developing two e-notification services. This is a genuine FOI-released procurement document with real redaction ("REDACTED TEXT under FOIA Section 40"). It contains both the framework order form (call-off terms, incorporated schedules, liability, charges) and a substantial embedded SoW template with scope, deliverables, milestones, KPIs, and reporting requirements. 70 sections is a large document — the system extracts many sub-sections from the framework template. Contract value £4M, 2-year term, T&M pricing. Realism is high; this is exactly the kind of document the SoW improver would process in production. The DOS5 framework structure overlaps with the corpus documents (also CCS framework SoWs), making this the best test of whether corpus retrieval adds value with structurally similar real documents.

**RM1043 — Skynet 6 Lead Architect (MoD / People Source Consulting).** A DOS5 Lot 2 Digital Specialists order form for a single contractor placement on the Skynet 6 satellite communications programme. This is an edge case — it's primarily an order form, not a traditional SoW. The "SoW" content is minimal: a brief Call-Off Specification describing architecture work needs, with most detail in incorporated schedules. Includes DEFCON clauses (531, 602B, 609, 627, 658, 659A, 660), MOD Terms, and IR35 determination — content not found in the corpus. Contract value £153K, 12 months. The document tests how the system handles SoWs where there's very little substantive content to improve. Many sections are "Not Applicable" or placeholder text, which the LLM struggles with (quality stays at 1). The MoD/defence domain is completely absent from the corpus.

**RM6263 — UC Continuity of Service (DWP / Astraeus Consulting).** A DSP Lot 2 order form for a 29-person development team maintaining Universal Credit. The largest document in the set at 140 sections, including a detailed DDaT role/rate card (9 role types at SFIA levels 3-6, all rates redacted), DWP-specific offshoring and security clauses, and SoW template content. Contract value up to £9M over 2 years. The rate card structure is directly comparable to corpus material (Accenture and Capgemini rate cards). The document has heavy redaction of financial values. Only 21 of 140 sections have been evaluated — the batch was cancelled before evaluation completed this document, so the data here is the thinnest.

**SoW1 — Comms and Training (DBT / Southerly Communications).** Not yet processed in this run, but the most complete real SoW in the test set. A full DSP SoW template with 16 milestones (with acceptance criteria and due dates), risk/assumption/dependency tables, supplier resource plan, and rate card for a 73-working-day comms engagement for the National Digital Twin Programme. Contains genuine quality issues: "communicatiosn" typo, "identifcal" misspelling, some vague acceptance criteria. This would be the most valuable single test document for evaluating the improvement pipeline because it has both good structure for the system to work with and real flaws to fix.

### Overall test set composition

**Strengths:** All four documents are genuine redacted government SoWs obtained under FOI. They use real CCS framework templates (DOS5, DSP) with legal schedules, DEFCON references, IR35 determinations, and call-off terms — structural elements shared with the corpus. Three different government departments (Cabinet Office, MoD, DWP) and a fourth (DBT) pending. Variable quality from a single-role placement (RM1043) to a full SoW template (SoW1 Comms). Genuine redaction artefacts.

**Weaknesses:** Three of four documents are primarily framework order forms where much of the content is legal/commercial boilerplate rather than SoW narrative. This inflates section count (70, 47, 140 sections vs 12-14 for synthetic docs) but many sections are "N/A", placeholder text, or signature blocks that the improvement pipeline can't meaningfully improve. The fourth document (Comms SoW) — which has the most traditional SoW structure — wasn't processed. Only 4 documents total, insufficient for statistical significance.

---

## 5. Corpus Assessment

Unchanged from the previous report. The corpus remains 4 redacted HMRC SoWs (effectively 3 unique) from Accenture and Capgemini:

- **Too small** — 27 chunks from 4 documents
- **Single client** — all HMRC
- **Redaction strips value** — concrete details removed, leaving generic procurement prose

**New finding this run:** The corpus is more relevant to real CCS framework documents than to synthetic ones. Context recall jumped from 0.07 to 0.28. The framework boilerplate, commercial terms, and security clauses in the corpus genuinely overlap with other CCS framework SoWs. This suggests the corpus is not useless — it's just being tested against the wrong material in the synthetic run.

---

## 6. Qualitative Analysis

### Case 1: Buyer's Authorised Representative — RAG adds structure but hallucinates

**RM6263 / BUYER'S AUTHORISED REPRESENTATIVE** — Quality: baseline 2, RAG 1. Faithfulness: baseline 1.00, RAG 0.50.

The original is a single redacted line: "REDACTED - Deputy Director Universal Credit and Working Age REDACTED DWP, Benton Park View, Newcastle, NE98 1YX". The **baseline** expanded this into a clean sentence identifying the role and address. The **RAG** version added "The designated base location for this representative is" — inferring a meaning ("base location") not present in the original, where it's simply an address. Faithfulness dropped to 0.50.

**Interpretation:** RAG introduced a subtle reinterpretation of the address field. This is a real-world failure mode: the LLM reads corpus patterns (where locations are often "base locations") and applies that framing to content where it doesn't fit.

### Case 2: Commercially Sensitive Information — baseline over-elaborates, RAG stays faithful

**CCZN21A57 / Commercially Sensitive Information** — Quality: baseline 2, RAG 1. Faithfulness: baseline 0.67, RAG 1.00.

The original is: "Not applicable (Suppliers Price breakdown and Tender Details are confidential and will not be shared)". The **baseline** expanded this into two sentences, adding "No other commercially sensitive information is applicable" — a reasonable but fabricated addition. The **RAG** version stayed closer to the original phrasing. RAG scored lower on quality (1 vs 2) but higher on faithfulness (1.00 vs 0.67).

**Interpretation:** This shows the quality-faithfulness trade-off. The baseline added plausible professional framing that the evaluator rewarded, but it introduced content not in the original. RAG was more conservative and more faithful. Whether quality or faithfulness matters more depends on the use case.

### Case 3: Buyer's Standards — RAG improves "N/A" handling

**CCZN21A57 / Buyer's Standards** — Quality: baseline 1, RAG 2. Faithfulness: baseline 0.75, RAG 1.00.

The original states the supplier shall comply with Framework Schedule 1 standards, then "N/A" for additional standards. The **baseline** preserved the "N/A" verbatim. The **RAG** version rephrased "N/A" as "The Buyer does not require the Supplier to comply with any additional Standards beyond those specified" — converting a placeholder into a proper negative statement. Quality improved from 1 to 2 with perfect faithfulness.

**Interpretation:** A genuine RAG success. The rephrasing of "N/A" into a clear negative statement is a meaningful improvement that preserves the original meaning. Whether this came from corpus patterns or the model's own knowledge is unclear (context precision was 0.00), but the RAG pipeline produced a better result.

### Case 4: Sign-off blocks — neither approach helps

**RM1043 / For and on behalf of the Supplier** — Quality: baseline 2, RAG 1. Faithfulness: baseline 0.75, RAG 1.00.

The original is a redacted signature block. The **baseline** reformatted with bold markdown labels. The **RAG** version kept the original format. Quality favoured baseline's formatting, but faithfulness favoured RAG's conservatism.

**Interpretation:** Sign-off blocks are a recurring pattern in real framework documents (8 sections in this run, vs 0 in the synthetic run). Neither approach can meaningfully improve redacted placeholder text. The system would benefit from detecting and skipping these sections.

---

## 7. Discussion

### 7.1 Real Documents Are a Better Test

The shift from synthetic to real documents is the most important change in this run. Key differences:

1. **Wider quality score range.** Quality scores reach 4 for the first time (Maximum Liability sections in both CCZN21A57 and RM1043). The evaluator can differentiate better when the input material has real complexity — legal boilerplate, framework cross-references, ambiguous scope.

2. **More sections, more noise.** The 3 real documents produced 257 sections vs 63 for 5 synthetic docs. Many of these are framework boilerplate (schedules, definitions, signatures) that the 15 canonical section matcher maps imperfectly. The system's section detection wasn't designed for framework order forms.

3. **Retrieval is more relevant.** Context recall 4x improvement (0.07 → 0.28) because the corpus and test documents share framework structure. This is the first evidence that the corpus provides value — but it's structural similarity (shared CCS framework patterns), not domain-specific content.

### 7.2 The Section Count Problem

Real framework documents have far more sections than the system expects. The 15 canonical sections assume a traditional SoW structure (Background, Scope, Deliverables, etc.). A DOS5 order form has call-off terms, incorporated schedules, liability caps, payment methods, DEFCON clauses, balanced scorecards, and signature blocks — none of which map cleanly to the canonical sections. The section matcher forces them into the closest canonical category, producing poor matches (e.g., "Cyber Essentials Scheme" → "Intellectual Property and Confidentiality").

This matters because the per-section RAG retrieval uses the canonical section name to contextualise the search. When the canonical match is wrong, the retriever looks for the wrong kind of content.

### 7.3 RAG's Faithfulness Advantage

The most surprising finding: RAG now has *better* average faithfulness (0.94 vs 0.92) and factual correctness (0.97 vs 0.96) than baseline. In the synthetic run, the reverse was true. One hypothesis: when processing complex real text with redaction gaps and legal language, the retrieved context acts as an anchor, keeping the LLM closer to established patterns rather than inventing filler. The baseline, without this anchor, occasionally fabricates plausible-sounding additions (as in the Commercially Sensitive Information case).

### 7.4 Limitations

- **Partial data.** Only 72 of 257 sections evaluated. The 4th document (and most SoW-like) wasn't processed at all.
- **Evaluator bias.** Same model improves and evaluates.
- **Single run.** No repeated trials.
- **Section matcher limitations.** Framework order form sections don't map well to the 15 canonical sections, degrading both retrieval relevance and evaluation accuracy.
- **Missing retrieval scores.** Similarity scores weren't recovered from the database, so retrieval quality analysis is limited to Ragas metrics.

---

## 8. Recommendations

### Immediate (complete the experiment)

1. **Complete the batch run.** Re-run with `--resume` or re-run fully against the 4 real documents. The Comms SoW (SoW1) is the most valuable test document and wasn't processed. Completing evaluation for all sections would give a much more robust dataset.

2. **Add incremental export to the batch runner.** The batch runner currently exports only after all documents complete. If it wrote `experiment-results.json` after each document, cancelling a long run wouldn't lose data. This is a small code change to `Program.cs`.

### Methodological improvements

3. **Re-run evaluation with a cloud LLM.** Still the highest priority validation step.

4. **Add framework-aware section detection.** The current 15 canonical sections don't cover framework order form content (liability, schedules, definitions, signature blocks). Either expand the canonical section list for framework documents, or add a filter to skip non-SoW sections before improvement.

5. **Run multiple trials** to capture variance.

### Corpus improvements

6. **Expand the corpus to 20+ documents.** The improved context recall with real documents (0.28 vs 0.07) shows the corpus *is* more relevant to real CCS framework SoWs. More corpus documents from different government departments and frameworks would amplify this signal.

7. **Add DOS5 and DSP framework template examples** to the corpus specifically, since the test documents use these frameworks. The generic framework boilerplate (incorporated terms, definitions, schedules) is consistent across departments and could be retrieved effectively.

### Test set improvements

8. **Include the Comms SoW** (already in `test-sows/`). It's the most traditional SoW in the set and has genuine quality issues.

9. **Consider filtering or weighting sections.** A document with 140 sections (RM6263) dominates aggregate metrics. Many of its sections are framework boilerplate or placeholders. Section-level analysis should distinguish between "real SoW content" and "framework wrapper".
