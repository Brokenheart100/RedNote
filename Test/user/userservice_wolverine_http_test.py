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
# 🔧 RedNote 当前开发环境
# ============================================================

# Gateway
#
# 登录、注册、OpenIddict 都通过 Gateway。
#
# Aspire:
#   HTTPS https://localhost:7161
#   HTTP  http://localhost:5000
#
# Identity Cookie 是 Secure，
# pytest/httpx 这里使用 HTTPS Gateway。
# GATEWAY_BASE_URL = "http://localhost:5000"
GATEWAY_BASE_URL = "https://localhost:7161"


# UserService
#
# Aspire:
#   HTTPS https://localhost:7135
#   HTTP  http://localhost:5003
#
# 这里直接使用 HTTP，
# 避免开发证书影响 UserService HTTP 测试。
USER_SERVICE_BASE_URL = "http://localhost:5003"


# ============================================================
# 🔐 OpenIddict
# ============================================================

CLIENT_ID = "rednote-web"

REDIRECT_URI = (
    "http://localhost:3000/auth/rednote"
)

SCOPES = (
    "openid "
    "profile "
    "email "
    "offline_access "
    "rednote-api"
)


# ============================================================
# 🌐 Endpoint
# ============================================================

CSRF_ENDPOINT = (
    "/api/v1/auth/csrf"
)

REGISTER_ENDPOINT = (
    "/api/v1/auth/register"
)

SESSION_LOGIN_ENDPOINT = (
    "/api/v1/auth/session/login"
)

AUTHORIZE_ENDPOINT = (
    "/connect/authorize"
)

TOKEN_ENDPOINT = (
    "/connect/token"
)

USER_SERVICE_ME_ENDPOINT = (
    "/api/v1/users/me"
)


# ============================================================
# ⚙️ HTTP
# ============================================================

TIMEOUT_SECONDS = 20.0

VERIFY_GATEWAY_TLS = False


# ============================================================
# 📦 数据结构
# ============================================================

type JsonObject = dict[str, Any]


# ============================================================
# 🛠️ Helper
# ============================================================


def pretty_json(
    value: object,
) -> str:
    return json.dumps(
        value,
        ensure_ascii=False,
        indent=2,
    )


def random_text(
    length: int = 12,
) -> str:
    alphabet = (
        string.ascii_lowercase
        + string.digits
    )

    return "".join(
        secrets.choice(alphabet)
        for _ in range(length)
    )


def create_test_account() -> JsonObject:
    suffix = random_text(10)

    email = (
        f"wolverine_{suffix}"
        "@example.com"
    )

    display_name = (
        f"Wolverine {suffix}"
    )

    password = (
        f"RedNote@{random_text(12)}Aa1"
    )

    return {
        "email": email,
        "displayName": display_name,
        "familyName": "RedNote",
        "password": password,
        "confirmPassword": password,
    }


def base64url_encode(
    value: bytes,
) -> str:
    return (
        base64.urlsafe_b64encode(
            value,
        )
        .decode("ascii")
        .rstrip("=")
    )


def create_pkce() -> tuple[str, str]:
    """
    RFC 7636 PKCE

    verifier:
        随机高熵字符串

    challenge:
        BASE64URL(
            SHA256(verifier)
        )
    """

    code_verifier = (
        secrets.token_urlsafe(64)
    )

    digest = hashlib.sha256(
        code_verifier.encode(
            "ascii",
        )
    ).digest()

    code_challenge = (
        base64url_encode(
            digest,
        )
    )

    return (
        code_verifier,
        code_challenge,
    )


def print_response(
    title: str,
    response: httpx.Response,
) -> None:
    print()
    print(
        f"📥 {title}: "
        f"HTTP {response.status_code}"
    )

    if not response.content:
        print("   (empty)")
        return

    content_type = (
        response.headers
        .get(
            "content-type",
            "",
        )
        .lower()
    )

    print(
        "📦 Content-Type:",
        content_type or "(empty)",
    )

    print()

    if (
        "application/json"
        in content_type
    ):
        try:
            print(
                pretty_json(
                    response.json(),
                )
            )
        except ValueError:
            print(
                response.text[:3000],
            )
    else:
        print(
            response.text[:3000],
        )


# ============================================================
# 🌐 Client fixtures
# ============================================================


@pytest.fixture(scope="session")
def gateway_client() -> Iterator[httpx.Client]:
    print()
    print(
        "🌐 创建 Gateway HTTP Client"
    )

    with httpx.Client(
        base_url=GATEWAY_BASE_URL,
        timeout=TIMEOUT_SECONDS,
        verify=VERIFY_GATEWAY_TLS,
        follow_redirects=False,
    ) as client:
        yield client

    print()
    print(
        "🧹 Gateway HTTP Client 已关闭"
    )


@pytest.fixture(scope="session")
def user_service_client() -> Iterator[httpx.Client]:
    print()
    print(
        "👤 创建 UserService HTTP Client"
    )

    with httpx.Client(
        base_url=USER_SERVICE_BASE_URL,
        timeout=TIMEOUT_SECONDS,
        follow_redirects=False,
    ) as client:
        yield client

    print()
    print(
        "🧹 UserService HTTP Client 已关闭"
    )


# ============================================================
# 🛡️ CSRF
# ============================================================


def get_csrf(
    client: httpx.Client,
) -> tuple[str, str]:
    response = client.get(
        CSRF_ENDPOINT,
        headers={
            "Accept":
                "application/json",

            "X-Request-ID":
                "pytest-csrf",
        },
    )

    print_response(
        "GET CSRF",
        response,
    )

    assert (
        response.status_code
        == 200
    ), (
        "\n"
        "❌ 获取 CSRF Token 失败。\n"
        f"HTTP {response.status_code}"
    )

    data = response.json()

    token = data.get(
        "token",
    )

    header_name = data.get(
        "headerName",
    )

    assert isinstance(
        token,
        str,
    ) and token, (
        "❌ CSRF Response 缺少 token。"
    )

    assert isinstance(
        header_name,
        str,
    ) and header_name, (
        "❌ CSRF Response "
        "缺少 headerName。"
    )

    print()
    print(
        "✅ CSRF Token 获取成功"
    )

    print(
        "🏷️ Header:",
        header_name,
    )

    return (
        token,
        header_name,
    )


# ============================================================
# 📝 Register
# ============================================================


def register_user(
    client: httpx.Client,
    account: JsonObject,
) -> None:
    print()
    print("=" * 72)
    print(
        "📝 注册 pytest 测试用户"
    )
    print("=" * 72)

    print()
    print(
        "📧 Email:",
        account["email"],
    )

    print(
        "👤 DisplayName:",
        account["displayName"],
    )

    csrf_token, csrf_header = (
        get_csrf(
            client,
        )
    )

    response = client.post(
        REGISTER_ENDPOINT,
        json={
            "email":
                account["email"],

            "displayName":
                account[
                    "displayName"
                ],

            "familyName":
                account[
                    "familyName"
                ],

            "password":
                account["password"],

            "confirmPassword":
                account[
                    "confirmPassword"
                ],
        },
        headers={
            "Accept":
                "application/json",

            csrf_header:
                csrf_token,

            "X-Request-ID":
                "pytest-register",
        },
    )

    print_response(
        "POST Register",
        response,
    )

    assert response.status_code in {
        200,
        201,
        204,
    }, (
        "\n"
        "❌ Identity Register 失败。\n"
        f"HTTP {response.status_code}"
    )

    print()
    print(
        "✅ Identity 用户注册成功"
    )


# ============================================================
# 🔐 Cookie Login
# ============================================================


def login_user(
    client: httpx.Client,
    account: JsonObject,
) -> None:
    print()
    print("=" * 72)
    print(
        "🔐 Identity Cookie Login"
    )
    print("=" * 72)


    csrf_token, csrf_header = (
        get_csrf(
            client,
        )
    )

    response = client.post(
        SESSION_LOGIN_ENDPOINT,
        json={
            "email":
                account["email"],

            "password":
                account["password"],
        },
        headers={
            "Accept":
                "application/json",

            csrf_header:
                csrf_token,

            "X-Request-ID":
                "pytest-session-login",
        },
    )

    print_response(
        "POST Session Login",
        response,
    )

    assert response.status_code in {
        200,
        204,
    }, (
        "\n"
        "❌ Identity Cookie Login 失败。\n"
        f"HTTP {response.status_code}"
    )

    cookie_names = {
        cookie.name
        for cookie
        in client.cookies.jar
    }

    print()
    print(
        "🍪 当前 Cookie:",
        sorted(
            cookie_names,
        ),
    )

    assert (
        "__Host-RedNote.Identity"
        in cookie_names
    ), (
        "\n"
        "❌ 登录返回成功，"
        "但 Cookie Jar 中不存在 "
        "__Host-RedNote.Identity。"
    )

    print()
    print(
        "✅ Identity Cookie Login 成功"
    )


# ============================================================
# 🔑 Authorization Code + PKCE
# ============================================================


def authorize(
    client: httpx.Client,
) -> tuple[str, str]:
    print()
    print("=" * 72)
    print(
        "🔑 OpenIddict Authorization Code + PKCE"
    )
    print("=" * 72)

    (
        code_verifier,
        code_challenge,
    ) = create_pkce()

    state = (
        secrets.token_urlsafe(
            24,
        )
    )

    nonce = (
        secrets.token_urlsafe(
            24,
        )
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
                code_challenge,

            "code_challenge_method":
                "S256",

            "state":
                state,

            "nonce":
                nonce,
        },
        headers={
            "X-Request-ID":
                "pytest-authorize",
        },
    )

    print()
    print(
        "📥 Authorization:",
        response.status_code,
    )

    location = (
        response.headers
        .get(
            "location",
        )
    )

    print(
        "↪️ Location:",
        location,
    )

    assert response.status_code in {
        302,
        303,
    }, (
        "\n"
        "❌ Authorization Endpoint "
        "没有返回 Redirect。\n"
        f"HTTP {response.status_code}\n"
        f"Body:\n{response.text[:2000]}"
    )

    assert location, (
        "❌ Authorization Response "
        "缺少 Location Header。"
    )

    parsed = urlparse(
        location,
    )

    query = parse_qs(
        parsed.query,
    )

    if "error" in query:
        pytest.fail(
            "\n"
            "❌ OpenIddict Authorization "
            "返回 OAuth Error。\n"
            f"{pretty_json(query)}"
        )

    code_values = query.get(
        "code",
    )

    assert code_values, (
        "\n"
        "❌ Callback URL "
        "中没有 authorization code。\n"
        f"Location: {location}"
    )

    state_values = query.get(
        "state",
    )

    assert (
        state_values
        and state_values[0]
        == state
    ), (
        "❌ OAuth state 校验失败。"
    )

    authorization_code = (
        code_values[0]
    )

    print()
    print(
        "✅ Authorization Code 获取成功"
    )

    return (
        authorization_code,
        code_verifier,
    )


# ============================================================
# 🎫 Token Exchange
# ============================================================


def exchange_token(
    client: httpx.Client,
    authorization_code: str,
    code_verifier: str,
) -> JsonObject:
    print()
    print("=" * 72)
    print(
        "🎫 Authorization Code → Access Token"
    )
    print("=" * 72)

    response = client.post(
        TOKEN_ENDPOINT,
        data={
            "grant_type":
                "authorization_code",

            "client_id":
                CLIENT_ID,

            "code":
                authorization_code,

            "redirect_uri":
                REDIRECT_URI,

            "code_verifier":
                code_verifier,
        },
        headers={
            "Accept":
                "application/json",

            "Content-Type":
                "application/"
                "x-www-form-urlencoded",

            "X-Request-ID":
                "pytest-token-exchange",
        },
    )


    print()
    print(
        "📥 Token Endpoint:",
        response.status_code,
    )

    if response.status_code != 200:
        print_response(
            "Token Exchange",
            response,
        )

    assert (
        response.status_code
        == 200
    ), (
        "\n"
        "❌ Authorization Code "
        "换取 Token 失败。\n"
        f"HTTP {response.status_code}"
    )

    data = response.json()

    access_token = data.get(
        "access_token",
    )

    assert isinstance(
        access_token,
        str,
    ) and access_token, (
        "❌ Token Response "
        "缺少 access_token。"
    )

    print()
    print(
        "✅ Access Token 获取成功"
    )

    print(
        "🔄 Refresh Token:",
        "✅"
        if data.get(
            "refresh_token",
        )
        else "❌",
    )

    print(
        "🏷️ Token Type:",
        data.get(
            "token_type",
        ),
    )

    print(
        "⏱️ Expires In:",
        data.get(
            "expires_in",
        ),
    )

    # 绝对不要打印真实 Token。
    return data


# ============================================================
# 🔐 Auth fixture
# ============================================================


@pytest.fixture(scope="session")
def oidc_tokens(
    gateway_client: httpx.Client,
) -> JsonObject:
    account = (
        create_test_account()
    )

    register_user(
        gateway_client,
        account,
    )

    login_user(
        gateway_client,
        account,
    )

    (
        authorization_code,
        code_verifier,
    ) = authorize(
        gateway_client,
    )

    tokens = exchange_token(
        gateway_client,
        authorization_code,
        code_verifier,
    )

    print()
    print("=" * 72)
    print(
        "🎉 pytest OIDC 登录流程完成"
    )
    print("=" * 72)

    return tokens


# ============================================================
# 🧪 TEST 1
# Wolverine HTTP 必须要求登录
# ============================================================


def test_wolverine_http_requires_authentication(
    user_service_client: httpx.Client,
) -> None:
    print()
    print("=" * 72)
    print(
        "🧪 TEST 1: 无 Token"
    )
    print("=" * 72)

    response = user_service_client.get(
        USER_SERVICE_ME_ENDPOINT,
        headers={
            "Accept":
                "application/json",

            "X-Request-ID":
                "pytest-wolverine-no-auth",
        },
    )

    print()
    print(
        "📥 HTTP:",
        response.status_code,
    )

    assert (
        response.status_code
        == 401
    ), (
        "\n"
        "❌ 未携带 Token "
        "应该返回 401。\n"
        f"实际: {response.status_code}"
    )

    print()
    print(
        "✅ Wolverine HTTP Authorization 正常"
    )


# ============================================================
# 🧪 TEST 2
# JWT + Wolverine HTTP
# ============================================================


def test_wolverine_http_with_real_access_token(
    user_service_client: httpx.Client,
    oidc_tokens: JsonObject,
) -> None:
    print()
    print("=" * 72)
    print(
        "🧪 TEST 2: JWT + Wolverine HTTP"
    )
    print("=" * 72)

    access_token = (
        oidc_tokens[
            "access_token"
        ]
    )

    response = user_service_client.get(
        USER_SERVICE_ME_ENDPOINT,
        headers={
            "Accept":
                "application/json",

            "Authorization":
                f"Bearer {access_token}",

            "X-Request-ID":
                "pytest-wolverine-auth",
        },
    )

    print_response(
        "GET UserService /me",
        response,
    )

    assert (
        response.status_code
        == 200
    ), (
        "\n"
        "❌ JWT + Wolverine HTTP "
        "完整链路失败。\n"
        f"HTTP {response.status_code}"
    )

    data = response.json()

    assert data.get(
        "userId",
    ), (
        "❌ Response 缺少 userId。"
    )

    assert (
        "nickname"
        in data
    )

    assert (
        "avatarUrl"
        in data
    )

    assert (
        "followersCount"
        in data
    )

    assert (
        "followingCount"
        in data
    )

    print()
    print(
        "👤 UserId:",
        data.get(
            "userId",
        ),
    )

    print(
        "🪪 Nickname:",
        data.get(
            "nickname",
        ),
    )

    print(
        "🖼️ Avatar:",
        data.get(
            "avatarUrl",
        ),
    )

    print(
        "👥 Followers:",
        data.get(
            "followersCount",
        ),
    )

    print(
        "➡️ Following:",
        data.get(
            "followingCount",
        ),
    )

    print()
    print(
        "🎉 Wolverine HTTP "
        "完整认证测试通过"
    )


# ============================================================
# 🧪 TEST 3
# 检查 nickname 初始化
# ============================================================


def test_current_user_nickname(
    user_service_client: httpx.Client,
    oidc_tokens: JsonObject,
) -> None:
    print()
    print("=" * 72)
    print(
        "🧪 TEST 3: Nickname 初始化"
    )
    print("=" * 72)

    access_token = (
        oidc_tokens[
            "access_token"
        ]
    )

    response = user_service_client.get(
        USER_SERVICE_ME_ENDPOINT,
        headers={
            "Authorization":
                f"Bearer {access_token}",

            "X-Request-ID":
                "pytest-nickname",
        },
    )

    assert (
        response.status_code
        == 200
    )

    data = response.json()

    nickname = data.get(
        "nickname",
    )

    print()
    print(
        "🪪 Nickname:",
        nickname,
    )

    if nickname:
        print()
        print(
            "✅ name claim → "
            "UserProfile.Nickname 正常"
        )
    else:
        print()
        print(
            "⚠️ Nickname 仍然是 null"
        )

        print(
            "⚠️ 下一步检查 "
            "Access Token 的 name claim"
        )