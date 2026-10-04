from __future__ import annotations

from collections.abc import Generator
import json
import secrets
import string
from dataclasses import dataclass
from urllib.parse import urlparse

import httpx
import pytest


FRONTEND_URL = "http://localhost:3000"
GATEWAY_URL = "https://localhost:7161"

TIMEOUT = 30.0


@dataclass(slots=True)
class TestAccount:
    email: str
    password: str


def random_text(length: int = 8) -> str:
    alphabet = (
        string.ascii_lowercase
        + string.digits
    )

    return "".join(
        secrets.choice(alphabet)
        for _ in range(length)
    )


def create_test_account() -> TestAccount:
    return TestAccount(
        email=(
            f"user_{random_text()}@example.com"
        ),
        password=(
            f"RedNote@{random_text(12)}Aa1"
        ),
    )


def print_title(title: str) -> None:
    print()
    print("=" * 90)
    print(f"🚀 {title}")
    print("=" * 90)


def print_response(
    response: httpx.Response,
    *,
    body: bool = True,
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
        "location",
    )

    if location:
        print(
            f"➡️ Location: {location}"
        )

    set_cookies = (
        response.headers.get_list(
            "set-cookie",
        )
    )

    if set_cookies:
        print("🍪 Set-Cookie:")

        for cookie in set_cookies:
            print(
                f"   {cookie}"
            )

    if not body:
        return

    text = response.text.strip()

    if not text:
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
        if len(text) > 1500:
            text = (
                text[:1500]
                + "\n... [内容已截断]"
            )

        print("📦 Body:")
        print(text)


def print_cookies(
    client: httpx.Client,
) -> None:
    print()
    print("🍪 当前 Cookie Jar:")

    cookies = list(
        client.cookies.jar
    )

    if not cookies:
        print(
            "   ⚠️ 当前没有 Cookie"
        )
        return

    for cookie in cookies:
        value = cookie.value or ""

        if len(value) > 50:
            value = (
                value[:20]
                + "..."
                + value[-10:]
            )

        print(
            f"   🔹 {cookie.name}"
            f" = {value}"
            f" | domain={cookie.domain}"
            f" | path={cookie.path}"
            f" | secure={cookie.secure}"
        )


def print_request_cookie_header(
    request: httpx.Request,
) -> None:
    print()

    print(
        "📤 即将发送的 Cookie Header:"
    )

    cookie_header = (
        request.headers.get(
            "cookie",
        )
    )

    print(
        cookie_header
        or "(none)"
    )


def get_csrf(
    client: httpx.Client,
) -> tuple[str, str]:
    response = client.get(
        f"{GATEWAY_URL}/api/v1/auth/csrf",
    )

    print_response(response)

    assert response.status_code == 200

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
    )

    assert token

    assert isinstance(
        header_name,
        str,
    )

    assert header_name

    print(
        f"🛡️ Antiforgery Header: "
        f"{header_name}"
    )

    return (
        token,
        header_name,
    )


@pytest.fixture(scope="session")
def account() -> TestAccount:
    account = create_test_account()

    print_title(
        "🧪 创建随机测试账号"
    )

    print(
        f"📧 Email: "
        f"{account.email}"
    )

    print(
        "🔑 Password: "
        f"{account.password[:8]}"
        "********"
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
    ) as client:
        yield client


@pytest.fixture(scope="session")
def authenticated_client(
    client: httpx.Client,
    account: TestAccount,
) -> httpx.Client:
    print_title(
        "1️⃣ 注册随机测试用户"
    )

    csrf_token, csrf_header = get_csrf(
        client,
    )

    register_response = client.post(
        f"{GATEWAY_URL}/api/v1/auth/register",
        headers={
            csrf_header:
                csrf_token,

            "Origin":
                FRONTEND_URL,
        },
        json={
            "email":
                account.email,

            "password":
                account.password,

            "displayName":
                "OIDC Test",

            "familyName":
                "RedNote",
        },
    )

    print_response(
        register_response,
    )

    assert register_response.status_code in {
        200,
        201,
        204,
    }

    print()
    print(
        "✅ 注册成功"
    )

    print_title(
        "2️⃣ ASP.NET Core Identity Cookie 登录"
    )

    csrf_token, csrf_header = get_csrf(
        client,
    )

    login_response = client.post(
        f"{GATEWAY_URL}/api/v1/auth/session/login",
        headers={
            csrf_header:
                csrf_token,

            "Origin":
                FRONTEND_URL,
        },
        json={
            "email":
                account.email,

            "password":
                account.password,
        },
    )

    print_response(
        login_response,
    )

    assert login_response.status_code in {
        200,
        204,
    }

    print()
    print(
        "✅ Identity 登录接口成功"
    )

    print_cookies(
        client,
    )

    return client


@pytest.fixture(scope="session")
def oidc_authenticated_client(
    authenticated_client: httpx.Client,
) -> httpx.Client:
    print_title(
        "4️⃣ 测试 nuxt-auth-utils OIDC 重定向链"
    )

    current_url = (
        f"{FRONTEND_URL}/auth/rednote"
    )

    max_redirects = 20

    reached_callback = False
    reached_frontend_root = False

    for index in range(
        1,
        max_redirects + 1,
    ):
        print()

        print(
            f"🔄 Redirect Step "
            f"{index}/{max_redirects}"
        )

        request = (
            authenticated_client.build_request(
                "GET",
                current_url,
            )
        )

        print_request_cookie_header(
            request,
        )

        response = (
            authenticated_client.send(
                request,
            )
        )

        print_response(
            response,
            body=False,
        )

        print_cookies(
            authenticated_client,
        )

        if response.status_code not in {
            301,
            302,
            303,
            307,
            308,
        }:
            print()

            print(
                "🏁 重定向链结束"
            )

            print_response(
                response,
            )

            break

        location = (
            response.headers.get(
                "location",
            )
        )

        assert location is not None
        assert location

        next_url = str(
            response.url.join(
                location,
            )
        )

        print()

        print(
            f"➡️ 下一跳: "
            f"{next_url}"
        )

        parsed = urlparse(
            next_url,
        )

        assert parsed.hostname in {
            "localhost",
            "127.0.0.1",
        }

        if (
            parsed.path
            == "/auth/rednote"
            and "code="
            in parsed.query
        ):
            reached_callback = True

            print()

            print(
                "🎯 已到达 Auth Utils "
                "OIDC callback"
            )

        if (
            next_url
            == f"{FRONTEND_URL}/"
        ):
            reached_frontend_root = True

            print()

            print(
                "🏠 已返回 Nuxt 首页"
            )

        current_url = (
            next_url
        )

    else:
        pytest.fail(
            "❌ 重定向次数超过限制。"
        )

    assert reached_callback, (
        "❌ 没有观察到 "
        "/auth/rednote?code=... "
        "OIDC callback"
    )

    assert reached_frontend_root, (
        "❌ OIDC callback 后没有返回首页"
    )

    print()

    print(
        "✅ OIDC Authorization Code "
        "流程已完成"
    )

    print_cookies(
        authenticated_client,
    )

    return authenticated_client


def test_identity_me(
    authenticated_client: httpx.Client,
) -> None:
    print_title(
        "3️⃣ 验证 Identity Cookie"
    )

    request = (
        authenticated_client.build_request(
            "GET",
            f"{GATEWAY_URL}/api/v1/auth/me",
        )
    )

    print_request_cookie_header(
        request,
    )

    response = (
        authenticated_client.send(
            request,
        )
    )

    print_response(
        response,
    )

    assert response.status_code == 200

    print()

    print(
        "✅ Identity Cookie "
        "已被正确识别"
    )


def test_auth_utils_session(
    oidc_authenticated_client: httpx.Client,
) -> None:
    print_title(
        "5️⃣ 检查 nuxt-auth-utils Session"
    )

    print_cookies(
        oidc_authenticated_client,
    )

    request = (
        oidc_authenticated_client.build_request(
            "GET",
            (
                f"{FRONTEND_URL}"
                "/api/_auth/session"
            ),
        )
    )

    print_request_cookie_header(
        request,
    )

    response = (
        oidc_authenticated_client.send(
            request,
        )
    )

    print_response(
        response,
    )

    assert response.status_code == 200

    data = response.json()

    print()
    print(
        "📦 Auth Utils Session:"
    )

    print(
        json.dumps(
            data,
            ensure_ascii=False,
            indent=2,
        )
    )

    user = data.get(
        "user",
    )

    print()
    print(
        "🔍 Session User:"
    )

    print(
        json.dumps(
            user,
            ensure_ascii=False,
            indent=2,
        )
    )

    assert isinstance(
        user,
        dict,
    ), (
        "❌ Auth Utils Session 存在，"
        "但 user 为空。"
    )

    assert (
        user.get(
            "authenticated",
        )
        is True
    )

    assert (
        user.get(
            "provider",
        )
        == "oidc"
    )

    print()

    print(
        "✅ nuxt-auth-utils "
        "登录 Session 已建立"
    )


def test_bff_users_me(
    oidc_authenticated_client: httpx.Client,
) -> None:
    print_title(
        "6️⃣ 测试 Nuxt BFF → UserService"
    )

    print_cookies(
        oidc_authenticated_client,
    )

    request = (
        oidc_authenticated_client.build_request(
            "GET",
            (
                f"{FRONTEND_URL}"
                "/api/users/me"
            ),
        )
    )

    print_request_cookie_header(
        request,
    )

    response = (
        oidc_authenticated_client.send(
            request,
        )
    )

    print_response(
        response,
    )

    assert response.status_code == 200

    print()

    print(
        "🎉 OIDC 全链路测试成功！"
    )

    print(
        "✅ Identity Cookie"
    )

    print(
        "✅ OpenIddict Authorization Code"
    )

    print(
        "✅ PKCE"
    )

    print(
        "✅ Nuxt Auth Utils Session"
    )

    print(
        "✅ Access Token"
    )

    print(
        "✅ Nitro BFF"
    )

    print(
        "✅ Gateway"
    )

    print(
        "✅ UserService JWT"
    )