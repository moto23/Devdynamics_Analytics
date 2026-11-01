"""Generate comparison reports from actual GitHub snapshots (no AI needed)."""
from collections import Counter

from .analytics import analyze


def portfolio_report(snapshots: list[dict]) -> dict:
    """Compare any collection of repositories using the same window length."""
    if not snapshots:
        raise ValueError("At least one repository snapshot is required")
    windows = {int(item.get("window_days", 30)) for item in snapshots}
    if len(windows) != 1:
        raise ValueError("Snapshots must use the same window in days for comparison")

    per_repo = []
    totals = Counter()
    for snapshot in snapshots:
        row = analyze(snapshot)
        per_repo.append(row)
        totals["commits_fetched"] += row["commits_fetched"]
        totals["pull_requests_updated_in_window"] += row["pull_requests_updated_in_window"]
        totals["merged_pull_requests_updated_in_window"] += row["merged_pull_requests_updated_in_window"]

    per_repo.sort(key=lambda item: item["commits_fetched"], reverse=True)
    return {
        "window_days": windows.pop(),
        "repository_count": len(per_repo),
        "is_partial": any(item["is_partial"] for item in per_repo),
        "totals": dict(totals),
        "repositories": per_repo,
    }


def to_markdown(report: dict) -> str:
    """Readable report for GitHub issues, portfolios, or regular reviews."""
    lines = [
        "# DevDynamics Repository Activity Comparison",
        "",
        f"**Repositories:** {report['repository_count']}  ",
        f"**Analysis window:** {report['window_days']} days  ",
        f"**Partial API results:** {'Yes' if report['is_partial'] else 'No'}",
        "",
        "| Repository | Commits fetched | PRs updated | Merged PRs | Mean merge hours | Incomplete? |",
        "|---|---:|---:|---:|---:|:---:|",
    ]
    for item in report["repositories"]:
        name = item.get("repository") or "unknown"
        url = item.get("repository_url", "")
        linked = f"[{name}]({url})" if url and url.startswith("https://github.com/") else name
        hours = item["mean_pr_merge_hours"]
        lines.append(
            f"| {linked} | {item['commits_fetched']} | "
            f"{item['pull_requests_updated_in_window']} | "
            f"{item['merged_pull_requests_updated_in_window']} | "
            f"{hours if hours is not None else 'N/A'} | "
            f"{'Yes' if item['is_partial'] else 'No'} |"
        )
    totals = report["totals"]
    lines.extend([
        "",
        f"**Observed totals:** {totals.get('commits_fetched', 0)} fetched commits, "
        f"{totals.get('pull_requests_updated_in_window', 0)} PRs updated in the window.",
        "",
        "*Limitations: Counts reflect GitHub API responses, page budgets and observation windows. "
        "Merge duration is calculated only where both creation and merge timestamps are present. "
        "These event counts should not be used as an individual productivity ranking.*",
        "",
    ])
    return "\n".join(lines)
