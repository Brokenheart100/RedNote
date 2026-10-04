from __future__ import annotations

from collections.abc import Iterator
from urllib.parse import urlencode

import httpx
import pytest


# ============================================================
# 🔧 开发环境配置
# ============================================================

GATEWAY_BASE_URL = "https://localhost:7161"
FRONTEND_BASE_URL = "http://localhost:54710"

CLIENT_ID = "rednote-web"

REDIRECT_URI = (
    f"{FRONTEND_BASE_URL}/auth/oidc/callback"
)

SCOPES = [
    "openid",
    "profile",
    "rednote-api",
]


# ============================================================
# 🎨 输出工具
# ============================================================


def print_title(
    title: str,
) -> None:
    print()
    print("=" * 72)
    print(f"🚀 {title}")
    print("=" * 72)


def print_success(
    message: str,
) -> None:
    print(f"✅ {message}")


def print_info(
    message: str,
) -> None:
    print(f"ℹ️  {message}")


def print_warning(
    message: str,
) -> None:
    print(f"⚠️  {message}")


# ============================================================
# 🔌 HTTP Client Fixture
# ============================================================


@pytest.fixture
def client() -> Iterator[httpx.Client]:
    with httpx.Client(
        timeout=10.0,
        verify=False,
        follow_redirects=False,
    ) as http_client:
        yield http_client


# ============================================================
# 🔍 OIDC Discovery Fixture
# ============================================================


@pytest.fixture
def discovery(
    client: httpx.Client,
) -> dict:
    """
    从 Gateway 获取 OpenID Connect Discovery Document。

    其他 OIDC 测试复用这个 fixture，
    避免把某个 test 函数的返回值当作 fixture。
    """

    url = (
        f"{GATEWAY_BASE_URL}/"
        ".well-known/openid-configuration"
    )

    print_title(
        "加载 OpenID Connect Discovery"
    )

    print_info(
        f"请求地址: {url}"
    )

    response = client.get(
        url
    )

    print_info(
        f"HTTP 状态码: {response.status_code}"
    )

    assert response.status_code == 200, (
        "\n❌ OIDC Discovery 请求失败。\n"
        f"URL: {url}\n"
        f"Status: {response.status_code}\n"
        f"Body: {response.text}"
    )

    document = response.json()

    return document


# ============================================================
# 🧪 Test 1
# OIDC Discovery
# ============================================================


def test_discovery(
    discovery: dict,
) -> None:
    print_title(
        "测试 OpenID Connect Discovery"
    )

    required_fields = [
        "issuer",
        "authorization_endpoint",
        "token_endpoint",
    ]

    for field in required_fields:
        value = discovery.get(
            field
        )

        print_info(
            f"{field}: {value}"
        )

        assert value, (
            f"❌ Discovery 缺少必须字段: {field}"
        )

        print_success(
            f"{field} 存在"
        )

    optional_fields = [
        "userinfo_endpoint",
        "end_session_endpoint",
        "jwks_uri",
    ]

    for field in optional_fields:
        value = discovery.get(
            field
        )

        if value:
            print_success(
                f"{field}: {value}"
            )
        else:
            print_warning(
                f"Discovery 未提供可选字段: {field}"
            )

    grant_types = discovery.get(
        "grant_types_supported",
        [],
    )

    print_info(
        f"grant_types_supported: {grant_types}"
    )

    if grant_types:
        assert (
            "authorization_code"
            in grant_types
        ), (
            "❌ OpenIddict 没有声明 "
            "authorization_code grant。"
        )

        print_success(
            "Authorization Code Flow 已支持"
        )

    response_types = discovery.get(
        "response_types_supported",
        [],
    )

    print_info(
        f"response_types_supported: "
        f"{response_types}"
    )

    if response_types:
        assert (
            "code"
            in response_types
        ), (
            "❌ OpenIddict 没有声明 "
            "response_type=code。"
        )

        print_success(
            "response_type=code 已支持"
        )

    pkce_methods = discovery.get(
        "code_challenge_methods_supported",
        [],
    )

    print_info(
        "code_challenge_methods_supported: "
        f"{pkce_methods}"
    )

    if pkce_methods:
        assert (
            "S256"
            in pkce_methods
        ), (
            "❌ OpenIddict 没有声明 "
            "PKCE S256 支持。"
        )

        print_success(
            "PKCE S256 已支持"
        )

    print_success(
        "OIDC Discovery 测试完成"
    )


# ============================================================
# 🧪 Test 2
# Nuxt Frontend
# ============================================================


def test_frontend(
    client: httpx.Client,
) -> None:
    print_title(
        "测试 Nuxt Frontend"
    )

    print_info(
        f"Frontend 地址: {FRONTEND_BASE_URL}"
    )

    response = client.get(
        FRONTEND_BASE_URL
    )

    print_info(
        f"HTTP 状态码: {response.status_code}"
    )

    #
    # 只验证 Nuxt 服务是否成功响应。
    #
    # 当前新前端可能还没有首页，
    # 因此 404 不代表服务不可用。
    #
    assert response.status_code < 500, (
        "\n❌ Nuxt Frontend 服务异常。\n"
        f"Status: {response.status_code}\n"
        f"Body: {response.text[:2000]}"
    )

    if response.status_code == 404:
        print_warning(
            "Nuxt 服务正常响应，但当前没有首页路由。"
        )
    else:
        print_success(
            "Nuxt Frontend 页面可以访问"
        )

    print_success(
        "Nuxt Frontend 服务运行正常"
    )

# ============================================================
# 🧪 Test 3
# Authorization Endpoint
# ============================================================


def test_authorization_endpoint(
    client: httpx.Client,
    discovery: dict,
) -> None:
    print_title(
        "测试 Authorization Code + PKCE"
    )

    authorization_endpoint = (
        discovery[
            "authorization_endpoint"
        ]
    )

    print_info(
        "Authorization Endpoint: "
        f"{authorization_endpoint}"
    )

    print_info(
        f"Client ID: {CLIENT_ID}"
    )

    print_info(
        f"Redirect URI: {REDIRECT_URI}"
    )

    print_info(
        f"Scopes: {' '.join(SCOPES)}"
    )

    #
    # 这是一个符合 S256 长度要求的测试 challenge。
    #
    # 当前测试只需要验证：
    #
    # client_id
    # redirect_uri
    # response_type
    # scope
    # PKCE
    #
    # 是否被 OpenIddict 接受。
    #

    code_challenge = (
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
    )

    params = {
        "client_id":
            CLIENT_ID,

        "redirect_uri":
            REDIRECT_URI,

        "response_type":
            "code",

        "scope":
            " ".join(
                SCOPES
            ),

        "state":
            "rednote-python-test-state",

        "nonce":
            "rednote-python-test-nonce",

        "code_challenge":
            code_challenge,

        "code_challenge_method":
            "S256",
    }

    url = (
        f"{authorization_endpoint}?"
        f"{urlencode(params)}"
    )

    print_info(
        f"请求地址: {url}"
    )

    response = client.get(
        url
    )

    print_info(
        f"HTTP 状态码: {response.status_code}"
    )

    location = response.headers.get(
        "location"
    )

    if location:
        print_info(
            f"Location: {location}"
        )

    response_body = (
        response.text
    )

    combined_response = (
        f"{location or ''}\n"
        f"{response_body}"
    )

    combined_lower = (
        combined_response.lower()
    )

    #
    # --------------------------------------------------------
    # Client ID
    # --------------------------------------------------------
    #

    assert (
        "invalid_client"
        not in combined_lower
    ), (
        "\n❌ OpenIddict 拒绝了 rednote-web。\n"
        f"ClientId: {CLIENT_ID}\n"
        f"Response: {combined_response[:3000]}"
    )

    #
    # --------------------------------------------------------
    # Redirect URI
    # --------------------------------------------------------
    #

    redirect_uri_rejected = (
        "redirect_uri"
        in combined_lower
        and (
            "invalid"
            in combined_lower
            or
            "not valid"
            in combined_lower
            or
            "not registered"
            in combined_lower
        )
    )

    assert not redirect_uri_rejected, (
        "\n❌ OpenIddict 拒绝了 redirect_uri。\n"
        f"RedirectUri: {REDIRECT_URI}\n"
        f"Response: {combined_response[:3000]}"
    )

    #
    # --------------------------------------------------------
    # Scope
    # --------------------------------------------------------
    #

    invalid_scope = (
        "invalid_scope"
        in combined_lower
    )

    assert not invalid_scope, (
        "\n❌ OpenIddict 拒绝了请求的 Scope。\n"
        f"Scopes: {SCOPES}\n"
        f"Response: {combined_response[:3000]}"
    )

    #
    # --------------------------------------------------------
    # PKCE
    # --------------------------------------------------------
    #

    pkce_rejected = (
        "code_challenge"
        in combined_lower
        and "invalid"
        in combined_lower
    )

    assert not pkce_rejected, (
        "\n❌ OpenIddict 拒绝了 PKCE 参数。\n"
        f"Response: {combined_response[:3000]}"
    )

    #
    # --------------------------------------------------------
    # 最终响应
    # --------------------------------------------------------
    #

    assert response.status_code in {
        200,
        302,
        303,
        307,
        308,
    }, (
        "\n❌ Authorization Endpoint "
        "返回非预期状态码。\n"
        f"Status: {response.status_code}\n"
        f"Body: {response_body[:3000]}"
    )

    #
    # 如果 OpenIddict 接受协议参数，
    # 未登录用户通常会：
    #
    # - 跳登录页面
    # - 返回登录页面
    # - challenge 到 Identity 登录流程
    #
    # 这些都属于当前测试的成功结果。
    #

    print_success(
        "Client ID 未被拒绝"
    )

    print_success(
        "Redirect URI 未被拒绝"
    )

    print_success(
        "Scopes 未被拒绝"
    )

    print_success(
        "PKCE 参数未被拒绝"
    )

    print_success(
        "Authorization 请求已进入正常认证流程"
    )


def test_nuxt_oidc_login_redirect(
    client: httpx.Client,
) -> None:
    print_title(
        "测试 Nuxt OIDC Login Redirect"
    )

    login_url = (
        f"{FRONTEND_BASE_URL}/auth/oidc/login"
    )

    print_info(
        f"Login URL: {login_url}"
    )

    response = client.get(
        login_url
    )

    print_info(
        f"HTTP 状态码: {response.status_code}"
    )

    location = response.headers.get(
        "location"
    )

    if location:
        print_info(
            f"Location: {location}"
        )

    assert response.status_code in {
        302,
        303,
        307,
        308,
    }, (
        "\n❌ nuxt-oidc-auth 登录入口"
        "没有返回重定向。\n"
        f"Status: {response.status_code}\n"
        f"Body: {response.text[:3000]}"
    )

    assert location, (
        "\n❌ 登录响应没有 Location Header。"
    )

    location_lower = (
        location.lower()
    )

    assert (
        "/connect/authorize"
        in location_lower
    ), (
        "\n❌ Nuxt 没有重定向到 "
        "OpenIddict Authorization Endpoint。\n"
        f"Location: {location}"
    )

    assert (
        "client_id=rednote-web"
        in location_lower
    ), (
        "\n❌ Authorization Request "
        "缺少正确的 client_id。\n"
        f"Location: {location}"
    )

    assert (
        "response_type=code"
        in location_lower
    ), (
        "\n❌ Authorization Request "
        "不是 Authorization Code Flow。\n"
        f"Location: {location}"
    )

    assert (
        "code_challenge="
        in location_lower
    ), (
        "\n❌ Authorization Request "
        "没有包含 PKCE code_challenge。\n"
        f"Location: {location}"
    )

    assert (
        "code_challenge_method=s256"
        in location_lower
    ), (
        "\n❌ Authorization Request "
        "没有使用 PKCE S256。\n"
        f"Location: {location}"
    )

    assert (
        "state="
        in location_lower
    ), (
        "\n❌ Authorization Request "
        "缺少 state。\n"
        f"Location: {location}"
    )

    print_success(
        "Nuxt OIDC 登录入口可访问"
    )

    print_success(
        "成功跳转到 /connect/authorize"
    )

    print_success(
        "client_id=rednote-web"
    )

    print_success(
        "Authorization Code Flow"
    )

    print_success(
        "PKCE S256"
    )

    print_success(
        "state 参数存在"
    )
    
# ============================================================
# 📋 Test Summary
# ============================================================


def test_configuration_summary() -> None:
    print_title(
        "当前测试配置"
    )

    print_info(
        f"Gateway  : {GATEWAY_BASE_URL}"
    )

    print_info(
        f"Frontend : {FRONTEND_BASE_URL}"
    )

    print_info(
        f"Client ID: {CLIENT_ID}"
    )

    print_info(
        f"Callback : {REDIRECT_URI}"
    )

    print_info(
        f"Scopes   : {' '.join(SCOPES)}"
    )

    print_success(
        "配置输出完成"
    )