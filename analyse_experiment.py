"""
Analyse experiment-results.json and produce structured markdown tables
for the experiment report. Run from the repo root:

    python analyse_experiment.py [path/to/experiment-results.json]

Defaults to experiment-results.json in the current directory.
"""

import json
import sys
from collections import defaultdict
from pathlib import Path


def load(path: str) -> dict:
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def fmt(v, decimals=2):
    """Format a numeric value, handling None."""
    if v is None:
        return "null"
    if isinstance(v, float):
        return f"{v:.{decimals}f}"
    return str(v)


def section_rows(data: dict) -> list[dict]:
    """Flatten all evaluated sections into a list of dicts."""
    rows = []
    for doc in data["testDocuments"]:
        for sec in doc["sections"]:
            if sec["unrecognised"]:
                continue
            s = sec["scores"]
            if s is None:
                continue
            rows.append({
                "doc": doc["fileName"],
                "section": sec["sectionName"],
                "canonical": sec.get("matchedCanonicalSection", ""),
                "origQ": s.get("originalQualityScore"),
                "baseQ": s.get("baselineQualityScore"),
                "ragQ": s.get("ragQualityScore"),
                "baseF": s.get("baselineFaithfulnessScore"),
                "ragF": s.get("ragFaithfulnessScore"),
                "baseFC": s.get("baselineFactualCorrectnessScore"),
                "ragFC": s.get("ragFactualCorrectnessScore"),
                "baseRR": s.get("baselineResponseRelevancyScore"),
                "ragRR": s.get("ragResponseRelevancyScore"),
                "ctxP": s.get("contextPrecisionScore"),
                "ctxR": s.get("contextRecallScore"),
                "noise": s.get("noiseSensitivityScore"),
                "nChunks": sec.get("retrievedChunkCount", 0),
                "scores_list": sec.get("retrievedScores") or [],
            })
    return rows


def mean(vals: list[float]) -> float:
    return sum(vals) / len(vals) if vals else 0.0


def median(vals: list[float]) -> float:
    if not vals:
        return 0.0
    s = sorted(vals)
    n = len(s)
    if n % 2 == 1:
        return s[n // 2]
    return (s[n // 2 - 1] + s[n // 2]) / 2


def head_to_head(rows: list[dict], base_key: str, rag_key: str) -> tuple[int, int, int]:
    """Count sections where RAG > baseline, equal, RAG < baseline."""
    gt, eq, lt = 0, 0, 0
    for r in rows:
        b, rg = r[base_key], r[rag_key]
        if b is None or rg is None:
            continue
        if rg > b:
            gt += 1
        elif rg < b:
            lt += 1
        else:
            eq += 1
    return gt, eq, lt


def print_corpus_metadata(data: dict):
    c = data["corpus"]
    print("## Corpus Metadata\n")
    print(f"| Field | Value |")
    print(f"|-------|-------|")
    print(f"| Folder | `{c['folder']}` |")
    print(f"| Documents | {', '.join(c['documents'])} |")
    print(f"| Total Chunks | {c['totalChunks']} |")
    print(f"| Embedding Model | {c['embeddingModel']} |")
    print(f"| Chat Model | {c['chatModel']} |")
    print()


def print_test_documents(data: dict):
    print("## Test Documents\n")
    for doc in data["testDocuments"]:
        print(f"### {doc['fileName']}")
        print(f"- Sections: {doc['sectionCount']} total, {doc['evaluatedSectionCount']} evaluated")
        summary = doc.get("evaluationSummary", "(none)")
        print(f"- Summary: {summary}")
        print()


def print_full_scores_table(rows: list[dict]):
    print("## Full Scores Table\n")
    print("| Document | Section | Canonical | OrigQ | BaseQ | RagQ | BaseF | RagF | BaseFC | RagFC | BaseRR | RagRR | CtxP | CtxR | Noise |")
    print("|----------|---------|-----------|-------|-------|------|-------|------|--------|-------|--------|-------|------|------|-------|")
    for r in rows:
        doc_short = r["doc"][:35]
        print(f"| {doc_short} | {r['section']} | {r['canonical']} | "
              f"{fmt(r['origQ'])} | {fmt(r['baseQ'])} | {fmt(r['ragQ'])} | "
              f"{fmt(r['baseF'])} | {fmt(r['ragF'])} | "
              f"{fmt(r['baseFC'])} | {fmt(r['ragFC'])} | "
              f"{fmt(r['baseRR'])} | {fmt(r['ragRR'])} | "
              f"{fmt(r['ctxP'])} | {fmt(r['ctxR'])} | {fmt(r['noise'])} |")
    print()


def print_aggregate_comparison(rows: list[dict]):
    print("## Aggregate RAG vs Baseline Comparison\n")

    metrics = [
        ("Quality (1-5)", "baseQ", "ragQ"),
        ("Faithfulness (0-1)", "baseF", "ragF"),
        ("Factual Correctness (0-1)", "baseFC", "ragFC"),
        ("Response Relevancy (0-1)", "baseRR", "ragRR"),
    ]

    print("| Metric | Baseline Mean | RAG Mean | Baseline Median | RAG Median |")
    print("|--------|-------------|---------|----------------|-----------|")
    for label, bk, rk in metrics:
        bvals = [r[bk] for r in rows if r[bk] is not None]
        rvals = [r[rk] for r in rows if r[rk] is not None]
        print(f"| {label} | {fmt(mean(bvals))} | {fmt(mean(rvals))} | {fmt(median(bvals))} | {fmt(median(rvals))} |")
    print()

    # RAG-only metrics
    print("### RAG-Specific Metrics\n")
    print("| Metric | Mean | Median |")
    print("|--------|------|--------|")
    for label, key in [("Context Precision", "ctxP"), ("Context Recall", "ctxR"), ("Noise Sensitivity", "noise")]:
        vals = [r[key] for r in rows if r[key] is not None]
        print(f"| {label} | {fmt(mean(vals))} | {fmt(median(vals))} |")
    print()


def print_head_to_head(rows: list[dict]):
    print("## Head-to-Head Comparison\n")
    pairs = [
        ("Quality", "baseQ", "ragQ"),
        ("Faithfulness", "baseF", "ragF"),
        ("Factual Correctness", "baseFC", "ragFC"),
        ("Response Relevancy", "baseRR", "ragRR"),
    ]
    n = len(rows)
    print(f"| Metric | RAG > Baseline | RAG = Baseline | RAG < Baseline |")
    print(f"|--------|---------------|----------------|----------------|")
    for label, bk, rk in pairs:
        gt, eq, lt = head_to_head(rows, bk, rk)
        print(f"| {label} | {gt} ({100*gt//n}%) | {eq} ({100*eq//n}%) | {lt} ({100*lt//n}%) |")
    print()


def print_per_canonical_section(rows: list[dict]):
    print("## Per Canonical Section Breakdown\n")
    by_canon = defaultdict(list)
    for r in rows:
        by_canon[r["canonical"]].append(r)

    for canon in sorted(by_canon):
        sections = by_canon[canon]
        n = len(sections)
        avg_baseQ = mean([s["baseQ"] for s in sections if s["baseQ"] is not None])
        avg_ragQ = mean([s["ragQ"] for s in sections if s["ragQ"] is not None])
        print(f"### {canon} (n={n}, avg quality: baseline={fmt(avg_baseQ)}, RAG={fmt(avg_ragQ)})\n")
        for s in sections:
            noise_str = fmt(s["noise"])
            print(f"- **{s['doc']}** | {s['section']}")
            print(f"  Q: orig={fmt(s['origQ'])} base={fmt(s['baseQ'])} rag={fmt(s['ragQ'])}  "
                  f"F: base={fmt(s['baseF'])} rag={fmt(s['ragF'])}  "
                  f"FC: base={fmt(s['baseFC'])} rag={fmt(s['ragFC'])}  "
                  f"RR: base={fmt(s['baseRR'])} rag={fmt(s['ragRR'])}  "
                  f"CtxP={fmt(s['ctxP'])} CtxR={fmt(s['ctxR'])} Noise={noise_str}")
        print()


def print_retrieval_analysis(rows: list[dict]):
    print("## Retrieval Analysis\n")

    all_scores = []
    for r in rows:
        all_scores.extend(r["scores_list"])

    if not all_scores:
        print("No retrieval scores found.\n")
        return

    print(f"### Similarity Score Distribution (n={len(all_scores)} retrieved chunks)\n")
    print(f"| Statistic | Value |")
    print(f"|-----------|-------|")
    print(f"| Mean | {fmt(mean(all_scores), 4)} |")
    print(f"| Median | {fmt(median(all_scores), 4)} |")
    print(f"| Min | {fmt(min(all_scores), 4)} |")
    print(f"| Max | {fmt(max(all_scores), 4)} |")
    print()

    print(f"### Threshold Distribution\n")
    print(f"| Threshold | Count | Percentage |")
    print(f"|-----------|-------|------------|")
    for t in [0.3, 0.4, 0.5, 0.6, 0.7, 0.8]:
        count = sum(1 for s in all_scores if s >= t)
        pct = 100 * count / len(all_scores) if all_scores else 0
        print(f"| >= {t} | {count}/{len(all_scores)} | {pct:.0f}% |")
    print()


def print_qualitative_samples(data: dict, n_samples: int = 4):
    """Print original/baseline/RAG content for sections with the most interesting score deltas."""
    print("## Qualitative Samples\n")
    print("Sections selected by largest absolute quality or faithfulness delta between RAG and baseline.\n")

    candidates = []
    for doc in data["testDocuments"]:
        for sec in doc["sections"]:
            if sec["unrecognised"] or sec["scores"] is None:
                continue
            s = sec["scores"]
            bq = s.get("baselineQualityScore") or 0
            rq = s.get("ragQualityScore") or 0
            bf = s.get("baselineFaithfulnessScore") or 0
            rf = s.get("ragFaithfulnessScore") or 0
            delta = abs(rq - bq) + abs(rf - bf)
            candidates.append((delta, doc["fileName"], sec))

    candidates.sort(key=lambda x: -x[0])

    for _, doc_name, sec in candidates[:n_samples]:
        s = sec["scores"]
        print(f"### {doc_name} | {sec['sectionName']}")
        print(f"Canonical: {sec.get('matchedCanonicalSection', 'N/A')}")
        print(f"Quality: orig={fmt(s.get('originalQualityScore'))} "
              f"base={fmt(s.get('baselineQualityScore'))} "
              f"rag={fmt(s.get('ragQualityScore'))}")
        print(f"Faithfulness: base={fmt(s.get('baselineFaithfulnessScore'))} "
              f"rag={fmt(s.get('ragFaithfulnessScore'))}")
        print()

        orig = sec.get("originalContent", "") or ""
        base = sec.get("baselineContent", "") or ""
        rag = sec.get("ragContent", "") or ""

        print("**Original** (first 500 chars):")
        print(f"```\n{orig[:500]}\n```\n")
        print("**Baseline** (first 500 chars):")
        print(f"```\n{base[:500]}\n```\n")
        print("**RAG** (first 500 chars):")
        print(f"```\n{rag[:500]}\n```\n")

        scores_list = sec.get("retrievedScores") or []
        contexts = sec.get("retrievedContexts") or []
        if scores_list and contexts:
            print(f"**Top retrieved chunk** (similarity={fmt(scores_list[0], 4)}):")
            print(f"```\n{contexts[0][:300]}\n```\n")

        print("---\n")


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else "experiment-results.json"
    if not Path(path).exists():
        print(f"ERROR: {path} not found. Run the batch runner first:", file=sys.stderr)
        print(f'  dotnet run --project SoWImprover.BatchRunner -- "<test-folder-path>"', file=sys.stderr)
        sys.exit(1)

    data = load(path)
    rows = section_rows(data)

    print(f"# Experiment Analysis — {data.get('exportedAt', 'unknown date')}\n")
    print(f"Evaluated sections: {len(rows)}\n")

    print_corpus_metadata(data)
    print_test_documents(data)
    print_full_scores_table(rows)
    print_aggregate_comparison(rows)
    print_head_to_head(rows)
    print_per_canonical_section(rows)
    print_retrieval_analysis(rows)
    print_qualitative_samples(data)


if __name__ == "__main__":
    main()
