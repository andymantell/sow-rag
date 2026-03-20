# Experiment Report: RAG vs Baseline for SoW Improvement

**Date:** 2026-03-20
**Status:** Internal technical report — single-run results, 5 synthetic test documents
**Data source:** `experiment-results.json` exported 2026-03-19T15:23:41Z (same data as previous report)

---

## Executive Summary

RAG provides no measurable quality improvement over prompt-only baseline. Mean quality scores are identical (1.60 vs 1.60), with RAG winning and losing quality in exactly 10 of 60 sections each (16%). Retrieval remains critically poor: context precision 0.05, context recall 0.07. The test documents are well-structured synthetic SoWs that lack the messiness and irregularity of real government procurement documents, which inflates baseline performance and understates the potential value of RAG. Four real redacted SoWs are now available in the test folder but have not yet been evaluated.

---

## Changes Since Last Report

### Methodology Changes

| Parameter | Previous (2026-03-19) | Current (2026-03-20) |
|-----------|----------------------|---------------------|
| Chat model | qwen3.5:27b | qwen3.5:27b (unchanged) |
| Evaluation model | qwen3.5:27b | qwen3.5:27b (unchanged) |
| Test documents | 5 synthetic SoWs | 5 synthetic SoWs (unchanged) |
| Sections evaluated | 60 | 60 (unchanged) |
| Corpus | 4 redacted HMRC SoWs, 27 chunks | Unchanged |

**This report uses the same experiment data as 2026-03-19.** The quantitative results are identical. The new content is: (1) a test document quality assessment for both the synthetic documents and the newly available real documents, and (2) updated recommendations based on those findings.

### Previous Recommendations Addressed

| Recommendation | Status |
|----------------|--------|
| Re-run with cloud LLM evaluator | Not done |
| Expand corpus to 20+ documents | Not done |
| Run multiple trials | Not done |
| Add unredacted/lightly-redacted docs | Not done |
| Test with thinking enabled | Not done |
| Investigate quality score compression | Not done |
| Section-type-specific retrieval | Not done |
| **Re-run with real test documents** | **In progress** — 4 real SoWs now in `test-sows/`, batch run started but cancelled before completion |

---

## 1. Methodology

Unchanged from the 2026-03-19 report. See that report for full details.

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

---

## 2. Results

### Aggregate RAG vs Baseline Comparison

| Metric | Baseline Mean | RAG Mean | Baseline Median | RAG Median |
|--------|-------------|---------|----------------|-----------|
| Quality (1-5) | 1.60 | 1.60 | 2.00 | 2.00 |
| Faithfulness (0-1) | 0.99 | 0.96 | 1.00 | 1.00 |
| Factual Correctness (0-1) | 0.99 | 0.94 | 1.00 | 1.00 |
| Response Relevancy (0-1) | 0.65 | 0.65 | 0.65 | 0.65 |

### RAG-Specific Metrics

| Metric | Mean | Median |
|--------|------|--------|
| Context Precision | 0.05 | 0.00 |
| Context Recall | 0.07 | 0.00 |
| Noise Sensitivity | 0.00 | 0.00 |

### Head-to-Head Comparison

| Metric | RAG > Baseline | RAG = Baseline | RAG < Baseline |
|--------|---------------|----------------|----------------|
| Quality | 10 (16%) | 40 (66%) | 10 (16%) |
| Faithfulness | 1 (1%) | 52 (86%) | 7 (11%) |
| Factual Correctness | 3 (5%) | 50 (83%) | 7 (11%) |
| Response Relevancy | 15 (25%) | 21 (35%) | 24 (40%) |

### Per-Section Type Summary

| Canonical Section | n | Baseline Avg Q | RAG Avg Q | RAG Better | RAG Worse |
|------------------|---|---------------|-----------|------------|-----------|
| Budget and Payment Terms | 5 | 1.80 | 2.00 | 2 | 1 |
| Deliverables | 5 | 2.00 | 2.00 | 1 | 1 |
| Scope of Work | 8 | 1.38 | 1.50 | 1 | 0 |
| Project Timeline and Milestones | 15 | 1.60 | 1.67 | 3 | 2 |
| Project Requirements | 12 | 1.58 | 1.50 | 2 | 4 |
| Introduction/Background | 5 | 1.60 | 1.60 | 1 | 1 |
| Project Management and Reporting | 3 | 2.00 | 1.67 | 0 | 1 |
| Roles and Responsibilities | 3 | 1.33 | 1.00 | 0 | 1 |
| Acceptance Criteria | 3 | 1.33 | 1.00 | 0 | 1 |
| Intellectual Property and Confidentiality | 1 | 1.00 | 2.00 | 1 | 0 |

Budget and Payment Terms remains the only section type where RAG consistently outperforms baseline (avg 2.00 vs 1.80). This is the one area where the corpus contains concrete structural examples (rate tables, SFIA categories, payment milestone formats).

---

## 3. Retrieval Analysis

### Similarity Score Distribution (n=300 retrieved chunks)

| Statistic | Value |
|-----------|-------|
| Mean | 0.6442 |
| Median | 0.6443 |
| Min | 0.5530 |
| Max | 0.7624 |

### Threshold Distribution

| Threshold | Count | Percentage |
|-----------|-------|------------|
| >= 0.5 | 300/300 | 100% |
| >= 0.6 | 258/300 | 86% |
| >= 0.7 | 25/300 | 8% |
| >= 0.8 | 0/300 | 0% |

All 300 chunks pass the 0.3 similarity threshold, but similarity scores cluster tightly between 0.55–0.76. No chunk scores above 0.8, suggesting the retriever finds vaguely related material but never truly relevant content. The evaluator confirms this: context precision is 0.00 for 53 of 60 sections.

---

## 4. Test Document Assessment

### Synthetic Test Documents (used in this experiment)

| Document | Domain | Buyer | Sections | Canonical Coverage | Realism | Baseline Quality | Suitability |
|----------|--------|-------|----------|-------------------|---------|-----------------|-------------|
| SOW_01 Cyber Security Assessment | Cyber | DSIT | 14 | 10/15 (good) | Medium | Good | Medium |
| SOW_02 HMRC Digital Service Discovery | Digital/UX | HMRC | 12 | 9/15 (adequate) | Medium-High | Good | High |
| SOW_03 NHS Data Platform Migration | Data/Cloud | NHS England | 13 | 9/15 (adequate) | Medium | Good | Medium |
| SOW_04 MoJ Neurodiversity Training | Training/L&D | MoJ/HMPPS | 12 | 9/15 (adequate) | Medium | Good | Low |
| SOW_05 FCDO Strategic Communications | Comms | FCDO | 12 | 9/15 (adequate) | Medium | Good | Low |

#### Per-document assessment

**SOW_01 — Cyber Security Assessment (DSIT).** Covers 10 canonical sections, well-structured with clear scope, deliverables table, acceptance criteria, and governance. The document reads as plausible but shows signs of synthesis: every section is cleanly formatted, no ambiguity, no cross-references to external schedules, and no procurement framework boilerplate. Real cyber assessment SoWs would typically reference specific framework lots, DEFCON clauses, and have messier scope definitions. Baseline writing quality is *good* — the prose is clear and professional, leaving little room for LLM improvement. This explains why quality scores cluster at 1-2: there's nothing substantially wrong to fix.

**SOW_02 — HMRC Digital Service Discovery/Alpha.** The closest to a realistic SoW in the set because the domain (HMRC digital services) overlaps with the corpus, and the structure mirrors GDS-era discovery/alpha patterns. Includes team composition, ways of working, and co-location requirements — details you'd see in real SoWs. However, the conspicuous absence of a framework order form, commercial schedule references, IR35 status, and security clearance boilerplate marks it as synthetic. Baseline quality is *good*. The HMRC domain overlap makes this the most useful test document for evaluating whether corpus retrieval helps — but context precision/recall are still 0.00 for 10/12 sections, showing the corpus doesn't cover user research, prototyping, or accessibility content.

**SOW_03 — NHS Data Platform Migration.** Well-structured technical SoW with phases, deliverables table, and hypercare handover. Plausible domain (cloud migration) but the clean four-phase structure with no dependencies, no integration partners, and no procurement boilerplate reveals synthesis. The Phase 2 build task list is the most realistic section — detailed Azure-specific activities. Baseline quality is *good*. The RAG version of Phase 2 converted the bullet list to flowing prose (faithfulness 0.00), the most dramatic failure case in the experiment.

**SOW_04 — MoJ Neurodiversity Training.** An unusual domain for a SoW — training programme design and delivery. The content is plausible (co-design requirements, train-the-trainer, Kirkpatrick evaluation framework) but the domain is so far from the HMRC-focused corpus that retrieval has no useful material to surface. Context precision/recall are 0.00 for 10/11 sections. As a test of RAG vs baseline, this document primarily tests how well the model ignores irrelevant context — and it does (noise sensitivity 0.00). Low suitability for evaluating whether RAG *helps*, useful only for testing that RAG doesn't *harm*.

**SOW_05 — FCDO Strategic Communications.** Another domain with zero corpus overlap. The content is plausible but reads as a consulting brief rather than a government SoW — no framework references, no IR35, no security clearance requirements. The Commercials section is the only one where RAG helped (quality 3 vs baseline 2, context recall 0.25), consistent with the pattern that Budget/Payment Terms is where corpus material adds value.

#### Overall assessment of synthetic test set

**Strengths:** Good domain diversity (cyber, digital, data, training, comms), five different government departments, and consistent structure that makes section-level comparison straightforward.

**Weaknesses — and they are significant:**

1. **Too clean.** Every document is well-written, clearly structured, and internally consistent. Real SoWs are messy — sections get copy-pasted from previous contracts, scope descriptions are vague or contradictory, commercial terms are buried in cross-references to framework schedules. The synthetic documents give the baseline LLM a polished starting point, making it hard for RAG to add value. When the original is already quality 1 on a 1-5 scale but the writing is actually competent, the evaluator is underscoring originals, not identifying genuinely poor content.

2. **No framework boilerplate.** Real CCS framework SoWs (DOS5, DSP) come wrapped in extensive order form templates with legal schedules, DEFCON references, IR35 determinations, and call-off terms. These synthetic docs skip all of that. This matters because the corpus *does* contain framework boilerplate, and a real test would reveal whether RAG can help navigate and improve content embedded within that structure.

3. **No redaction artefacts.** The synthetic documents have no "[REDACTED]" blocks, no blanked-out names, no removed financial figures. Real documents have redaction gaps that create context loss and confuse both the LLM and the evaluator. Testing without redaction artefacts understates the difficulty of the real task.

4. **Uniform quality.** All five documents are written to roughly the same quality standard. A better test set would include one genuinely poor SoW (vague scope, missing sections, unclear deliverables) and one very polished one, to test whether RAG helps more when the starting quality is lower.

### Real Test Documents (available but not yet evaluated)

Four real redacted SoWs have been added to `test-sows/`:

| Document | Domain | Buyer | Supplier | Framework | Format | Est. Quality |
|----------|--------|-------|----------|-----------|--------|-------------|
| CCZN21A57 DOS5 Order Form | Digital (Contracts Finder/FTS) | Cabinet Office | NQC Ltd | DOS5 (RM1043.7) | Order Form + SoW ref | Adequate |
| RM1043 SK6 Lead Architect | Defence (Skynet 6 architecture) | Defence Digital (MoD) | People Source Consulting | DOS5 (RM1043.7) | Order Form only | Poor (as SoW) |
| RM6263 UC Continuity of Service | Digital (Universal Credit) | DWP | Astraeus Consulting | DSP (RM6263) | Order Form + rate card | Adequate |
| SoW1 Comms and Training | Digital (NDTP website/comms) | DBT | Southerly Communications | DSP (RM6263) | Full SoW template | Good |

#### Per-document assessment

**CCZN21A57 — Contracts Finder/FTS (Cabinet Office/NQC).** A DOS5 Lot 1 (Digital Outcomes) order form for maintaining and developing Contracts Finder and Find a Tender services. The document is primarily the framework order form — call-off terms, incorporated schedules, charges, liability caps, payment method — with the actual SoW content referenced via Call-Off Schedule 20 (not included in the PDF). What *is* visible: contract value (£4M), T&M pricing, 2-year term, Google Cloud subcontractor, Cyber Essentials Plus requirement. As test material for the SoW improver, it's challenging: most of the document is legal/commercial boilerplate that the system's 15 canonical sections won't match well. The actual deliverables and scope are in a separate document. This tests how the system handles SoWs where the interesting content is *not* in the uploaded PDF.

**RM1043 — Skynet 6 Lead Architect (MoD/People Source).** A DOS5 Lot 2 (Digital Specialists) order form for a single lead architect role on the Skynet 6 satellite communications programme. This is barely a SoW — it's an order form for contractor placement. No scope section, no deliverables table, no methodology. The "Call-Off Specification" (Schedule 20) contains a brief description of the architecture work. Includes DEFCON clauses, IR35 determination, MOD-specific security terms. Contract value £153K, 12 months, capped T&M. Realism is *high* — this is exactly what many government "SoWs" look like in practice. Baseline quality as a SoW is *poor* because it doesn't attempt to be one. This would be a valuable edge-case test: can the system handle a document that isn't structured as a traditional SoW?

**RM6263 — UC Continuity of Service (DWP/Astraeus).** A DSP Lot 2 order form for a 29-person development team maintaining Universal Credit digital services. Includes a detailed DDaT role/rate card table (9 role types, SFIA levels 3-6, all rates redacted). Contract value up to £9M over 2 years. Includes DWP-specific offshoring clauses, security requirements, and Cyber Essentials Plus. Like the others, the actual SoW content is in Call-Off Schedule 20. The rate card is the most interesting structural element — it's the kind of concrete tabular data the corpus also contains, so this tests whether RAG can improve commercial sections using similar corpus material.

**SoW1 — Comms and Training (DBT/Southerly Communications).** The most complete real SoW in the set. Full DSP template with milestones table (16 milestones with acceptance criteria and due dates), risk/assumption/dependency tables, supplier resource plan, rate card, and reporting requirements. The content is a 73-working-day engagement for website work, event support, and training material design for the National Digital Twin Programme. Baseline quality is *good* — well-structured, specific, with clear deliverables. There are genuine quality issues to fix: "communicatiosn" typo in the background, "identifcal" in the milestones, some milestone acceptance criteria are vague. This is the most suitable real document for testing RAG vs baseline because it has both structure the system can work with and genuine quality issues to improve.

#### Overall assessment of real test set

**Strengths compared to synthetic set:**

1. **Real framework boilerplate.** All four documents use actual CCS framework templates (DOS5, DSP). They contain the legal schedules, IR35 clauses, DEFCON references, and commercial terms that real SoWs live within. This tests whether the system can find and improve the actual SoW content within the noise of framework documentation.

2. **Genuine redaction.** Real "[REDACTED]" blocks, blanked-out names, removed financial figures. Tests the system's handling of incomplete text.

3. **Variable quality and structure.** From a single-role placement order form (SK6 Lead Architect) to a fully-populated SoW template (Comms and Training). Tests the system across a realistic range of document quality.

4. **Real typos and quality issues.** The Comms SoW has genuine spelling errors and some vague acceptance criteria — exactly the kind of issues the improvement pipeline should catch.

**Weaknesses:**

1. **Small set (4 documents).** Not enough for statistical significance, same as the synthetic set.

2. **Mostly order forms, not SoWs.** Three of four documents are primarily framework order forms where the actual SoW content is in a separate Call-Off Schedule 20 that isn't included. The system will see a lot of legal/commercial text and very little scope/deliverables content.

3. **Still HMRC-adjacent.** The Capgemini corpus documents overlap with two of these (CCZN21A57 and the DWP contract are in similar procurement frameworks). The MoD and DBT documents provide domain diversity.

---

## 5. Corpus Assessment

Unchanged from the 2026-03-19 report. In brief:

- **Too small** — 27 chunks from 4 documents (effectively 3 unique, as two Accenture documents are near-duplicates)
- **Single client** — all HMRC, from Accenture or Capgemini only
- **Domain mismatch** — corpus covers HMRC-specific SAP, HR/Finance, and Borders & Trade; test documents span cyber security, NHS data, MoJ training, FCDO communications
- **Redaction removes value** — concrete details stripped, leaving generic procurement prose

The one area where corpus material helps (Budget/Payment Terms) is where the corpus has the most concrete structural examples: rate tables, SFIA categories, payment milestone formats.

---

## 6. Qualitative Analysis

Same cases as the 2026-03-19 report (the data is unchanged):

### Case 1: Phase 2 Build and Test — RAG converts list to prose (worse)

**SOW_03 / Phase 2** — Quality: baseline 1, RAG 2. Faithfulness: baseline 1, RAG 0.

The original is a clean bullet list of Azure build tasks. Baseline preserved the list format with minor wording improvements. RAG converted the list into a single dense paragraph. Quality scored higher but faithfulness dropped to 0 — the evaluator penalised the structural transformation. RAG borrowed a "flowing prose" style from the corpus.

### Case 2: Deliverables table — RAG adds professional framing

**SOW_03 / Deliverables** — Quality: baseline 2, RAG 3. Faithfulness: baseline 1, RAG 0.83.

RAG added "The Supplier shall deliver the following outputs in accordance with the schedule specified below:" before the table. This is a genuine RAG contribution — the phrasing is visible in corpus documents. Quality 3, the highest in the experiment.

### Case 3: Objectives — RAG restructures prose into list

**SOW_01 / Objectives** — Quality: baseline 1, RAG 2. Faithfulness: both 1.

RAG broke a semicolon-separated list into proper bullets. Quality improved with perfect faithfulness.

### Case 4: Background — RAG barely changes anything

**SOW_02 / Background** — Quality: baseline 2, RAG 1. Faithfulness: baseline 0.92, RAG 1.

Baseline made substantive wording improvements; RAG version was nearly identical to the original. The retrieved chunk was completely irrelevant (supplier compliance obligations).

---

## 7. Discussion

### 7.1 The Test Document Problem

The most important finding from the document assessment is that **the synthetic test documents are too clean to meaningfully test the improvement pipeline.** Every original section is competently written, clearly structured, and internally consistent. When the starting quality is already adequate, both baseline and RAG produce only marginal wording-level changes — and the evaluator correctly scores these as low-impact (quality 1-2).

The real test documents tell a different story. The Comms SoW (Southerly/DBT) has genuine typos, vague acceptance criteria, and the kind of structural inconsistency that a real improvement system should catch. The order-form-heavy documents (CCZN21A57, SK6 Lead Architect, UC Continuity) present a different challenge: can the system identify and improve the actual SoW content buried within framework boilerplate?

Until the experiment is re-run with the real documents, we cannot assess whether RAG helps with genuinely imperfect SoWs.

### 7.2 Why RAG Still Doesn't Help (on these documents)

Same as the previous report: the corpus is too small, too homogeneous, and too heavily redacted. The retriever returns the same generic chunks for every section type. Context precision is 0.00 for 53/60 sections.

### 7.3 Evaluator Limitations

The evaluator scores all 60 originals at quality 1. The synthetic originals are competently written — they should not all score 1. Either the evaluator systematically underscores unimproved text (likely, given it's the same model that wrote the improvements), or the quality rubric is poorly calibrated.

### 7.4 Limitations

- **Evaluator bias.** Same model improves and evaluates.
- **Single-run variance.** No repeated trials.
- **Synthetic test documents.** All 5 test SoWs are fictional and uniformly well-written, making it hard for any improvement approach to demonstrate value.
- **Quality scale compression.** Scores cluster at 1-2; the 1-5 scale is effectively 1-3.
- **Thinking mode disabled.** Reasoning disabled for speed.

---

## 8. Recommendations

### Highest priority

1. **Re-run the experiment with the real test documents.** The 4 real redacted SoWs in `test-sows/` are more representative of actual system usage. They include framework boilerplate, redaction artefacts, variable quality, and genuine errors. This is the single highest-value next step — it tests whether the current findings hold when the input quality is more realistic. Run: `dotnet run --project SoWImprover.BatchRunner -- test-sows`

2. **Re-run evaluation with a cloud LLM evaluator.** Still the most important methodological improvement. The same-model evaluation bias is the biggest threat to result validity.

### Test set improvements

3. **Add a deliberately poor-quality SoW** to the test set — one with vague scope, missing sections, contradictory terms, and unclear deliverables. This tests whether RAG adds more value when the starting quality is lower (the hypothesis is yes, but we haven't tested it).

4. **Include at least one SoW close to the corpus domain** (HMRC, SAP/finance). The current synthetic set has SOW_02 (HMRC digital) which is domain-adjacent but not in the specific sub-domains the corpus covers. A test SoW about HMRC tax platform development or Borders & Trade would directly test whether domain-matched retrieval works.

### Corpus improvements

5. **Expand the corpus to 20+ documents** from multiple clients, sectors, and procurement frameworks. Still the fundamental bottleneck.

6. **Add unredacted or lightly-redacted structural examples.** Rate tables, deliverables matrices, and acceptance criteria templates are the highest-value additions.

### Methodological improvements

7. **Run multiple trials** (3-5 runs per document) to capture output variance.

8. **Switch to comparative evaluation** ("is A better than B?") rather than absolute scoring. Comparative judgements are more reliable for LLM evaluators than absolute 1-5 scales.

9. **Implement section-type-specific retrieval.** Tag chunks with their canonical section type at indexing time and filter retrieval to matching types.
