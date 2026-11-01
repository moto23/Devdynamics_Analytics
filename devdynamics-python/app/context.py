"""Convert structured GitHub snapshots into traceable retrieval records."""
import hashlib


def records_from_snapshot(snapshot: dict) -> list[dict]:
    """Use stable IDs so re-indexing the same commit or PR does not duplicate it."""
    repository = snapshot.get("repository", "unknown/unknown")
    repository_url = snapshot.get("repository_url", "")
    records = []

    for c in snapshot.get("commits", []):
        sha = c.get("sha", "")
        if not sha:
            continue
        info = c.get("commit") or {}
        author = (c.get("author") or {}).get("login") or (info.get("author") or {}).get("name", "unknown")
        message = info.get("message", "")[:3000]
        url = c.get("html_url") or f"{repository_url}/commit/{sha}"
        records.append({
            "id": hashlib.sha256(f"{repository}:commit:{sha}".encode()).hexdigest(),
            "text": f"Repository: {repository}\nType: commit\nSHA: {sha}\nAuthor: {author}\nDate: {(info.get('author') or {}).get('date', '')}\nMessage: {message}",
            "metadata": {"repository": repository, "type": "commit", "source": url},
        })

    for pr in snapshot.get("pull_requests", []):
        number = pr.get("number")
        if number is None:
            continue
        url = pr.get("html_url") or f"{repository_url}/pull/{number}"
        records.append({
            "id": hashlib.sha256(f"{repository}:pr:{number}".encode()).hexdigest(),
            "text": (
                f"Repository: {repository}\nType: pull request\nNumber: {number}\n"
                f"Title: {str(pr.get('title') or '')[:500]}\nState: {pr.get('state', '')}\n"
                f"Created: {pr.get('created_at', '')}\nMerged: {pr.get('merged_at') or 'not merged'}\n"
                f"Description: {str(pr.get('body') or '')[:3000]}"
            ),
            "metadata": {"repository": repository, "type": "pull_request", "source": url},
        })
    return records
