from app.api import create_app
from app.github_client import GitHubAPIError


class FakeGitHub:
    def snapshot(self, owner, repo, days=30):
        if owner == "private":
            raise GitHubAPIError("GitHub repository or endpoint not found", 404)
        return {"repository": f"{owner}/{repo}", "window_days": days,
                "commits": [], "pull_requests": [], "contributors": []}


def test_health_endpoint():
    client = create_app(FakeGitHub()).test_client()
    result = client.get("/health")
    assert result.status_code == 200
    assert result.json["service"] == "devdynamics-python"


def test_summary_endpoint_and_day_validation():
    client = create_app(FakeGitHub()).test_client()
    result = client.get("/api/v1/github/example/demo/summary?days=14")
    assert result.status_code == 200
    assert result.json["window_days"] == 14
    assert len(result.json["daily_commits"]) == 15
    assert client.get("/api/v1/github/example/demo/summary?days=0").status_code == 400
    assert client.get("/api/v1/github/example/demo/summary?days=no").status_code == 400


def test_not_found_propagates_cleanly():
    client = create_app(FakeGitHub()).test_client()
    result = client.get("/api/v1/github/private/demo/summary")
    assert result.status_code == 404
