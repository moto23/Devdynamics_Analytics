"""Optional MongoDB storage adapter for reproducible GitHub snapshots."""
from datetime import datetime, timezone


def save_snapshot(snapshot: dict, uri: str, database: str = "devdynamics") -> str:
    """Store one snapshot per repository/window; never touches legacy SQL tables."""
    if not uri:
        raise ValueError("MONGODB_URI is required for --save-mongo")
    from pymongo import MongoClient
    with MongoClient(uri, serverSelectionTimeoutMS=5000) as client:
        collection = client[database]["github_snapshots"]
        document = {**snapshot, "stored_at": datetime.now(timezone.utc)}
        collection.replace_one(
            {"repository": snapshot["repository"], "window_days": snapshot["window_days"]},
            document,
            upsert=True,
        )
    return snapshot["repository"]
