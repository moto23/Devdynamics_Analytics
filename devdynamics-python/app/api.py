"""Optional, isolated Flask service; does not modify an existing backend."""
from flask import Flask, jsonify, request

from .analytics import analyze
from .github_client import GitHubAPIError, GitHubClient


def create_app(client: GitHubClient | None = None) -> Flask:
    app = Flask(__name__)
    github = client or GitHubClient()

    @app.get("/health")
    def health():
        return jsonify({"status": "ok", "service": "devdynamics-python"})

    @app.get("/api/v1/github/<owner>/<repo>/summary")
    def summary(owner: str, repo: str):
        try:
            days = int(request.args.get("days", "30"))
            if not 1 <= days <= 365:
                return jsonify({"error": "days must be between 1 and 365"}), 400
            result = analyze(github.snapshot(owner, repo, days=days))
            # The summary intentionally excludes raw document payloads.
            return jsonify(result)
        except ValueError as exc:
            return jsonify({"error": str(exc)}), 400
        except GitHubAPIError as exc:
            return jsonify({"error": str(exc)}), exc.status

    return app


if __name__ == "__main__":
    create_app().run(host="127.0.0.1", port=5055, debug=False)
