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
import os
import logging

# Log everything to a fixed path for easy tailing
_log_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "ragas_debug.log")
logging.basicConfig(
    filename=_log_path,
    level=logging.DEBUG,
    format="%(asctime)s %(levelname)s %(message)s",
    filemode="w",  # overwrite each run
)
_log = logging.getLogger("ragas_evaluate")


def _fix_json_floats_to_ints(text):
    """Fix LLM responses where floats appear where Ragas expects integers.

    Ragas RubricsScoreWithoutReference expects {"score": 3} but local LLMs
    sometimes return {"score": 0.5} or {"score": 3.0}. This finds JSON-like
    "score": <float> patterns and rounds them to the nearest int, clamped 1-5.
    """
    import re

    def _round_score(m):
        try:
            val = float(m.group(1))
            rounded = max(1, min(5, round(val)))
            return f'"score": {rounded}'
        except ValueError:
            return m.group(0)

    return re.sub(r'"score"\s*:\s*([\d.]+)', _round_score, text)


def create_llm(endpoint, model_id):
    import functools
    import copy
    from openai import AsyncOpenAI
    from ragas.llms import llm_factory

    client = AsyncOpenAI(base_url=endpoint, api_key="foundry-local")

    # Ragas metrics like FactualCorrectness and NoiseSensitivity decompose text
    # into claims, which can produce long outputs. Some metrics explicitly pass
    # a low max_tokens — override it to ensure at least 4096 output tokens.
    #
    # Also post-process responses to fix floats where Ragas expects integers
    # (e.g. rubric scores) — local LLMs often return 0.5 instead of 1.
    _original_create = client.chat.completions.create
    _min_max_tokens = 4096

    @functools.wraps(_original_create)
    async def _patched_create(*args, **kwargs):
        if kwargs.get("max_tokens") is None or kwargs["max_tokens"] < _min_max_tokens:
            kwargs["max_tokens"] = _min_max_tokens

        # Disable thinking/reasoning for evaluation calls — scoring is mechanical
        # and doesn't benefit from chain-of-thought. Ollama maps this to think=false.
        extra_body = kwargs.get("extra_body", {}) or {}
        extra_body["reasoning_effort"] = "none"
        kwargs["extra_body"] = extra_body

        # Log the request messages (last message only, truncated)
        messages = kwargs.get("messages", args[0] if args else [])
        if messages:
            last_msg = messages[-1] if isinstance(messages, list) else messages
            content = last_msg.get("content", "") if isinstance(last_msg, dict) else str(last_msg)
            _log.debug("LLM request (last msg, first 200 chars): %s", content[:200])

        response = await _original_create(*args, **kwargs)

        # Log and fix float-where-int-expected in LLM responses
        for choice in response.choices:
            if choice.message and choice.message.content:
                original_content = choice.message.content
                choice.message.content = _fix_json_floats_to_ints(original_content)
                if original_content != choice.message.content:
                    _log.info("Fixed float-to-int in response: %s -> %s",
                              original_content[:200], choice.message.content[:200])
                _log.debug("LLM response (first 500 chars): %s", choice.message.content[:500])
            if choice.finish_reason == "length":
                _log.warning("Response truncated (finish_reason=length), max_tokens=%s", kwargs.get("max_tokens"))

        return response

    client.chat.completions.create = _patched_create

    return llm_factory(model_id, client=client), client


def create_embeddings(client, embedding_model_id):
    if not embedding_model_id:
        return None
    from ragas.embeddings.base import embedding_factory

    return embedding_factory("openai", model=embedding_model_id, client=client)


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


async def _eval_metric(name, coro, results, log):
    """Run a single metric coroutine, store result in *results* dict, and log."""
    try:
        value = await coro
        results[name] = value
        log.info("%s = %s", name, value)
    except Exception as e:
        log.error("%s FAILED: %s: %s", name, type(e).__name__, e)
        log.debug(traceback.format_exc())
        results[name] = None


async def evaluate_section(section, llm, emit, embeddings=None, parallel=False):
    """Evaluate a section, calling emit(results_dict) after each metric completes.

    When *parallel* is True, all metrics run concurrently via asyncio.gather
    and emit is called once at the end with all results.
    """
    from ragas.metrics.collections import RubricsScoreWithoutReference
    from ragas.metrics.collections import ContextPrecisionWithoutReference
    from ragas.metrics.collections import FactualCorrectness
    from ragas.metrics.collections import ContextRecall
    from ragas.metrics.collections import NoiseSensitivity

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

    # ── Build list of (name, coroutine) for all metrics ──

    async def _original_quality():
        scorer = RubricsScoreWithoutReference(rubrics=rubrics, llm=llm, max_retries=3)
        result = await scorer.ascore(user_input=user_input, response=original)
        return int(result.value)

    async def _baseline_quality():
        scorer = RubricsScoreWithoutReference(rubrics=rubrics, llm=llm, max_retries=3)
        result = await scorer.ascore(user_input=user_input, response=baseline)
        return int(result.value)

    async def _rag_quality():
        scorer = RubricsScoreWithoutReference(rubrics=rubrics, llm=llm, max_retries=3)
        result = await scorer.ascore(user_input=user_input, response=rag_improved)
        return int(result.value)

    async def _baseline_faithfulness():
        return await score_faithfulness(llm, user_input, baseline, original_as_context)

    async def _rag_faithfulness():
        return await score_faithfulness(llm, user_input, rag_improved, original_as_context)

    async def _context_precision():
        scorer = ContextPrecisionWithoutReference(llm=llm, max_retries=3)
        result = await scorer.ascore(
            user_input=user_input,
            response=rag_improved,
            retrieved_contexts=rag_contexts,
        )
        return round(float(result.value), 2) if result.value is not None else None

    async def _baseline_factual_correctness():
        scorer = FactualCorrectness(llm=llm, mode="f1", max_retries=3)
        result = await scorer.ascore(response=baseline, reference=original)
        return round(float(result.value), 2) if result.value is not None else None

    async def _rag_factual_correctness():
        scorer = FactualCorrectness(llm=llm, mode="f1", max_retries=3)
        result = await scorer.ascore(response=rag_improved, reference=original)
        return round(float(result.value), 2) if result.value is not None else None

    async def _context_recall():
        scorer = ContextRecall(llm=llm, max_retries=3)
        result = await scorer.ascore(
            user_input=user_input,
            retrieved_contexts=rag_contexts,
            reference=original,
        )
        return round(float(result.value), 2) if result.value is not None else None

    async def _baseline_response_relevancy():
        from ragas.metrics.collections import AnswerRelevancy
        scorer = AnswerRelevancy(llm=llm, embeddings=embeddings, max_retries=3)
        result = await scorer.ascore(user_input=user_input, response=baseline)
        val = max(0.0, float(result.value)) if result.value is not None else None
        return round(val, 2) if val is not None else None

    async def _rag_response_relevancy():
        from ragas.metrics.collections import AnswerRelevancy
        scorer = AnswerRelevancy(llm=llm, embeddings=embeddings, max_retries=3)
        result = await scorer.ascore(user_input=user_input, response=rag_improved)
        val = max(0.0, float(result.value)) if result.value is not None else None
        return round(val, 2) if val is not None else None

    async def _noise_sensitivity():
        scorer = NoiseSensitivity(llm=llm, max_retries=3)
        result = await scorer.ascore(
            user_input=user_input,
            response=rag_improved,
            reference=original,
            retrieved_contexts=rag_contexts,
        )
        return round(float(result.value), 2) if result.value is not None else None

    # Always-run metrics
    metrics = [
        ("original_quality", _original_quality()),
        ("baseline_quality", _baseline_quality()),
        ("rag_quality", _rag_quality()),
        ("baseline_faithfulness", _baseline_faithfulness()),
        ("rag_faithfulness", _rag_faithfulness()),
        ("baseline_factual_correctness", _baseline_factual_correctness()),
        ("rag_factual_correctness", _rag_factual_correctness()),
    ]

    # Conditional metrics
    if rag_contexts:
        metrics.append(("context_precision", _context_precision()))
        metrics.append(("context_recall", _context_recall()))
        metrics.append(("noise_sensitivity", _noise_sensitivity()))

    if embeddings is not None:
        metrics.append(("baseline_response_relevancy", _baseline_response_relevancy()))
        metrics.append(("rag_response_relevancy", _rag_response_relevancy()))

    if parallel:
        # Run all metrics concurrently, emit once at the end
        await asyncio.gather(
            *[_eval_metric(name, coro, results, _log) for name, coro in metrics]
        )
        emit(results)
    else:
        # Run sequentially with progressive emit after each metric
        for name, coro in metrics:
            await _eval_metric(name, coro, results, _log)
            emit(results)


async def main():
    parallel = "--parallel" in sys.argv

    data = json.loads(sys.stdin.read())
    endpoint = data["endpoint"]
    model_id = data["model_id"]

    embedding_model_id = data.get("embedding_model_id")

    _log.info("=== Ragas evaluation starting ===")
    _log.info("Mode: %s", "parallel" if parallel else "sequential")
    _log.info("Endpoint: %s, model: %s, embedding: %s", endpoint, model_id, embedding_model_id or "(none)")
    _log.info("Sections to evaluate: %d", len(data["sections"]))
    print(f"Using endpoint: {endpoint}, model: {model_id}", file=sys.stderr)
    if embedding_model_id:
        print(f"Embedding model: {embedding_model_id}", file=sys.stderr)
    else:
        print("No embedding model configured — response relevancy will be skipped", file=sys.stderr)
    print(f"Sections to evaluate: {len(data['sections'])}", file=sys.stderr)

    llm, client = create_llm(endpoint, model_id)
    embeddings = create_embeddings(client, embedding_model_id)

    for i, section in enumerate(data["sections"]):
        ctx_count = len(section.get("retrieved_contexts", []))
        _log.info("--- Section %d/%d (%d contexts) ---", i + 1, len(data["sections"]), ctx_count)
        print(f"Evaluating section {i + 1} of {len(data['sections'])} ({ctx_count} contexts)...", file=sys.stderr)

        def emit(results, idx=i):
            """Emit cumulative results for this section after each metric."""
            out = dict(results)
            out["index"] = idx
            line = json.dumps(out)
            _log.debug("EMIT: %s", line)
            sys.stdout.write(line + "\n")
            sys.stdout.flush()

        await evaluate_section(section, llm, emit, embeddings, parallel=parallel)
        _log.info("Section %d complete", i + 1)
        print(f"  Section {i + 1} complete", file=sys.stderr)

    _log.info("=== Ragas evaluation complete ===")


if __name__ == "__main__":
    asyncio.run(main())
