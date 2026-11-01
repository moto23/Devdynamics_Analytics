from datetime import datetime, timezone

import pytest

from app.config import Settings
from app.github_client import GitHubAPIError, GitHubClient, validate_repository


class Response:
    def __init__(self, data, status_code=200, has_next=False):
        self.data = data
        self.status_code = status_code
        self.links = {"next": {"url": "more"}} if has_next else {}

    def json(self):
        return self.data


class FakeSession:
    def __init__(self, responses):
        self.responses = iter(responses)
        self.calls = []

    def get(self, url, headers, params, timeout):
        self.calls.append({"url": url, "headers": headers, "params": params, "timeout": timeout})
        return next(self.responses)


def test_bad_repositories_blocked():
    for owner, repo in [("a/b", "repo"), ("good", "../admin"), ("", "repo")]:
        with pytest.raises(ValueError):
            validate_repository(owner, repo)


def test_bounded_snapshot_with_realistic_shapes():
    now = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
    session = FakeSession([
        Response({"full_name": "example/repo", "html_url": "https://github.com/example/repo"}),
        Response([{"sha": "abc", "commit": {"author": {"date": now}}}], has_next=False),
        Response([{"number": 3, "updated_at": now, "created_at": now}], has_next=False),
        Response([{"login": "contributor"}], has_next=False),
    ])
    result = GitHubClient(Settings(github_token="secret", max_pages=1), session).snapshot("example", "repo")
    assert result["repository"] == "example/repo"
    assert len(result["pull_requests"]) == 1
    assert result["is_partial"] is False
    assert len(session.calls) == 4
    assert session.calls[0]["headers"]["Authorization"] == "Bearer secret"
    assert session.calls[1]["params"]["per_page"] == 100


def test_respects_page_limit_and_flags_partial_data():
    session = FakeSession([Response([{"id": 1}], has_next=True)])
    client = GitHubClient(Settings(max_pages=1), session)
    rows, truncated = client._pages("/repos/acme/demo/commits")
    assert len(rows) == 1
    assert truncated


def test_rate_limit_error_does_not_leak_token():
    session = FakeSession([Response({"message": "rate limit"}, 403)])
    client = GitHubClient(Settings(github_token="SECRET"), session)
    with pytest.raises(GitHubAPIError) as err:
        client._request("/repos/example/demo")
    assert err.value.status == 503
    assert "SECRET" not in str(err.value)
