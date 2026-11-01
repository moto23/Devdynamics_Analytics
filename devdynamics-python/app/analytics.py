"""Pure GitHub activity transformations: easy to test, no API or DB calls."""
from collections import Counter
from datetime import datetime, timedelta, timezone
from statistics import mean


def _datetime(value: str | None):
    if not value:
        return None
    try:
        result = datetime.fromisoformat(value.replace("Z", "+00:00"))
        return result if result.tzinfo else result.replace(tzinfo=timezone.utc)
    except ValueError:
        return None


def _author(commit: dict) -> str:
    profile = commit.get("author") or {}
    nested = commit.get("commit") or {}
    author = nested.get("author") or {}
    return profile.get("login") or author.get("name") or "unknown"


def analyze(snapshot: dict) -> dict:
    """Compute a snapshot with explicit window and truncation caveats."""
    commits = snapshot.get("commits", [])
    pulls = snapshot.get("pull_requests", [])
    contributors = snapshot.get("contributors", [])
    days = int(snapshot.get("window_days", 30))
    today = datetime.now(timezone.utc).date()
    start = today - timedelta(days=days)
    daily = Counter()
    actors = Counter()
    seen_shas = set()

    for commit in commits:
        sha = commit.get("sha")
        if sha and sha in seen_shas:
            continue
        if sha:
            seen_shas.add(sha)
        author = _author(commit)
        actors[author] += 1
        stamp = ((commit.get("commit") or {}).get("author") or {}).get("date")
        dt = _datetime(stamp)
        if dt is not None and dt.date() >= start:
            daily[dt.date().isoformat()] += 1

    merged_hours = []
    merged_count = 0
    for pr in pulls:
        created = _datetime(pr.get("created_at"))
        merged = _datetime(pr.get("merged_at"))
        if merged:
            merged_count += 1
        if created and merged and merged >= created:
            merged_hours.append(round((merged - created).total_seconds() / 3600, 3))

    daily_series = [
        {"date": (start + timedelta(days=i)).isoformat(), "commits": daily.get((start + timedelta(days=i)).isoformat(), 0)}
        for i in range(days + 1)
    ]
    return {
        "repository": snapshot.get("repository"),
        "repository_url": snapshot.get("repository_url"),
        "captured_at": snapshot.get("captured_at"),
        "window_days": days,
        "is_partial": bool(snapshot.get("is_partial", False)),
        "truncation": snapshot.get("truncation", {}),
        "commits_fetched": len(commits),
        "pull_requests_updated_in_window": len(pulls),
        "merged_pull_requests_updated_in_window": merged_count,
        "mean_pr_merge_hours": round(mean(merged_hours), 2) if merged_hours else None,
        "contributors_all_time_fetched": len(contributors),
        "active_commit_authors_fetched": len(actors),
        "top_commit_authors": [
            {"login": login, "commits": count} for login, count in actors.most_common(10)
        ],
        "daily_commits": daily_series,
    }
