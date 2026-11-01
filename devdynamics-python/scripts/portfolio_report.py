"""Compare multiple real snapshots and export a Markdown analytics report."""
import argparse
import json
from pathlib import Path

from app.reporting import portfolio_report, to_markdown


def main():
    parser = argparse.ArgumentParser(description="Compare GitHub repository snapshots")
    parser.add_argument("--snapshots", nargs="+", required=True, help="One or more snapshot JSON files")
    parser.add_argument("--output", default="data/portfolio_report.md")
    args = parser.parse_args()

    snapshots = [json.loads(Path(name).read_text(encoding="utf-8")) for name in args.snapshots]
    report = portfolio_report(snapshots)
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(to_markdown(report), encoding="utf-8")
    print(f"Wrote comparison for {report['repository_count']} repositories to {output}")
    if report["is_partial"]:
        print("WARNING: Some data is incomplete due to GitHub API pagination budget")


if __name__ == "__main__":
    main()
