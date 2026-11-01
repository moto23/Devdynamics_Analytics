"""Environment-based configuration for the optional Python sidecar."""
import os
from dataclasses import dataclass

from dotenv import load_dotenv

load_dotenv()


def int_env(name: str, default: int, minimum: int, maximum: int) -> int:
    try:
        return max(minimum, min(maximum, int(os.environ.get(name, default))))
    except (ValueError, TypeError):
        return default


@dataclass(frozen=True)
class Settings:
    github_token: str = os.getenv("GITHUB_TOKEN", "")
    max_pages: int = int_env("GITHUB_MAX_PAGES", 3, 1, 10)
    request_timeout: int = int_env("GITHUB_TIMEOUT_SECONDS", 15, 1, 60)
    chroma_directory: str = os.getenv("CHROMA_DIR", "./data/chroma")
    openai_model: str = os.getenv("OPENAI_MODEL", "gpt-4o-mini")
    embedding_model: str = os.getenv("OPENAI_EMBEDDING_MODEL", "text-embedding-3-small")
    mongodb_uri: str = os.getenv("MONGODB_URI", "")
    mongodb_db: str = os.getenv("MONGODB_DB", "devdynamics")
