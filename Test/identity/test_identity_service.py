from __future__ import annotations

import secrets
import sys
import time
from pathlib import Path

import httpx


PROJECT_ROOT = Path(__file__).resolve().parents[2]

if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))


from Test.support import DEFAULT_PASSWORD


def create_test_email() -> str:
    return (
        f"identity_test_"
        f"{int(time.time() * 1000)}_"
        f"{secrets.token_hex(4)}"
        "@example.com"
    )


def test_register(
    client: httpx.Client,
) -> None:
    email = create_test_email()

    response = client.post(
        "/api/v1/auth/register",
        json={
            "email": email,
            "password": DEFAULT_PASSWORD,
            "displayName": "Identity Test",
            "familyName": "Test",
        },
    )

    assert response.status_code == 200


def test_duplicate_register(
    client: httpx.Client,
) -> None:
    email = create_test_email()

    payload = {
        "email": email,
        "password": DEFAULT_PASSWORD,
        "displayName": "Identity Test",
        "familyName": "Test",
    }

    first_response = client.post(
        "/api/v1/auth/register",
        json=payload,
    )

    assert first_response.status_code == 200

    second_response = client.post(
        "/api/v1/auth/register",
        json=payload,
    )

    assert second_response.status_code >= 400


def test_invalid_session_login(
    client: httpx.Client,
) -> None:
    email = create_test_email()

    register_response = client.post(
        "/api/v1/auth/register",
        json={
            "email": email,
            "password": DEFAULT_PASSWORD,
            "displayName": "Identity Test",
            "familyName": "Test",
        },
    )

    assert register_response.status_code == 200

    login_response = client.post(
        "/api/v1/auth/session/login",
        json={
            "email": email,
            "password": "WrongPassword123",
        },
    )

    assert register_response.status_code == 200
    assert login_response.status_code >= 400


if __name__ == "__main__":
    import pytest

    raise SystemExit(
        pytest.main(
            [
                __file__,
                "-v",
            ]
        )
    )