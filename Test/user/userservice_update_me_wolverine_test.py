from __future__ import annotations

import base64
import hashlib
import json
import secrets
import string
from collections.abc import Iterator
from typing import Any
from urllib.parse import parse_qs, urlparse

import httpx
import pytest


# ============================================================
# 🔧 当前环境
# ============================================================

GATEWAY_BASE_URL = "https://localhost:7161"
USER_SERVICE_BASE_URL = "http://localhost:5003"

VERIFY_GATEWAY_TLS = False
TIMEOUT_SECONDS = 20.0


# ============================================================
# 🔐 OIDC
# ============================================================

CLIENT_ID = "rednote-web"
REDIRECT_URI = "http://localhost:3000/auth/rednote"

SCOPES = (
    "openid "
    "profile "
    "email "
    "offline_access "
    "rednote-api"
)


# ============================================================
# 🌐 Endpoints
# ============================================================

CSRF_ENDPOINT = "/api/v1/auth/csrf"
REGISTER_ENDPOINT = "/api/v1/auth/register"
LOGIN_ENDPOINT = "/api/v1/auth/session/login"

AUTHORIZE_ENDPOINT = "/connect/authorize"
TOKEN_ENDPOINT = "/connect/token"

ME_ENDPOINT = "/api/v1/users/me"


type JsonObject = dict[str, Any]


# ============================================================
# 🛠️ Helpers
# ============================================================


def random_text(
    length: int = 10,
) -> str:
    alphabet = (
        string.ascii_lowercase
        + string.digits
    )

    return "".join(
        secrets.choice(alphabet)
        for _ in range(length)
    )


def pretty_json(
    value: object,
) -> str:
    return json.dumps(
        value,
        ensure_ascii=False,
        indent=2,
    )


def create_pkce() -> tuple[str, str]:
    verifier = secrets.token_urlsafe(64)

    digest = hashlib.sha256(
        verifier.encode("ascii")
    ).digest()

    challenge = (
        base64.urlsafe_b64encode(
            digest,
        )
        .decode("ascii")
        .rstrip("=")
    )

    return verifier, challenge


def print_response(
    title: str,
    response: httpx.Response,
) -> None:
    print()
    print("=" * 72)
    print(f"📡 {title}")
    print("=" * 72)

    print(
        f"📥 HTTP {response.status_code}"
    )

    if not response.content:
        print("📦 Body: (empty)")
        return

    try:
        data = response.json()

        print("📦 Body:")
        print(
            pretty_json(
                data,
            )
        )

    except ValueError:
        print(
            response.text[:3000]
        )


# ============================================================
# 🌐 Clients
# ============================================================


@pytest.fixture(scope="session")
def gateway_client() -> Iterator[httpx.Client]:
    with httpx.Client(
        base_url=GATEWAY_BASE_URL,
        verify=VERIFY_GATEWAY_TLS,
        timeout=TIMEOUT_SECONDS,
        follow_redirects=False,
    ) as client:
        yield client


@pytest.fixture(scope="session")
def user_service_client() -> Iterator[httpx.Client]:
    with httpx.Client(
        base_url=USER_SERVICE_BASE_URL,
        timeout=TIMEOUT_SECONDS,
        follow_redirects=False,
    ) as client:
        yield client


# ============================================================
# 🔐 Auth helpers
# ============================================================


def get_csrf(
    client: httpx.Client,
) -> tuple[str, str]:
    response = client.get(
        CSRF_ENDPOINT,
    )

    assert response.status_code == 200

    data = response.json()

    token = data.get("token")
    header_name = data.get(
        "headerName",
    )

    assert isinstance(
        token,
        str,
    )

    assert isinstance(
        header_name,
        str,
    )

    return token, header_name


def create_test_account() -> JsonObject:
    suffix = random_text()

    password = (
        f"RedNote@{random_text(12)}Aa1"
    )

    return {
        "email":
            f"update_{suffix}@example.com",

        "displayName":
            f"更新测试用户 {suffix}",

        "familyName":
            "RedNote",

        "password":
            password,
    }


def register(
    client: httpx.Client,
    account: JsonObject,
) -> None:
    csrf, header_name = get_csrf(
        client,
    )

    response = client.post(
        REGISTER_ENDPOINT,
        json={
            "email":
                account["email"],

            "displayName":
                account["displayName"],

            "familyName":
                account["familyName"],

            "password":
                account["password"],
        },
        headers={
            header_name:
                csrf,
        },
    )

    print_response(
        "Register",
        response,
    )

    assert response.status_code == 200


def login(
    client: httpx.Client,
    account: JsonObject,
) -> None:
    csrf, header_name = get_csrf(
        client,
    )

    response = client.post(
        LOGIN_ENDPOINT,
        json={
            "email":
                account["email"],

            "password":
                account["password"],
        },
        headers={
            header_name:
                csrf,
        },
    )

    print_response(
        "Session Login",
        response,
    )

    assert response.status_code == 200


def authorize(
    client: httpx.Client,
) -> tuple[str, str]:
    verifier, challenge = (
        create_pkce()
    )

    state = secrets.token_urlsafe(
        24,
    )

    response = client.get(
        AUTHORIZE_ENDPOINT,
        params={
            "client_id":
                CLIENT_ID,

            "response_type":
                "code",

            "redirect_uri":
                REDIRECT_URI,

            "scope":
                SCOPES,

            "code_challenge":
                challenge,

            "code_challenge_method":
                "S256",

            "state":
                state,
        },
    )

    assert response.status_code in {
        302,
        303,
    }

    location = response.headers.get(
        "location",
    )

    assert location

    query = parse_qs(
        urlparse(location).query,
    )

    assert "error" not in query

    code = query.get("code")

    assert code

    returned_state = query.get(
        "state",
    )

    assert returned_state
    assert returned_state[0] == state

    return (
        code[0],
        verifier,
    )


def exchange_token(
    client: httpx.Client,
    code: str,
    verifier: str,
) -> str:
    response = client.post(
        TOKEN_ENDPOINT,
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
                verifier,
        },
        headers={
            "Content-Type":
                "application/"
                "x-www-form-urlencoded",
        },
    )

    print_response(
        "Token Exchange",
        response,
    )

    assert response.status_code == 200

    data = response.json()

    access_token = data.get(
        "access_token",
    )

    assert isinstance(
        access_token,
        str,
    )

    return access_token


@pytest.fixture(scope="session")
def access_token(
    gateway_client: httpx.Client,
) -> str:
    account = create_test_account()

    print()
    print("👤 创建 UpdateMe 测试账号")
    print(
        "📧",
        account["email"],
    )
    print(
        "🪪",
        account["displayName"],
    )

    register(
        gateway_client,
        account,
    )

    login(
        gateway_client,
        account,
    )

    code, verifier = authorize(
        gateway_client,
    )

    token = exchange_token(
        gateway_client,
        code,
        verifier,
    )

    print()
    print(
        "✅ Access Token 获取完成"
    )

    return token


# ============================================================
# 🧪 TEST 1
# 无 Token PATCH
# ============================================================


def test_update_me_requires_authentication(
    user_service_client: httpx.Client,
) -> None:
    response = user_service_client.patch(
        ME_ENDPOINT,
        json={
            "nickname":
                "未登录修改",

            "avatarUrl":
                None,

            "bio":
                "should fail",
        },
    )

    print_response(
        "PATCH /me without token",
        response,
    )

    assert response.status_code == 401


# ============================================================
# 🧪 TEST 2
# 正常更新
# ============================================================


def test_update_me_success(
    user_service_client: httpx.Client,
    access_token: str,
) -> None:
    nickname = (
        f"新昵称 {random_text(6)}"
    )

    bio = (
        "这是 Wolverine HTTP "
        "UpdateMe pytest 测试简介。"
    )

    response = user_service_client.patch(
        ME_ENDPOINT,
        headers={
            "Authorization":
                f"Bearer {access_token}",
        },
        json={
            "nickname":
                nickname,

            "avatarUrl":
                None,

            "bio":
                bio,
        },
    )

    print_response(
        "PATCH /me success",
        response,
    )

    assert response.status_code == 200

    data = response.json()

    assert data["nickname"] == nickname
    assert data["bio"] == bio
    assert data["avatarUrl"] is None
    assert data["userId"]

    print()
    print(
        "✅ UpdateMe Wolverine HTTP 正常"
    )


# ============================================================
# 🧪 TEST 3
# GET /me 验证持久化
# ============================================================


def test_get_me_after_update(
    user_service_client: httpx.Client,
    access_token: str,
) -> None:
    response = user_service_client.get(
        ME_ENDPOINT,
        headers={
            "Authorization":
                f"Bearer {access_token}",
        },
    )

    print_response(
        "GET /me after update",
        response,
    )

    assert response.status_code == 200

    data = response.json()

    assert data["nickname"]
    assert data["bio"]

    print()
    print(
        "✅ PostgreSQL 更新结果可再次读取"
    )


# ============================================================
# 🧪 TEST 4
# Nickname 长度限制
# ============================================================


def test_update_me_rejects_long_nickname(
    user_service_client: httpx.Client,
    access_token: str,
) -> None:
    response = user_service_client.patch(
        ME_ENDPOINT,
        headers={
            "Authorization":
                f"Bearer {access_token}",
        },
        json={
            "nickname":
                "A" * 65,

            "avatarUrl":
                None,

            "bio":
                None,
        },
    )

    print_response(
        "Nickname > 64",
        response,
    )

    assert response.status_code == 400

    data = response.json()

    errors = data.get(
        "errors",
        {},
    )

    assert "nickname" in errors

    print()
    print(
        "✅ Nickname 最大 64 字符验证正常"
    )


# ============================================================
# 🧪 TEST 5
# Bio 长度限制
# ============================================================


def test_update_me_rejects_long_bio(
    user_service_client: httpx.Client,
    access_token: str,
) -> None:
    response = user_service_client.patch(
        ME_ENDPOINT,
        headers={
            "Authorization":
                f"Bearer {access_token}",
        },
        json={
            "nickname":
                "正常昵称",

            "avatarUrl":
                None,

            "bio":
                "B" * 501,
        },
    )

    print_response(
        "Bio > 500",
        response,
    )

    assert response.status_code == 400

    data = response.json()

    errors = data.get(
        "errors",
        {},
    )

    assert "bio" in errors

    print()
    print(
        "✅ Bio 最大 500 字符验证正常"
    )


# ============================================================
# 🧪 TEST 6
# AvatarUrl 长度限制
# ============================================================


def test_update_me_rejects_long_avatar_url(
    user_service_client: httpx.Client,
    access_token: str,
) -> None:
    response = user_service_client.patch(
        ME_ENDPOINT,
        headers={
            "Authorization":
                f"Bearer {access_token}",
        },
        json={
            "nickname":
                "正常昵称",

            "avatarUrl":
                "https://example.com/"
                + "a" * 2030,

            "bio":
                None,
        },
    )

    print_response(
        "AvatarUrl > 2048",
        response,
    )

    assert response.status_code == 400

    data = response.json()

    errors = data.get(
        "errors",
        {},
    )

    assert "avatarUrl" in errors

    print()
    print(
        "✅ AvatarUrl 最大 2048 字符验证正常"
    )