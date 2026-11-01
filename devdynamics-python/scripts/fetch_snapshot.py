"""Fetch live GitHub data, create analytics JSON, optionally save to MongoDB."""
import argparse
import json
from pathlib import Path

from app.analytics import analyze
from app.config import Settings
from app.github_client import GitHubClient
from app.storage import save_snapshot


def main():
    parser = argparse.ArgumentParser(description="Fetch a GitHub repository snapshot")
    parser.add_argument("--repo", required=True, help="owner/name")
    parser.add_argument("--days", type=int, default=30)
    parser.add_argument("--output", default="data/repository_snapshot.json")
    parser.add_argument("--save-mongo", action="store_true")
    args = parser.parse_args()
    try:
        owner, name = args.repo.split("/")
    except ValueError:
        parser.error("--repo must look like owner/name")
    settings = Settings()
    snapshot = GitHubClient(settings).snapshot(owner, name, days=args.days)
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(snapshot, indent=2, ensure_ascii=False), encoding="utf-8")
    print(f"Saved snapshot: {output}")
    print(json.dumps(analyze(snapshot), indent=2))
    if args.save_mongo:
        save_snapshot(snapshot, settings.mongodb_uri, settings.mongodb_db)
        print("Saved latest repository snapshot to MongoDB")


if __name__ == "__main__":
    main()
