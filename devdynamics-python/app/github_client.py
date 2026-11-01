"""Read-only GitHub REST API client with pagination and bounded requests."""
import re
from datetime import datetime, timedelta, timezone

import requests

from .config import Settings

_OWNER = re.compile(r"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?$")
_REPO = re.compile(r"^[A-Za-z0-9_.-]{1,100}$")


class GitHubAPIError(Exception):
    def __init__(self, message: str, status: int = 502):
        super().__init__(message)
        self.status = status


def validate_repository(owner: str, repo: str) -> None:
    if not _OWNER.fullmatch(owner) or not _REPO.fullmatch(repo) or repo in (".", ".."):
        raise ValueError("Expected a valid GitHub owner and repository name")


class GitHubClient:
    BASE_URL = "https://api.github.com"

    def __init__(self, settings: Settings | None = None, session=None):
        self.settings = settings or Settings()
        self.session = session or requests.Session()
        self.headers = {
            "Accept": "application/vnd.github+json",
            "User-Agent": "DevDynamics-Python-Toolkit/1.0",
            "X-GitHub-Api-Version": "2022-11-28",
        }
        if self.settings.github_token:
            self.headers["Authorization"] = f"Bearer {self.settings.github_token}"

    def _request(self, path: str, params: dict | None = None):
        try:
            response = self.session.get(
                f"{self.BASE_URL}{path}",
                headers=self.headers,
                params=params,
                timeout=self.settings.request_timeout,
            )
        except requests.RequestException as exc:
            raise GitHubAPIError("GitHub connection failed") from exc

        if response.status_code == 404:
            raise GitHubAPIError("GitHub repository or endpoint not found", 404)
        if response.status_code in (401, 403, 429):
            raise GitHubAPIError("GitHub authentication or rate limit prevented the request", 503)
        if response.status_code >= 400:
            raise GitHubAPIError(f"GitHub returned HTTP {response.status_code}", 502)
        try:
            return response.json(), response
        except ValueError as exc:
            raise GitHubAPIError("GitHub returned invalid JSON") from exc

    def _pages(self, path: str, params: dict | None = None):
        """Return (records, truncated). Page count is capped to avoid runaway sync."""
        params = {**(params or {}), "per_page": 100}
        results = []
        for page in range(1, self.settings.max_pages + 1):
            data, response = self._request(path, {**params, "page": page})
            if not isinstance(data, list):
                raise GitHubAPIError("Expected an array from GitHub")
            results.extend(data)
            has_next = bool(getattr(response, "links", {}).get("next"))
            if not has_next:
                return results, False
        return results, True

    def snapshot(self, owner: str, repo: str, days: int = 30) -> dict:
        validate_repository(owner, repo)
        if not 1 <= days <= 365:
            raise ValueError("days must be between 1 and 365")

        since = datetime.now(timezone.utc) - timedelta(days=days)
        path = f"/repos/{owner}/{repo}"
        metadata, _ = self._request(path)
        commits, commits_truncated = self._pages(
            f"{path}/commits", {"since": since.isoformat().replace("+00:00", "Z")}
        )
        pulls, pulls_truncated = self._pages(
            f"{path}/pulls", {"state": "all", "sort": "updated", "direction": "desc"}
        )
        contributors, contributors_truncated = self._pages(f"{path}/contributors")
        # GitHub does not support a `since` filter on the pull-list endpoint.
        # `updated_at` filtering is approximate and subject to page budgets.
        pulls = [
            pr for pr in pulls
            if pr.get("updated_at") and datetime.fromisoformat(
                pr["updated_at"].replace("Z", "+00:00")
            ) >= since
        ]
        return {
            "repository": metadata.get("full_name", f"{owner}/{repo}"),
            "repository_url": metadata.get("html_url", f"https://github.com/{owner}/{repo}"),
            "captured_at": datetime.now(timezone.utc).isoformat(),
            "window_days": days,
            "is_partial": commits_truncated or pulls_truncated or contributors_truncated,
            "truncation": {
                "commits": commits_truncated,
                "pull_requests": pulls_truncated,
                "contributors": contributors_truncated,
            },
            "commits": commits,
            "pull_requests": pulls,
            "contributors": contributors,
        }
