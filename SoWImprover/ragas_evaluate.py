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


async def evaluate_section(section, llm, emit, embeddings=None):
    """Evaluate a section, calling emit(results_dict) after each metric completes."""
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

    # Score original quality (1-5 rubric)
    try:
        scorer = RubricsScoreWithoutReference(rubrics=rubrics, llm=llm, max_retries=3)
        result = await scorer.ascore(user_input=user_input, response=original)
        results["original_quality"] = int(result.value)
        _log.info("original_quality = %s", result.value)
    except Exception as e:
        _log.error("original_quality FAILED: %s: %s", type(e).__name__, e)
        _log.debug(traceback.format_exc())
        results["original_quality"] = None
    emit(results)

    # Score baseline quality (1-5 rubric)
    try:
        scorer = RubricsScoreWithoutReference(rubrics=rubrics, llm=llm, max_retries=3)
        result = await scorer.ascore(user_input=user_input, response=baseline)
        results["baseline_quality"] = int(result.value)
        _log.info("baseline_quality = %s", result.value)
    except Exception as e:
        _log.error("baseline_quality FAILED: %s: %s", type(e).__name__, e)
        _log.debug(traceback.format_exc())
        results["baseline_quality"] = None
    emit(results)

    # Score RAG quality (1-5 rubric)
    try:
        scorer = RubricsScoreWithoutReference(rubrics=rubrics, llm=llm, max_retries=3)
        result = await scorer.ascore(user_input=user_input, response=rag_improved)
        results["rag_quality"] = int(result.value)
        _log.info("rag_quality = %s", result.value)
    except Exception as e:
        _log.error("rag_quality FAILED: %s: %s", type(e).__name__, e)
        _log.debug(traceback.format_exc())
        results["rag_quality"] = None
    emit(results)

    # Baseline faithfulness: is the baseline output faithful to the original text?
    try:
        score = await score_faithfulness(llm, user_input, baseline, original_as_context)
        results["baseline_faithfulness"] = score
        _log.info("baseline_faithfulness = %s", score)
    except Exception as e:
        _log.error("baseline_faithfulness FAILED: %s: %s", type(e).__name__, e)
        _log.debug(traceback.format_exc())
        results["baseline_faithfulness"] = None
    emit(results)

    # RAG faithfulness: is the RAG output faithful to the original text?
    try:
        score = await score_faithfulness(llm, user_input, rag_improved, original_as_context)
        results["rag_faithfulness"] = score
        _log.info("rag_faithfulness = %s", score)
    except Exception as e:
        _log.error("rag_faithfulness FAILED: %s: %s", type(e).__name__, e)
        _log.debug(traceback.format_exc())
        results["rag_faithfulness"] = None
    emit(results)

    # Context precision: were the retrieved RAG chunks relevant? (RAG-only metric)
    if rag_contexts:
        try:
            scorer = ContextPrecisionWithoutReference(llm=llm, max_retries=3)
            result = await scorer.ascore(
                user_input=user_input,
                response=rag_improved,
                retrieved_contexts=rag_contexts,
            )
            results["context_precision"] = round(float(result.value), 2) if result.value is not None else None
            _log.info("context_precision = %s", result.value)
        except Exception as e:
            _log.error("context_precision FAILED: %s: %s", type(e).__name__, e)
            _log.debug(traceback.format_exc())
            results["context_precision"] = None
        emit(results)

    # ── Factual Correctness: claim-level F1 of output vs original ──
    # Baseline factual correctness
    try:
        scorer = FactualCorrectness(llm=llm, mode="f1", max_retries=3)
        result = await scorer.ascore(response=baseline, reference=original)
        results["baseline_factual_correctness"] = round(float(result.value), 2) if result.value is not None else None
        _log.info("baseline_factual_correctness = %s", result.value)
    except Exception as e:
        _log.error("baseline_factual_correctness FAILED: %s: %s", type(e).__name__, e)
        _log.debug(traceback.format_exc())
        results["baseline_factual_correctness"] = None
    emit(results)

    # RAG factual correctness
    try:
        scorer = FactualCorrectness(llm=llm, mode="f1", max_retries=3)
        result = await scorer.ascore(response=rag_improved, reference=original)
        results["rag_factual_correctness"] = round(float(result.value), 2) if result.value is not None else None
        _log.info("rag_factual_correctness = %s", result.value)
    except Exception as e:
        _log.error("rag_factual_correctness FAILED: %s: %s", type(e).__name__, e)
        _log.debug(traceback.format_exc())
        results["rag_factual_correctness"] = None
    emit(results)

    # ── Context Recall: did retrieval cover the info in the original? (RAG-only) ──
    if rag_contexts:
        try:
            scorer = ContextRecall(llm=llm, max_retries=3)
            result = await scorer.ascore(
                user_input=user_input,
                retrieved_contexts=rag_contexts,
                reference=original,
            )
            results["context_recall"] = round(float(result.value), 2) if result.value is not None else None
            _log.info("context_recall = %s", result.value)
        except Exception as e:
            _log.error("context_recall FAILED: %s: %s", type(e).__name__, e)
            _log.debug(traceback.format_exc())
            results["context_recall"] = None
        emit(results)

    # ── Response Relevancy: did the output stay on-task? (needs embeddings) ──
    if embeddings is not None:
        # Baseline response relevancy
        try:
            from ragas.metrics.collections import AnswerRelevancy

            scorer = AnswerRelevancy(llm=llm, embeddings=embeddings, max_retries=3)
            result = await scorer.ascore(user_input=user_input, response=baseline)
            val = max(0.0, float(result.value)) if result.value is not None else None
            results["baseline_response_relevancy"] = round(val, 2) if val is not None else None
            _log.info("baseline_response_relevancy = %s", val)
        except Exception as e:
            _log.error("baseline_response_relevancy FAILED: %s: %s", type(e).__name__, e)
            _log.debug(traceback.format_exc())
            results["baseline_response_relevancy"] = None
        emit(results)

        # RAG response relevancy
        try:
            from ragas.metrics.collections import AnswerRelevancy

            scorer = AnswerRelevancy(llm=llm, embeddings=embeddings, max_retries=3)
            result = await scorer.ascore(user_input=user_input, response=rag_improved)
            val = max(0.0, float(result.value)) if result.value is not None else None
            results["rag_response_relevancy"] = round(val, 2) if val is not None else None
            _log.info("rag_response_relevancy = %s", val)
        except Exception as e:
            _log.error("rag_response_relevancy FAILED: %s: %s", type(e).__name__, e)
            _log.debug(traceback.format_exc())
            results["rag_response_relevancy"] = None
        emit(results)

    # ── Noise Sensitivity: did irrelevant chunks hurt the RAG output? (RAG-only) ──
    if rag_contexts:
        try:
            scorer = NoiseSensitivity(llm=llm, max_retries=3)
            result = await scorer.ascore(
                user_input=user_input,
                response=rag_improved,
                reference=original,
                retrieved_contexts=rag_contexts,
            )
            results["noise_sensitivity"] = round(float(result.value), 2) if result.value is not None else None
            _log.info("noise_sensitivity = %s", result.value)
        except Exception as e:
            _log.error("noise_sensitivity FAILED: %s: %s", type(e).__name__, e)
            _log.debug(traceback.format_exc())
            results["noise_sensitivity"] = None
        emit(results)


async def main():
    data = json.loads(sys.stdin.read())
    endpoint = data["endpoint"]
    model_id = data["model_id"]

    embedding_model_id = data.get("embedding_model_id")

    _log.info("=== Ragas evaluation starting ===")
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

        await evaluate_section(section, llm, emit, embeddings)
        _log.info("Section %d complete", i + 1)
        print(f"  Section {i + 1} complete", file=sys.stderr)

    _log.info("=== Ragas evaluation complete ===")


if __name__ == "__main__":
    asyncio.run(main())
