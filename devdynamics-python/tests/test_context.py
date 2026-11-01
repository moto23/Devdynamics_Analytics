from app.context import records_from_snapshot


def test_retrieval_records_have_stable_ids_and_source_links():
    snap = {
        "repository": "acme/widget", "repository_url": "https://github.com/acme/widget",
        "commits": [{"sha": "abc123", "html_url": "https://github.com/acme/widget/commit/abc123",
                     "commit": {"message": "Fix caching", "author": {"date": "2026-10-01T00:00:00Z"}},
                     "author": {"login": "dev"}}],
        "pull_requests": [{"number": 8, "title": "Add dashboard", "state": "closed",
                           "merged_at": "2026-10-03T00:00:00Z"}],
    }
    a = records_from_snapshot(snap)
    b = records_from_snapshot(snap)
    assert a == b
    assert len(a) == 2
    assert len({r["id"] for r in a}) == 2
    assert a[0]["metadata"]["repository"] == "acme/widget"
    assert a[0]["metadata"]["source"].endswith("abc123")
    assert "Fix caching" in a[0]["text"]
    assert "/pull/8" in a[1]["metadata"]["source"]


def test_missing_ids_are_skipped():
    assert records_from_snapshot({"repository": "x/y", "commits": [{}], "pull_requests": [{}]}) == []
