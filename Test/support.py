from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
import sys


TEST_ROOT = Path(__file__).resolve().parent.parent

if str(TEST_ROOT) not in sys.path:
    sys.path.insert(0, str(TEST_ROOT))

GATEWAY_URL = "http://localhost:5000"

CLIENT_ID = "rednote-web"

REDIRECT_URI = "http://localhost:3000/auth/callback"

SCOPE = (
    "openid "
    "profile "
    "email "
    "offline_access "
    "rednote-api"
)

DEFAULT_PASSWORD = "Test1234"


@dataclass(frozen=True)
class AuthenticatedUser:
    email: str
    password: str
    access_token: str
    refresh_token: str | None
    id_token: str | None