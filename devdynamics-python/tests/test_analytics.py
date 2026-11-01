from datetime import datetime, timedelta, timezone

from app.analytics import analyze


def utc_z(dt):
    return dt.astimezone(timezone.utc).isoformat().replace("+00:00", "Z")


def test_summary_and_merge_cycle():
    now = datetime.now(timezone.utc)
    snapshot = {
        "repository": "example/repo", "repository_url": "https://github.com/example/repo",
        "captured_at": utc_z(now), "window_days": 7,
        "commits": [
            {"sha": "abc", "author": {"login": "alice"}, "commit": {"author": {"date": utc_z(now)}}},
            {"sha": "abc", "author": {"login": "alice"}, "commit": {"author": {"date": utc_z(now)}}},
            {"sha": "def", "author": {"login": "bob"}, "commit": {"author": {"date": utc_z(now)}}},
        ],
        "pull_requests": [
            {"number": 1, "created_at": utc_z(now - timedelta(hours=3)), "merged_at": utc_z(now)},
            {"number": 2, "created_at": utc_z(now), "merged_at": None},
        ],
        "contributors": [{"login": "alice"}, {"login": "bob"}],
    }
    result = analyze(snapshot)
    assert result["commits_fetched"] == 3  # Raw API records, prior to SHA de-duplication
    assert result["active_commit_authors_fetched"] == 2
    assert result["merged_pull_requests_updated_in_window"] == 1
    assert result["mean_pr_merge_hours"] == 3
    assert result["daily_commits"][-1]["commits"] == 2
    assert result["contributors_all_time_fetched"] == 2


def test_empty_data_does_not_invent_metrics():
    result = analyze({"repository": "example/empty", "window_days": 7})
    assert result["mean_pr_merge_hours"] is None
    assert result["commits_fetched"] == 0
    assert result["daily_commits"][-1]["commits"] == 0
