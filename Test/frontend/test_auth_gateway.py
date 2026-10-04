from __future__ import annotations

from collections.abc import Iterator

import httpx
import pytest


# ============================================================
# 🔧 当前 Aspire 开发环境
# ============================================================

GATEWAY_BASE_URL = "https://localhost:7161"

IDENTITY_BASE_URL = "https://localhost:7025"

FRONTEND_ORIGIN = "http://localhost:3000"

CSRF_PATH = "/api/v1/auth/csrf"


# ============================================================
# 🎨 输出
# ============================================================


def title(message: str) -> None:
    print()
    print("=" * 72)
    print(f"🚀 {message}")
    print("=" * 72)


def info(message: str) -> None:
    print(f"ℹ️  {message}")


def success(message: str) -> None:
    print(f"✅ {message}")


def warning(message: str) -> None:
    print(f"⚠️  {message}")


def failure(message: str) -> None:
    print(f"❌ {message}")


# ============================================================
# 🌐 HTTP Client
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
# 1️⃣ IdentityService 直接测试
# ============================================================


def test_identity_csrf_direct(
    client: httpx.Client,
) -> None:
    title(
        "IdentityService 直接测试 CSRF Endpoint"
    )

    url = (
        f"{IDENTITY_BASE_URL}"
        f"{CSRF_PATH}"
    )

    info(
        f"GET {url}"
    )

    response = client.get(
        url
    )

    info(
        f"HTTP Status: {response.status_code}"
    )

    content_type = response.headers.get(
        "content-type"
    )

    info(
        f"Content-Type: {content_type}"
    )

    if response.text:
        info(
            "Response Body:"
        )

        print(
            response.text[:2000]
        )

    if response.status_code == 404:
        failure(
            "IdentityService 自己就返回了 404。"
        )

        failure(
            "这意味着问题不是 YARP，"
            "而是 IdentityService 当前没有映射 "
            f"{CSRF_PATH}。"
        )

    else:
        success(
            "IdentityService 找到了 CSRF Endpoint。"
        )

    assert response.status_code != 404, (
        "\n❌ IdentityService 直接访问 CSRF Endpoint 返回 404。\n"
        f"URL: {url}\n"
        "请检查 IdentityService 实际映射的 CSRF/Antiforgery 路由。"
    )


# ============================================================
# 2️⃣ Gateway Proxy 测试
# ============================================================


def test_gateway_csrf_proxy(
    client: httpx.Client,
) -> None:
    title(
        "Gateway → IdentityService CSRF Proxy"
    )

    url = (
        f"{GATEWAY_BASE_URL}"
        f"{CSRF_PATH}"
    )

    info(
        f"GET {url}"
    )

    response = client.get(
        url
    )

    info(
        f"HTTP Status: {response.status_code}"
    )

    if response.text:
        info(
            "Response Body:"
        )

        print(
            response.text[:2000]
        )

    if response.status_code == 404:
        failure(
            "Gateway 返回 404。"
        )

        warning(
            "结合 Gateway 日志，如果已经显示 "
            "'Executing endpoint identity-route'，"
            "说明 YARP 路由其实匹配成功了。"
        )

        warning(
            "此时 404 通常是下游 IdentityService 返回的。"
        )

    else:
        success(
            "Gateway 已经成功代理 CSRF Endpoint。"
        )

    assert response.status_code != 404, (
        "\n❌ Gateway CSRF 请求返回 404。\n"
        f"URL: {url}"
    )


# ============================================================
# 3️⃣ CORS Preflight
# ============================================================


def test_gateway_cors_preflight(
    client: httpx.Client,
) -> None:
    title(
        "Gateway CORS Preflight"
    )

    url = (
        f"{GATEWAY_BASE_URL}"
        f"{CSRF_PATH}"
    )

    info(
        f"OPTIONS {url}"
    )

    info(
        f"Origin: {FRONTEND_ORIGIN}"
    )

    response = client.options(
        url,
        headers={
            "Origin":
                FRONTEND_ORIGIN,

            "Access-Control-Request-Method":
                "GET",

            "Access-Control-Request-Headers":
                "content-type",
        },
    )

    info(
        f"HTTP Status: {response.status_code}"
    )

    allow_origin = response.headers.get(
        "access-control-allow-origin"
    )

    allow_credentials = response.headers.get(
        "access-control-allow-credentials"
    )

    allow_methods = response.headers.get(
        "access-control-allow-methods"
    )

    info(
        f"Access-Control-Allow-Origin: "
        f"{allow_origin}"
    )

    info(
        f"Access-Control-Allow-Credentials: "
        f"{allow_credentials}"
    )

    info(
        f"Access-Control-Allow-Methods: "
        f"{allow_methods}"
    )

    assert allow_origin == FRONTEND_ORIGIN, (
        "\n❌ Gateway 没有允许当前 Frontend Origin。\n"
        f"Expected: {FRONTEND_ORIGIN}\n"
        f"Actual:   {allow_origin}"
    )

    assert (
        allow_credentials
        and allow_credentials.lower()
        == "true"
    ), (
        "\n❌ Gateway 没有允许 Credentials/Cookie。\n"
        f"Actual: {allow_credentials}"
    )

    success(
        "Frontend Origin 已被 Gateway CORS 接受。"
    )

    success(
        "Credentials/Cookie 已允许。"
    )


# ============================================================
# 4️⃣ 模拟真正浏览器 GET
# ============================================================


def test_gateway_csrf_with_browser_origin(
    client: httpx.Client,
) -> None:
    title(
        "模拟浏览器访问 Gateway CSRF"
    )

    url = (
        f"{GATEWAY_BASE_URL}"
        f"{CSRF_PATH}"
    )

    response = client.get(
        url,
        headers={
            "Origin":
                FRONTEND_ORIGIN,
        },
    )

    info(
        f"HTTP Status: {response.status_code}"
    )

    allow_origin = response.headers.get(
        "access-control-allow-origin"
    )

    allow_credentials = response.headers.get(
        "access-control-allow-credentials"
    )

    info(
        f"Access-Control-Allow-Origin: "
        f"{allow_origin}"
    )

    info(
        f"Access-Control-Allow-Credentials: "
        f"{allow_credentials}"
    )

    assert allow_origin == FRONTEND_ORIGIN, (
        "\n❌ 实际 GET Response 没有正确的 CORS Header。\n"
        f"Expected Origin: {FRONTEND_ORIGIN}\n"
        f"Actual Origin:   {allow_origin}"
    )

    assert response.status_code != 404, (
        "\n❌ CORS 即使通过，CSRF Endpoint 本身仍然返回 404。\n"
        "请检查 IdentityService Endpoint 映射。"
    )

    success(
        "浏览器 Origin + Gateway CORS 正常。"
    )

    success(
        "CSRF Endpoint 可以访问。"
    )