from __future__ import annotations

from collections.abc import Generator
from dataclasses import dataclass
import base64
import hashlib
import json
import secrets
import string
from urllib.parse import parse_qs, urlparse

import httpx
import pytest


GATEWAY_URL = "https://localhost:7161"
FRONTEND_URL = "http://localhost:3000"

CLIENT_ID = "rednote-web"
REDIRECT_URI = "http://localhost:3000/auth/rednote"

TIMEOUT = 30.0


@dataclass(slots=True)
class TestAccount:
    email: str
    password: str
    display_name: str
    family_name: str


@dataclass(slots=True)
class PkceContext:
    code_verifier: str
    code_challenge: str
    state: str


def random_text(
    length: int = 8,
) -> str:
    alphabet = (
        string.ascii_lowercase
        + string.digits
    )

    return "".join(
        secrets.choice(alphabet)
        for _ in range(length)
    )


def create_account() -> TestAccount:
    suffix = random_text()

    return TestAccount(
        email=(
            f"oidc_{suffix}"
            "@example.com"
        ),
        password=(
            "RedNote@"
            f"{random_text(12)}"
            "Aa1"
        ),
        display_name=(
            f"OIDC测试用户 {suffix}"
        ),
        family_name="RedNote",
    )


def create_pkce() -> PkceContext:
    verifier = (
        secrets.token_urlsafe(64)
    )

    digest = hashlib.sha256(
        verifier.encode("ascii")
    ).digest()

    challenge = (
        base64.urlsafe_b64encode(
            digest
        )
        .rstrip(b"=")
        .decode("ascii")
    )

    return PkceContext(
        code_verifier=verifier,
        code_challenge=challenge,
        state=secrets.token_urlsafe(24),
    )


def print_title(
    title: str,
) -> None:
    print()
    print("=" * 90)
    print(f"🚀 {title}")
    print("=" * 90)


def print_response(
    response: httpx.Response,
) -> None:
    print()
    print(
        f"📡 {response.request.method} "
        f"{response.request.url}"
    )

    print(
        f"📥 HTTP {response.status_code}"
    )

    location = response.headers.get(
        "location"
    )

    if location:
        print(
            f"➡️ Location: {location}"
        )

    set_cookies = (
        response.headers.get_list(
            "set-cookie"
        )
    )

    if set_cookies:
        print("🍪 Set-Cookie:")

        for cookie in set_cookies:
            print(
                f"   🔹 {cookie}"
            )

    body = response.text.strip()

    if not body:
        print("📦 Body: <empty>")
        return

    try:
        data = response.json()

        print("📦 Body:")

        print(
            json.dumps(
                data,
                ensure_ascii=False,
                indent=2,
            )
        )
    except Exception:
        print("📦 Body:")
        print(
            body[:2000]
        )


@pytest.fixture(scope="session")
def account() -> TestAccount:
    account = create_account()

    print_title(
        "🧪 创建随机测试账号"
    )

    print(
        f"📧 {account.email}"
    )

    print(
        f"👤 {account.display_name}"
    )

    return account


@pytest.fixture(scope="session")
def client() -> Generator[
    httpx.Client,
    None,
    None,
]:
    with httpx.Client(
        verify=False,
        timeout=TIMEOUT,
        follow_redirects=False,
        headers={
            "Origin":
                FRONTEND_URL,
        },
    ) as client:
        yield client


def get_csrf(
    client: httpx.Client,
) -> tuple[str, str]:
    print_title(
        "🛡️ 获取 CSRF Token"
    )

    response = client.get(
        f"{GATEWAY_URL}"
        "/api/v1/auth/csrf"
    )

    print_response(
        response
    )

    assert (
        response.status_code
        == 200
    )

    data = response.json()

    token = data.get(
        "token"
    )

    header_name = data.get(
        "headerName"
    )

    assert isinstance(
        token,
        str,
    )

    assert token

    assert isinstance(
        header_name,
        str,
    )

    assert header_name

    return (
        token,
        header_name,
    )


def test_01_register(
    client: httpx.Client,
    account: TestAccount,
) -> None:
    print_title(
        "1️⃣ Register"
    )

    csrf_token, csrf_header = (
        get_csrf(
            client
        )
    )

    response = client.post(
        f"{GATEWAY_URL}"
        "/api/v1/auth/register",
        headers={
            csrf_header:
                csrf_token,
        },
        json={
            "email":
                account.email,

            "password":
                account.password,

            "displayName":
                account.display_name,

            "familyName":
                account.family_name,
        },
    )

    print_response(
        response
    )

    assert (
        response.status_code
        == 200
    )

    data = response.json()

    assert (
        data["email"]
        == account.email
    )

    print(
        "✅ Register 正常"
    )


def test_02_session_login(
    client: httpx.Client,
    account: TestAccount,
) -> None:
    print_title(
        "2️⃣ Session Login"
    )

    csrf_token, csrf_header = (
        get_csrf(
            client
        )
    )

    response = client.post(
        f"{GATEWAY_URL}"
        "/api/v1/auth/session/login",
        headers={
            csrf_header:
                csrf_token,
        },
        json={
            "email":
                account.email,

            "password":
                account.password,
        },
    )

    print_response(
        response
    )

    assert (
        response.status_code
        == 200
    )

    cookie_names = {
        cookie.name
        for cookie
        in client.cookies.jar
    }

    assert any(
        "Identity" in name
        for name
        in cookie_names
    )

    print(
        "✅ Identity Cookie 已建立"
    )


def test_03_me(
    client: httpx.Client,
    account: TestAccount,
) -> None:
    print_title(
        "3️⃣ GET /me"
    )

    response = client.get(
        f"{GATEWAY_URL}"
        "/api/v1/auth/me"
    )

    print_response(
        response
    )

    assert (
        response.status_code
        == 200
    )

    data = response.json()

    assert (
        data["email"]
        == account.email
    )

    print(
        "✅ GetMe 正常"
    )


def test_04_authorize_returns_code(
    client: httpx.Client,
) -> tuple[str, PkceContext]:
    print_title(
        "4️⃣ Authorization Code Flow"
    )

    pkce = create_pkce()

    response = client.get(
        f"{GATEWAY_URL}"
        "/connect/authorize",
        params={
            "client_id":
                CLIENT_ID,

            "response_type":
                "code",

            "redirect_uri":
                REDIRECT_URI,

            "scope":
                (
                    "openid "
                    "profile "
                    "email "
                    "offline_access "
                    "rednote-api"
                ),

            "code_challenge":
                pkce.code_challenge,

            "code_challenge_method":
                "S256",

            "state":
                pkce.state,
        },
    )

    print_response(
        response
    )

    assert (
        response.status_code
        in {
            302,
            303,
        }
    )

    location = response.headers.get(
        "location"
    )

    assert location

    parsed = urlparse(
        location
    )

    query = parse_qs(
        parsed.query
    )

    code = (
        query.get("code", [None])[0]
    )

    returned_state = (
        query.get("state", [None])[0]
    )

    assert code
    assert (
        returned_state
        == pkce.state
    )

    print(
        "✅ Authorization Code 已获取"
    )

    return (
        code,
        pkce,
    )


def exchange_code(
    client: httpx.Client,
    code: str,
    pkce: PkceContext,
) -> dict:
    print_title(
        "5️⃣ Exchange Authorization Code"
    )

    response = client.post(
        f"{GATEWAY_URL}"
        "/connect/token",
        data={
            "grant_type":
                "authorization_code",

            "client_id":
                CLIENT_ID,

            "code":
                code,

            "redirect_uri":
                REDIRECT_URI,

            "code_verifier":
                pkce.code_verifier,
        },
        headers={
            "Content-Type":
                "application/x-www-form-urlencoded",
        },
    )

    print_response(
        response
    )

    assert (
        response.status_code
        == 200
    )

    data = response.json()

    assert (
        isinstance(
            data.get("access_token"),
            str,
        )
    )

    assert data.get(
        "access_token"
    )

    assert (
        isinstance(
            data.get("refresh_token"),
            str,
        )
    )

    assert data.get(
        "refresh_token"
    )

    print(
        "✅ Access Token 获取成功"
    )

    print(
        "✅ Refresh Token 获取成功"
    )

    return data


def refresh_token(
    client: httpx.Client,
    refresh_token: str,
) -> dict:
    print_title(
        "6️⃣ Refresh Token"
    )

    response = client.post(
        f"{GATEWAY_URL}"
        "/connect/token",
        data={
            "grant_type":
                "refresh_token",

            "client_id":
                CLIENT_ID,

            "refresh_token":
                refresh_token,
        },
        headers={
            "Content-Type":
                "application/x-www-form-urlencoded",
        },
    )

    print_response(
        response
    )

    assert (
        response.status_code
        == 200
    )

    data = response.json()

    assert data.get(
        "access_token"
    )

    print(
        "✅ Refresh Token Flow 正常"
    )

    return data


def test_05_openiddict_full_flow(
    client: httpx.Client,
) -> None:
    code, pkce = (
        test_04_authorize_returns_code(
            client
        )
    )

    tokens = exchange_code(
        client,
        code,
        pkce,
    )

    refresh = tokens.get(
        "refresh_token"
    )

    assert isinstance(
        refresh,
        str,
    )

    refreshed = refresh_token(
        client,
        refresh,
    )

    assert refreshed.get(
        "access_token"
    )

    print()
    print(
        "🎉 Wolverine + OpenIddict "
        "核心链路全部通过"
    )