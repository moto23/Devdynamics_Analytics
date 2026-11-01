"""Ask a grounded question and optionally save the answer for DeepEval."""
import argparse
import json
from pathlib import Path

from app.rag_pipeline import RepositoryRAG


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", required=True, help="owner/name")
    parser.add_argument("--question", required=True)
    parser.add_argument("--top-k", type=int, default=4)
    parser.add_argument("--eval-output", help="Append a real Q&A evaluation case (JSONL)")
    args = parser.parse_args()
    result = RepositoryRAG().ask(args.question, args.repo, args.top_k)
    print(json.dumps(result, indent=2, ensure_ascii=False))
    if args.eval_output:
        path = Path(args.eval_output)
        path.parent.mkdir(parents=True, exist_ok=True)
        case = {"input": result["question"], "actual_output": result["answer"],
                "retrieval_context": result["retrieval_context"]}
        with path.open("a", encoding="utf-8") as target:
            target.write(json.dumps(case, ensure_ascii=False) + "\n")
        print(f"Appended evaluation case to {path}")


if __name__ == "__main__":
    main()
