import pytest

from app.reporting import portfolio_report, to_markdown


def fixture(repository, commits):
    return {
        "repository": repository,
        "repository_url": f"https://github.com/{repository}",
        "window_days": 30,
        "commits": [
            {"sha": str(i), "author": {"login": "dev"}, "commit": {"author": {"date": "2026-10-31T00:00:00Z"}}}
            for i in range(commits)
        ],
        "pull_requests": [],
        "contributors": [],
    }


def test_portfolio_comparison_orders_by_commit_count():
    report = portfolio_report([fixture("a/one", 1), fixture("b/two", 4)])
    assert report["repository_count"] == 2
    assert report["totals"]["commits_fetched"] == 5
    assert report["repositories"][0]["repository"] == "b/two"
    output = to_markdown(report)
    assert "[b/two](https://github.com/b/two)" in output
    assert "5 fetched commits" in output


def test_mixed_windows_are_rejected():
    a = fixture("a/one", 1)
    b = fixture("b/two", 1)
    b["window_days"] = 14
    with pytest.raises(ValueError):
        portfolio_report([a, b])
    with pytest.raises(ValueError):
        portfolio_report([])
