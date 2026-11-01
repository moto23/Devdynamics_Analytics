"""Index a fetched snapshot in local ChromaDB using LangChain embeddings."""
import argparse
import json
from pathlib import Path

from app.rag_pipeline import RepositoryRAG


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--snapshot", required=True, help="JSON file created by fetch_snapshot")
    args = parser.parse_args()
    snapshot = json.loads(Path(args.snapshot).read_text(encoding="utf-8"))
    count = RepositoryRAG().index(snapshot)
    print(f"Indexed {count} real GitHub activity records from {snapshot['repository']}")


if __name__ == "__main__":
    main()
