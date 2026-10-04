import base64
import hashlib
import secrets
import time
import urllib.parse
from collections.abc import Callable, Iterator

import httpx
import pytest

from Test.support import (
    AuthenticatedUser,
    CLIENT_ID,
    DEFAULT_PASSWORD,
    GATEWAY_URL,
    REDIRECT_URI,
    SCOPE,
)


@pytest.fixture
def client() -> Iterator[httpx.Client]:
    with httpx.Client(
        base_url=GATEWAY_URL,
        timeout=10.0,
        follow_redirects=False,
    ) as client:
        yield client


@pytest.fixture
def create_user(
    client: httpx.Client,
) -> Callable[..., AuthenticatedUser]:
    def factory(
        *,
        prefix: str,
        display_name: str,
    ) -> AuthenticatedUser:
        return create_authenticated_user(
            client,
            prefix=prefix,
            display_name=display_name,
        )

    return factory


@pytest.fixture
def authenticated_user(
    client: httpx.Client,
) -> AuthenticatedUser:
    return create_authenticated_user(
        client,
        prefix="pytest_user",
        display_name="Pytest User",
    )


def create_authenticated_user(
    client: httpx.Client,
    *,
    prefix: str,
    display_name: str,
) -> AuthenticatedUser:
    email = (
        f"{prefix}_{int(time.time() * 1000)}_"
        f"{secrets.token_hex(4)}"
        "@example.com"
    )

    register_response = client.post(
        "/api/v1/auth/register",
        json={
            "email": email,
            "password": DEFAULT_PASSWORD,
            "displayName": display_name,
            "familyName": "Test",
        },
    )

    assert register_response.status_code == 200

    with httpx.Client(
        base_url=GATEWAY_URL,
        timeout=10.0,
        follow_redirects=False,
    ) as auth_client:
        login_response = auth_client.post(
            "/api/v1/auth/session/login",
            json={
                "email": email,
                "password": DEFAULT_PASSWORD,
            },
        )

        assert login_response.status_code == 200

        code_verifier = secrets.token_urlsafe(64)

        digest = hashlib.sha256(
            code_verifier.encode("ascii")
        ).digest()

        code_challenge = (
            base64.urlsafe_b64encode(digest)
            .rstrip(b"=")
            .decode("ascii")
        )

        state = secrets.token_urlsafe(32)

        authorize_response = auth_client.get(
            "/connect/authorize",
            params={
                "client_id": CLIENT_ID,
                "redirect_uri": REDIRECT_URI,
                "response_type": "code",
                "scope": SCOPE,
                "code_challenge": code_challenge,
                "code_challenge_method": "S256",
                "state": state,
            },
        )

        assert authorize_response.status_code in {
            301,
            302,
            303,
            307,
            308,
        }

        location = authorize_response.headers.get(
            "Location"
        )

        assert location is not None

        parsed = urllib.parse.urlparse(
            location
        )

        query = urllib.parse.parse_qs(
            parsed.query
        )

        assert query.get("state", [None])[0] == state

        code = query.get("code", [None])[0]

        assert isinstance(code, str)
        assert code

        token_response = client.post(
            "/connect/token",
            data={
                "grant_type":
                    "authorization_code",
                "client_id":
                    CLIENT_ID,
                "redirect_uri":
                    REDIRECT_URI,
                "code":
                    code,
                "code_verifier":
                    code_verifier,
            },
        )

        assert token_response.status_code == 200

        tokens = token_response.json()

        access_token = tokens.get(
            "access_token"
        )

        assert isinstance(
            access_token,
            str,
        )
        assert access_token

        return AuthenticatedUser(
            email=email,
            password=DEFAULT_PASSWORD,
            access_token=access_token,
            refresh_token=tokens.get(
                "refresh_token"
            ),
            id_token=tokens.get(
                "id_token"
            ),
        )