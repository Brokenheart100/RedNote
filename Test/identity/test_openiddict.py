from __future__ import annotations

import sys
from pathlib import Path

import httpx


PROJECT_ROOT = Path(__file__).resolve().parents[2]

if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))


from Test.support import (
    AuthenticatedUser,
    CLIENT_ID,
    GATEWAY_URL,
)


def test_discovery(
    client: httpx.Client,
) -> None:
    response = client.get(
        "/.well-known/openid-configuration"
    )

    assert response.status_code == 200

    document = response.json()

    assert document["issuer"] == (
        f"{GATEWAY_URL}/"
    )

    assert document[
        "authorization_endpoint"
    ] == (
        f"{GATEWAY_URL}"
        "/connect/authorize"
    )

    assert document[
        "token_endpoint"
    ] == (
        f"{GATEWAY_URL}"
        "/connect/token"
    )

    assert document[
        "end_session_endpoint"
    ] == (
        f"{GATEWAY_URL}"
        "/connect/logout"
    )

    assert document[
        "jwks_uri"
    ] == (
        f"{GATEWAY_URL}"
        "/.well-known/jwks"
    )


def test_access_token_is_jwt(
    authenticated_user: AuthenticatedUser,
) -> None:
    parts = (
        authenticated_user
        .access_token
        .split(".")
    )

    assert len(parts) == 3


def test_refresh_token(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    refresh_token = (
        authenticated_user.refresh_token
    )

    assert refresh_token

    response = client.post(
        "/connect/token",
        data={
            "grant_type":
                "refresh_token",
            "client_id":
                CLIENT_ID,
            "refresh_token":
                refresh_token,
        },
    )

    assert response.status_code == 200

    payload = response.json()

    access_token = payload.get(
        "access_token"
    )

    assert isinstance(
        access_token,
        str,
    )

    assert access_token


def test_access_token_can_call_user_service(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    response = client.get(
        "/api/v1/users/me",
        headers={
            "Authorization":
                f"Bearer "
                f"{authenticated_user.access_token}",
        },
    )

    assert response.status_code == 200


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