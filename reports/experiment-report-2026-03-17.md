# Experiment Report: RAG vs Baseline for SoW Improvement

**Date:** 2026-03-17
**Status:** Internal technical report — single-run results, limited sample size

---

## Executive Summary

RAG does not improve SoW quality over prompt-only baseline with the current corpus and local LLM. Baseline outperforms RAG on quality scores (mean 1.80 vs 1.50; RAG won only 1 of 20 sections). Retrieval quality is poor — context precision averages 0.095 and context recall 0.222 — meaning the retriever is not finding relevant material for most sections. The corpus of 4 SoWs (27 chunks), all for a single client (HMRC) from two suppliers (Accenture, Capgemini), is too small and too homogeneous after redaction to provide useful RAG context for general SoW improvement.

---

## 1. Methodology

### Experiment Setup

| Parameter | Value |
|-----------|-------|
| Chat model | phi-4 (local, via Foundry/Ollama) |
| Embedding model | nomic-embed-text (local, Ollama) |
| Corpus documents | 4 PDFs, 27 chunks total |
| Test documents | 2 synthetic SoW PDFs |
| Sections evaluated | 20 (of 29 total; 9 unrecognised sections skipped) |
| Retrieval | Top-5 chunks by cosine similarity per section |
| Evaluation | Ragas metrics, same local LLM as evaluator |
| Runs | 1 per document (no repeated trials) |

### Process

For each test document, the system runs two improvement passes per section:
- **Baseline**: same improvement prompt, no retrieved context
- **RAG**: same prompt with top-5 retrieved corpus chunks injected

Then Ragas evaluation scores both outputs against the original across 12 metrics.

### Test Documents

1. **Digital_Service_SOW_Unnumbered.pdf** — 14 sections, 10 evaluated. Synthetic digital service SoW.
2. **Realistic_Gov_Digital_SOW.pdf** — 15 sections, 10 evaluated. Synthetic government digital SoW, fictional content.

Both are test-only documents not present in the corpus.

### Corpus Documents

| Document | Size | Supplier | Buyer | Domain |
|----------|------|----------|-------|--------|
| SOW UBS Redacted Good 1.pdf | 327 KB | Accenture (UK) Ltd | HMRC | Unity Programme — HR/Finance delivery partner |
| SOW Accenure Redacted Good.pdf | 412 KB | Accenture (UK) Ltd | HMRC | Borders & Trade — Trader Digital Interface discovery |
| Redacted Accenture.pdf | 259 KB | Accenture (UK) Ltd | HMRC | Borders & Trade — Trader Digital Interface discovery (variant) |
| Redacted CapGemini.pdf | 363 KB | Capgemini UK Plc | HMRC | ETMP — SAP payment processing (iOSS, FIPDFP) |

All 4 documents are SoWs / Project Work Orders delivered to HMRC. Two are from Accenture (both for Borders & Trade, one appears to be a variant of the other), one is a broader Accenture programme delivery SoW, and one is a Capgemini SAP technical delivery PWO. All have been redacted to varying degrees.

---

## 2. Results

### 2.1 Aggregate RAG vs Baseline

| Metric | Baseline Mean | RAG Mean | Baseline Median | RAG Median |
|--------|-------------|---------|----------------|-----------|
| **Quality (1–5)** | **1.80** | 1.50 | **2.00** | 1.00 |
| Faithfulness (0–1) | 0.79 | **0.89** | 1.00 | 1.00 |
| Factual Correctness (0–1) | 0.86 | **0.93** | 1.00 | 1.00 |
| Response Relevancy (0–1) | **0.67** | 0.67 | 0.66 | 0.68 |

Baseline wins on quality — the metric that most directly measures whether the improvement is useful. RAG edges ahead on faithfulness and factual correctness, but these are secondary metrics, and higher faithfulness to a poor original is not necessarily a virtue when the goal is to *improve* the document.

### 2.2 Head-to-Head (n=20 evaluated sections)

| Metric | RAG > Baseline | RAG = Baseline | RAG < Baseline |
|--------|---------------|----------------|----------------|
| Quality | 1 (5%) | 12 (60%) | **7 (35%)** |
| Faithfulness | 7 (35%) | 9 (45%) | 4 (20%) |
| Factual Correctness | 6 (30%) | 10 (50%) | 4 (20%) |
| Response Relevancy | 8 (40%) | 1 (5%) | **11 (55%)** |

RAG improved quality in **1 out of 20 sections**. It degraded quality in 7. In 12, it made no difference. This is worse than chance.

### 2.3 RAG-Specific Metrics

| Metric | Mean | Median | Non-zero count |
|--------|------|--------|----------------|
| Context Precision | 0.095 | 0.000 | 3 / 20 |
| Context Recall | 0.222 | 0.000 | 6 / 20 |
| Noise Sensitivity | 0.034 | 0.000 | 2 / 19 |

Context precision is non-zero in only 3 sections (values: 0.70, 0.20, 1.00). Context recall is non-zero in 6. In 14 of 20 sections, the retriever found nothing the evaluator considered relevant. Noise sensitivity is low in aggregate (0.034 mean), but this is because most sections have zero — when it fires, it fires hard (0.47 in one case).

### 2.4 Per-Section Breakdown by Canonical Type

#### Structural / Boilerplate Sections

| Canonical Section | n | Avg Baseline Q | Avg RAG Q | Verdict |
|-------------------|---|---------------|-----------|---------|
| Budget and Payment Terms | 2 | 1.00 | 1.00 | No change. Both versions are minimal rewrites. |
| Change Control | 2 | 2.00 | 1.00 | **RAG worse.** One section dropped from 3→1 (see qualitative analysis). |
| Termination | 2 | 1.50 | 1.00 | RAG slightly worse. |

RAG adds nothing to boilerplate sections. These are exactly the sections the LLM handles well from pretraining alone.

#### Content-Heavy Sections

| Canonical Section | n | Avg Baseline Q | Avg RAG Q | Verdict |
|-------------------|---|---------------|-----------|---------|
| Scope of Work | 5 | 1.80 | 2.00 | Misleading average — one RAG "win" (3 vs 1) had faithfulness 0.12. |
| Deliverables | 2 | 2.00 | 2.00 | Tied on quality. RAG faithfulness dropped to 0.29 in one case. |
| Project Requirements | 1 | 2.00 | 1.00 | RAG worse. |
| Project Timeline | 1 | 2.00 | 1.00 | RAG worse. |

The single section where RAG quality exceeded baseline (Realistic_Gov / SOW, 3 vs 1) also had the worst faithfulness in the entire experiment (0.12) and factual correctness of 0.19. The RAG version hallucinated additional scope from corpus fragments — impressive-looking but untrue to the original.

#### Process Sections

| Canonical Section | n | Avg Baseline Q | Avg RAG Q | Verdict |
|-------------------|---|---------------|-----------|---------|
| Introduction/Background | 2 | 2.00 | 2.00 | Tied. High context recall (1.0) but no quality gain. |
| Project Management | 2 | 2.00 | 1.00 | RAG worse. |
| Risk Management | 1 | 2.00 | 2.00 | Tied. RAG added one borrowed phrase but no real benefit. |

---

## 3. Retrieval Analysis

### 3.1 Similarity Score Distribution

100 total retrieved chunks (5 per section × 20 sections):

| Statistic | Value |
|-----------|-------|
| Mean | 0.6589 |
| Median | 0.6599 |
| Min | 0.5813 |
| Max | 0.7540 |
| >= 0.70 | 14 / 100 (14%) |
| >= 0.60 | 92 / 100 (92%) |
| >= 0.80 | 0 / 100 (0%) |

Scores cluster in a tight 0.58–0.75 band with very low variance. This is a hallmark of **low embedding discriminability** — the embeddings produce roughly the same score for any chunk against any section, regardless of actual topical relevance. No chunk ever exceeds 0.754.

### 3.2 Chunk Reuse

The same chunks appear repeatedly across unrelated sections. Evidence from the qualitative samples:

- A chunk about "Confidence Testing (BCT)... project governance" (from the Capgemini ETMP document) was the top retrieval for **Change Control**, **Term and Termination**, **Commercial and Payment Terms**, and **Governance and Reporting** — four completely different section types.
- A chunk about "Supplier will retain responsibility for compliance..." appeared in nearly every section's retrieval set.

With only 27 corpus chunks, the retriever has too few options. It returns the same generic material regardless of what section is being improved.

### 3.3 Mismatch Between Scores and Relevance

Context precision tells the real story. Despite all chunks scoring 0.58+ on cosine similarity, the evaluator judged them relevant in only 3 of 20 sections. High similarity scores do not mean topical relevance — they mean the embeddings are not discriminating. The 0.3 similarity threshold (the app's cutoff) is meaningless when everything scores 0.58+.

---

## 4. Corpus Assessment

### 4.1 Document-Level Review

**SOW UBS Redacted Good 1.pdf** (Accenture → HMRC, Unity Programme)
- **Structure:** Rich. Detailed deliverables table with engagement outputs, descriptions, deliverables, assumptions, dependencies, and acceptance criteria. Separate sections for location, assumptions, dependencies, security, IT, personnel, handover, invoicing.
- **Content quality:** High within its domain. Specific deliverables with dates ("E2E delivery plans embedded in Programme plan by March 24"), named roles, rate cards with day rates.
- **Redaction:** Incomplete — many personal names remain (Cath Rollo, Steve Bruzzese), org names visible (HMRC, Accenture, DLUHC, MCA). The `[REDACTED]` placeholders are sparse.
- **RAG suitability:** Good structure to learn from, but the content is hyper-specific to HMRC's Unity HR/Finance programme. The assumptions and dependencies sections are the most transferable — but they're also the most generic (standard Buyer/Supplier obligation boilerplate that phi-4 already knows).
- **Canonical section coverage:** ~8 of 15 sections represented. Missing: explicit Change Control, Termination, Warranties, Risk Management, Acceptance Testing, Service Levels.

**SOW Accenure Redacted Good.pdf** (Accenture → HMRC, Borders & Trade Discovery)
- **Structure:** Excellent. Clear numbered sections: Summary, Overview/Problem Statement, Scope (General/Specific Requirements, Standards/QA, Deliverables, Considerations, Responsibilities, Acceptance Criteria, Reporting), Locations, Clearance, Expenses, Offboarding, Knowledge Transfer.
- **Content quality:** Good for a discovery-phase SoW. Specific deliverables table with acceptance criteria, named acceptors, dates. Clear Buyer/Supplier responsibilities split.
- **Redaction:** Minimal — many names visible (Jill Asbery, Tony Horrell, Stephen Richardson, Dave Hatch). Dates and contract references intact.
- **RAG suitability:** Best structural exemplar in the corpus. The section layout maps well to several canonical sections. However, it's an 8-week discovery SoW — very narrow scope, unlikely to contain material relevant to full delivery SoWs being improved.
- **Canonical section coverage:** ~10 of 15 sections. Good coverage of Scope, Deliverables, Responsibilities, Acceptance. Missing: Risk Management, Budget detail, Change Control, Termination terms.

**Redacted Accenture.pdf** (Accenture → HMRC, Borders & Trade)
- Appears to be a **variant or earlier draft** of the same Borders & Trade Trader Digital Interface work. Same buyer, same supplier, same domain.
- Adds minimal vocabulary or structural diversity to the corpus beyond the "Good" version.
- **RAG suitability:** Near-duplicate. Reducing corpus to 3 effective documents.

**Redacted CapGemini.pdf** (Capgemini → HMRC, ETMP SAP)
- **Structure:** Project Work Order format. Introduction, HMRC Requirements, Capgemini Approach, Activities, Out of Scope, Transition considerations, Project Plan, Test Exit Criteria, Roles and Responsibilities, Resource Profile.
- **Content quality:** Highly technical. Detailed requirements tables with JIRA ticket references (VECOM-xxxx), SAP module names (BRF+, WebDynpro, ADR, EBS), API specifications. Resource tables with SFIA categories and day rates.
- **Redaction:** Moderate. Org names visible (HMRC, Capgemini, Barclays). Some reviewer comments left in (`Commented [HS1]`, `Commented [HS2]`).
- **RAG suitability:** Poor for general SoW improvement. The content is SAP-specific technical delivery — VECOM tickets, ETMP modules, ISO20022 banking formats. None of this transfers to a generic digital service SoW. The resource profile structure (SFIA categories, day rates) is moderately useful, but the app already generates rate tables from pretraining knowledge.
- **Canonical section coverage:** ~7 of 15. Strong on Scope, Activities, Out of Scope, Project Plan, Test Criteria, Resource Profile. Missing: formal Change Control, Termination, Risk Management, Acceptance.

### 4.2 Corpus Sufficiency Verdict

**The corpus is insufficient for meaningful RAG.** Five structural problems:

1. **Too small.** 27 chunks from 4 documents (effectively 3 unique). The retriever has so few chunks that it returns the same ones for unrelated sections. A meaningful RAG corpus for this task would need 20+ documents minimum.

2. **Single client, two suppliers.** All documents are for HMRC, from Accenture or Capgemini. They share procurement framework language (Call-Off Contract, Framework Contract RM6187), HMRC-specific terms (CDIO, ITDL, GDS standards), and identical contractual boilerplate. There is no diversity of buyer type, sector, or procurement framework.

3. **Domain mismatch with test data.** The corpus covers: HMRC Unity HR/Finance programme, HMRC Borders & Trade digital interface discovery, and HMRC SAP tax platform (ETMP/iOSS). The test documents describe a fictional "Digital Case Management System." No domain overlap exists.

4. **Redaction removes the value proposition.** RAG's strength is surfacing specific, concrete detail — named deliverables, acceptance criteria, rate benchmarks, technical specifications. Redaction targets exactly this material. What remains after redaction is generic procurement prose ("Supplier shall...", "Buyer will...") that phi-4 generates from pretraining. In practice, the redaction in these documents is inconsistent (many names and org references survive), but the *chunks* that the retriever selects tend to be the more generic passages, not the specific ones.

5. **No section-level structure.** Chunks don't carry section-type metadata. A chunk about project governance retrieves equally well for Change Control, Termination, and Deliverables — it's generic enough to weakly match everything and strongly match nothing.

### 4.3 What the Corpus Provides vs What the LLM Already Knows

| Corpus provides | LLM already knows |
|----------------|-------------------|
| UK government procurement language (GDS, BPSS/SC, IR35) | Standard SoW structural patterns |
| HMRC-specific terms (Call-Off, framework references) | Contractual boilerplate (change control, termination, etc.) |
| SFIA-category rate tables | Project management frameworks |
| Example deliverables tables with named acceptors | Generic deliverables, milestones, acceptance criteria |

The overlap is large. The corpus's unique contribution — HMRC-specific vocabulary and SFIA rate structures — is narrow and unlikely to be relevant to SoWs for other clients/sectors.

---

## 5. Qualitative Analysis

### Case 1: Change Control — RAG constrains the LLM (worst RAG quality delta)

**Digital_Service_SOW / Change Control** — Quality: baseline 3, RAG 1.

The original is a single sentence: *"All changes to scope, cost, or schedule must be managed through a formal change control process."*

The **baseline** expanded this into a detailed procedure — proposals, impact assessment, approval hierarchy, documentation. Quality score 3. Faithfulness 0.06 (it went far beyond the original, which is expected for improvement).

The **RAG** version barely changed the sentence: *"All modifications to scope, cost, or schedule must be administered through an established formal change control process."* Quality score 1. Faithfulness 1.00 (it stuck to the original).

The **retrieved chunk** (similarity 0.6463) was about "Confidence Testing (BCT)... project governance" — completely irrelevant to change control. The model appears to have become more conservative in the presence of context it couldn't use, producing a near-verbatim rewrite instead of expanding the section.

**Interpretation:** Irrelevant context made the model *worse* by suppressing the creative expansion that baseline achieved. This is the fundamental RAG failure mode: bad context is worse than no context.

### Case 2: Statement of Work — RAG hallucinates (highest RAG quality, lowest faithfulness)

**Realistic_Gov_Digital_SOW / Statement of Work** — Quality: baseline 1, RAG 3. Faithfulness: baseline 0.60, RAG 0.12.

The original is boilerplate: *"This Statement of Work forms part of a Call-Off Contract... All content is fictional."*

The **baseline** minimally rephrased it. Quality 1.

The **RAG** version invented additional structure — added a "Scope of Work" subsection, mentioned "specified tasks and deliverables", referenced amendment processes. Quality 3 — but this content was hallucinated from corpus patterns, not derived from the original document. Faithfulness 0.12, factual correctness 0.19.

The **retrieved chunk** (similarity 0.6405) was the table of contents from the Accenture SoW. The model used this structural template to generate plausible-looking but fabricated content.

**Interpretation:** RAG's one "quality win" was a hallucination. The model borrowed structural patterns from the corpus to invent content that doesn't exist in the original. This would be actively harmful in production.

### Case 3: Term and Termination — baseline expands, RAG rephrases

**Digital_Service_SOW / Term and Termination** — Quality: baseline 2, RAG 1.

Original: *"The Contract shall commence on the Effective Date and continue for the agreed term, subject to termination provisions set out in the Contract."*

Baseline added post-termination obligations, data access, IP rights. RAG just rephrased the original sentence. Same pattern as Change Control — irrelevant context (BCT testing again, similarity 0.6719) suppressed expansion.

### Case 4: In-Scope Services — baseline more detailed

**Digital_Service_SOW / In-Scope Services** — Quality: baseline 3, RAG 2.

Original was a bullet list of 8 service areas. Baseline expanded each bullet with descriptions ("Executing user research and designing the service according to user needs"). RAG kept them terse ("Conducting discovery and validating requirements"). The retrieved chunk was about supplier responsibilities and IR35 — tangentially related but not helpful.

---

## 6. Discussion

### 6.1 Why RAG Underperforms

The results confirm the experiment plan's hypothesis: *"The current corpus may not provide enough novel, domain-specific information to justify RAG."*

Three failure modes are visible:

1. **Irrelevant context suppresses expansion.** In 7 of 20 sections, RAG quality was lower than baseline. The qualitative samples show a consistent pattern: the baseline freely expands sparse originals, while the RAG version becomes more conservative when fed irrelevant chunks. The model appears to anchor on the retrieved context and constrain its output, even when the context has nothing to do with the section.

2. **When RAG does influence output, it can hallucinate.** The one section where RAG beat baseline on quality (3 vs 1) did so by inventing content borrowed from corpus structural patterns. Faithfulness was 0.12. This is the worst possible outcome — plausible-looking fabrication.

3. **The retriever cannot discriminate.** With 27 chunks and similarity scores clustered in a 0.58–0.75 band, every section gets the same generic material. Context precision of 0.095 means the evaluator considers the retrieved chunks irrelevant 90.5% of the time.

### 6.2 The Faithfulness–Quality Trade-off

RAG's faithfulness scores are higher than baseline (mean 0.89 vs 0.79). This is **not a feature** — it means RAG is doing less creative expansion. For SoW improvement, where the goal is to enrich sparse originals with structure and detail, higher faithfulness to a poor original means less improvement. The baseline's lower faithfulness reflects the fact that it adds more new material — exactly what the user wants.

### 6.3 Limitations

- **Evaluator bias.** phi-4 both improved and evaluated the documents. The evaluator may systematically favour certain output patterns, skewing all scores in a consistent direction. The fact that all 20 original quality scores are exactly 1 is suspicious — either the test documents are uniformly terrible, or the evaluator defaults to 1 for any non-improved text.
- **Small sample size.** 20 evaluated sections from 2 test documents provides no statistical power. No significance tests are reported because they would be meaningless at this n.
- **Synthetic test documents.** Both test SoWs are fictional. They may not represent real-world SoW quality distributions or domain-specific content.
- **Single-run variance.** phi-4 output is non-deterministic. Quality scores could shift by ±1 on a re-run. The Change Control section scoring baseline=3 vs RAG=1 could reverse on another run.
- **Local LLM ceiling.** phi-4 (14B parameters) may be below the capability threshold for both the improvement and evaluation tasks. A more capable model might use RAG context more selectively and expand originals more effectively.
- **Evaluation metric reliability.** Response relevancy clusters in a narrow 0.51–0.83 band across all sections, suggesting the evaluator is not discriminating effectively on this metric.

---

## 7. Recommendations

### Highest priority: validate the evaluation pipeline

1. **Re-run evaluation with a cloud LLM.** Use Claude or GPT-4 as the evaluator (not the improver) to remove evaluator bias and test whether phi-4's scores are reliable. This is the single most valuable next step — if the evaluation itself is unreliable, all other findings are suspect.

2. **Add more test documents.** Use 5–10 real (non-synthetic) SoWs of varying quality and domain to increase sample size and ecological validity.

3. **Run multiple trials.** 3–5 runs per document would capture phi-4's output variance and enable basic significance testing.

### Medium priority: improve the RAG pipeline

4. **Expand the corpus to 20+ documents.** Include SoWs from multiple clients, sectors, and procurement frameworks. The current single-client, two-supplier corpus cannot support meaningful retrieval.

5. **Add unredacted or template documents.** The highest-value RAG source material would be best-practice SoW templates, style guides, or example sections — not redacted production documents. These would provide the structural patterns and exemplar language that the LLM could use without hallucination risk.

6. **Implement section-aware chunking.** Label chunks with their canonical section type at indexing time. Retrieve only chunks matching the current section type, rather than using generic similarity across all chunks. This would eliminate the cross-section contamination visible in the data.

7. **Raise or make the similarity threshold dynamic.** The current 0.3 threshold admits everything. Given the 0.58–0.75 score distribution, a threshold of 0.68 or higher would filter out the weakest matches. Alternatively, retrieve only when the best chunk exceeds a minimum score gap above the worst, ensuring retrieved material is meaningfully more relevant than random.

### Long-term: consider alternatives to RAG

8. **Few-shot prompting.** Instead of retrieving arbitrary chunks, hand-curate 2–3 exemplar sections per canonical section type and include them directly in the prompt. This provides consistent, high-quality context without retrieval noise. It scales poorly to many section types but may be more effective with 4 documents than RAG.

9. **Conditional RAG.** Use RAG for content-heavy sections (Scope, Deliverables) where domain-specific examples genuinely add value, and prompt-only baseline for structural/boilerplate sections (Change Control, Termination, Payment Terms) where the LLM's pretraining knowledge is sufficient. The per-section data supports this split.

10. **Upgrade the improvement LLM.** Test with a larger model (or cloud model) to determine whether the quality ceiling is the LLM's capability or the RAG pipeline's contribution. If a better model still shows no RAG benefit, the corpus is definitively the bottleneck.
