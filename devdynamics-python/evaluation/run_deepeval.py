"""Evaluate saved, real RAG answers without inventing scores."""
import argparse
import json
import os
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description="Run DeepEval metrics on saved Q&A JSONL")
    parser.add_argument("--cases", required=True, help="JSONL generated with --eval-output")
    parser.add_argument("--model", default=os.getenv("DEEPEVAL_JUDGE_MODEL", "gpt-4.1"))
    parser.add_argument("--faithfulness", action="store_true", help="Evaluate answer grounding too")
    args = parser.parse_args()
    if not os.getenv("OPENAI_API_KEY"):
        parser.error("OPENAI_API_KEY required by the default DeepEval judge")
    from deepeval.metrics import AnswerRelevancyMetric, FaithfulnessMetric
    from deepeval.test_case import LLMTestCase

    lines = Path(args.cases).read_text(encoding="utf-8").splitlines()
    if not lines:
        parser.error("No cases found. First run scripts.ask_repository with --eval-output")
    results = []
    for line_num, line in enumerate(lines, 1):
        if not line.strip():
            continue
        row = json.loads(line)
        test_case = LLMTestCase(
            input=row["input"], actual_output=row["actual_output"],
            retrieval_context=row.get("retrieval_context", []),
        )
        relevance = AnswerRelevancyMetric(model=args.model, include_reason=True)
        relevance.measure(test_case)
        item = {"case": line_num, "answer_relevance": relevance.score,
                "reason": relevance.reason}
        if args.faithfulness:
            if not test_case.retrieval_context:
                item["faithfulness_error"] = "No retrieval context was saved for this case"
            else:
                faithful = FaithfulnessMetric(model=args.model, include_reason=True)
                faithful.measure(test_case)
                item["faithfulness"] = faithful.score
                item["faithfulness_reason"] = faithful.reason
        results.append(item)
        print(json.dumps(item, ensure_ascii=False))
    scores = [r["answer_relevance"] for r in results if isinstance(r["answer_relevance"], (int, float))]
    if scores:
        print(json.dumps({"cases": len(results), "mean_answer_relevance": sum(scores) / len(scores)}))


if __name__ == "__main__":
    main()
