"""
Evaluates RAG vs non-RAG section improvements using Ragas metrics.
Reads JSON from stdin, writes JSON scores to stdout.

Input JSON shape:
{
  "endpoint": "http://127.0.0.1:5272/v1",
  "model_id": "mistral:7b",
  "sections": [
    {
      "original": "...",
      "baseline": "...",
      "rag_improved": "...",
      "retrieved_contexts": ["...", "..."],
      "definition_of_good": "..."
    }
  ]
}

Output: one JSON object per line (JSONL), flushed as each section completes:
{"index": 0, "baseline_quality": 3, "rag_quality": 4, "baseline_faithfulness": 0.9, "rag_faithfulness": 0.85, "context_precision": 0.9}

Install: pip install ragas openai
"""
import sys
import json
import asyncio
import traceback


def create_llm(endpoint, model_id):
    from openai import AsyncOpenAI
    from ragas.llms import llm_factory

    client = AsyncOpenAI(base_url=endpoint, api_key="foundry-local")
    return llm_factory(model_id, client=client)


def build_quality_rubric():
    return {
        "score1_description": "Output ignores quality standards entirely. No meaningful improvement over the original. Poor structure, vague language, missing key elements.",
        "score2_description": "Output addresses some quality standards but misses key requirements. Partial improvement with significant gaps in clarity or completeness.",
        "score3_description": "Output meets most quality standards. Acceptable professional quality with some room for improvement in structure or specificity.",
        "score4_description": "Output strongly adheres to quality standards. Clear, well-structured, and professional. Only minor improvements possible.",
        "score5_description": "Output fully meets all quality standards. Polished, comprehensive, professional SoW content. Excellent clarity and completeness.",
    }


async def score_faithfulness(llm, user_input, response, contexts):
    """Score faithfulness of a response against provided contexts (original text)."""
    from ragas.metrics.collections import Faithfulness

    scorer = Faithfulness(llm=llm, max_retries=3)
    result = await scorer.ascore(
        user_input=user_input,
        response=response,
        retrieved_contexts=contexts,
    )
    return round(float(result.value), 2) if result.value is not None else None


async def evaluate_section(section, llm):
    from ragas.metrics.collections import RubricsScoreWithoutReference
    from ragas.metrics.collections import ContextPrecisionWithoutReference

    results = {}
    rubrics = build_quality_rubric()

    original = section["original"]
    baseline = section["baseline"]
    rag_improved = section["rag_improved"]
    rag_contexts = section["retrieved_contexts"]
    definition = section["definition_of_good"]

    user_input = f"Improve this Statement of Work section to meet these quality standards:\n\nQUALITY STANDARDS:\n{definition}\n\nORIGINAL SECTION:\n{original}"

    # The original text is the context for faithfulness (did the LLM stay true to it?)
    original_as_context = [original]

    # Score original quality (1-5 rubric)
    try:
        scorer = RubricsScoreWithoutReference(rubrics=rubrics, llm=llm)
        result = await scorer.ascore(user_input=user_input, response=original)
        results["original_quality"] = int(result.value)
        print(f"  Original quality: {result.value}", file=sys.stderr)
    except Exception as e:
        print(f"Warning: original quality scoring failed: {type(e).__name__}: {e}", file=sys.stderr)
        results["original_quality"] = None

    # Score baseline quality (1-5 rubric)
    try:
        scorer = RubricsScoreWithoutReference(rubrics=rubrics, llm=llm)
        result = await scorer.ascore(user_input=user_input, response=baseline)
        results["baseline_quality"] = int(result.value)
        print(f"  Baseline quality: {result.value}", file=sys.stderr)
    except Exception as e:
        print(f"Warning: baseline quality scoring failed: {type(e).__name__}: {e}", file=sys.stderr)
        results["baseline_quality"] = None

    # Score RAG quality (1-5 rubric)
    try:
        scorer = RubricsScoreWithoutReference(rubrics=rubrics, llm=llm)
        result = await scorer.ascore(user_input=user_input, response=rag_improved)
        results["rag_quality"] = int(result.value)
        print(f"  RAG quality: {result.value}", file=sys.stderr)
    except Exception as e:
        print(f"Warning: RAG quality scoring failed: {type(e).__name__}: {e}", file=sys.stderr)
        results["rag_quality"] = None

    # Baseline faithfulness: is the baseline output faithful to the original text?
    try:
        score = await score_faithfulness(llm, user_input, baseline, original_as_context)
        results["baseline_faithfulness"] = score
        print(f"  Baseline faithfulness: {score}", file=sys.stderr)
    except Exception as e:
        print(f"Warning: baseline faithfulness scoring failed: {type(e).__name__}: {e}", file=sys.stderr)
        traceback.print_exc(file=sys.stderr)
        results["baseline_faithfulness"] = None

    # RAG faithfulness: is the RAG output faithful to the original text?
    try:
        score = await score_faithfulness(llm, user_input, rag_improved, original_as_context)
        results["rag_faithfulness"] = score
        print(f"  RAG faithfulness: {score}", file=sys.stderr)
    except Exception as e:
        print(f"Warning: RAG faithfulness scoring failed: {type(e).__name__}: {e}", file=sys.stderr)
        traceback.print_exc(file=sys.stderr)
        results["rag_faithfulness"] = None

    # Context precision: were the retrieved RAG chunks relevant? (RAG-only metric)
    if rag_contexts:
        try:
            scorer = ContextPrecisionWithoutReference(llm=llm)
            result = await scorer.ascore(
                user_input=user_input,
                response=rag_improved,
                retrieved_contexts=rag_contexts,
            )
            results["context_precision"] = round(float(result.value), 2) if result.value is not None else None
            print(f"  Context precision: {result.value}", file=sys.stderr)
        except Exception as e:
            print(f"Warning: context precision scoring failed: {type(e).__name__}: {e}", file=sys.stderr)
            traceback.print_exc(file=sys.stderr)
            results["context_precision"] = None
    else:
        print("  No retrieved contexts — skipping context precision", file=sys.stderr)
        results["context_precision"] = None

    return results


async def main():
    data = json.loads(sys.stdin.read())
    endpoint = data["endpoint"]
    model_id = data["model_id"]

    print(f"Using endpoint: {endpoint}, model: {model_id}", file=sys.stderr)
    print(f"Sections to evaluate: {len(data['sections'])}", file=sys.stderr)

    llm = create_llm(endpoint, model_id)

    for i, section in enumerate(data["sections"]):
        ctx_count = len(section.get("retrieved_contexts", []))
        print(f"Evaluating section {i + 1} of {len(data['sections'])} ({ctx_count} contexts)...", file=sys.stderr)
        scores = await evaluate_section(section, llm)
        scores["index"] = i
        sys.stdout.write(json.dumps(scores) + "\n")
        sys.stdout.flush()
        print(f"  Section {i + 1} complete: {json.dumps(scores)}", file=sys.stderr)


if __name__ == "__main__":
    asyncio.run(main())
