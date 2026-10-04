from __future__ import annotations

from collections.abc import Generator
from dataclasses import dataclass
import json
import secrets
import string

import httpx
import pytest


GATEWAY_URL = "https://localhost:7161"
FRONTEND_URL = "http://localhost:3000"

TIMEOUT = 30.0


@dataclass(slots=True)
class TestAccount:
    email: str
    password: str
    display_name: str
    family_name: str


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


def create_test_account() -> TestAccount:
    suffix = random_text()

    return TestAccount(
        email=(
            f"wolverine_{suffix}"
            "@example.com"
        ),
        password=(
            f"RedNote@"
            f"{random_text(12)}"
            "Aa1"
        ),
        display_name=(
            f"Wolverine测试用户 {suffix}"
        ),
        family_name="RedNote",
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
        "📡 "
        f"{response.request.method} "
        f"{response.request.url}"
    )

    print(
        f"📥 HTTP "
        f"{response.status_code}"
    )

    location = response.headers.get(
        "location"
    )

    if location:
        print(
            f"➡️ Location: "
            f"{location}"
        )

    set_cookies = (
        response.headers.get_list(
            "set-cookie"
        )
    )

    if set_cookies:
        print(
            "🍪 Set-Cookie:"
        )

        for cookie in set_cookies:
            print(
                f"   🔹 {cookie}"
            )

    body = response.text.strip()

    if not body:
        print(
            "📦 Body: <empty>"
        )
        return

    try:
        data = response.json()

        print(
            "📦 Body:"
        )

        print(
            json.dumps(
                data,
                ensure_ascii=False,
                indent=2,
            )
        )
    except Exception:
        print(
            "📦 Body:"
        )

        print(
            body[:2000]
        )


def print_cookies(
    client: httpx.Client,
) -> None:
    print()
    print(
        "🍪 当前 Cookie Jar:"
    )

    cookies = list(
        client.cookies.jar
    )

    if not cookies:
        print(
            "   ⚠️ 没有 Cookie"
        )
        return

    for cookie in cookies:
        value = cookie.value or ""

        if len(value) > 60:
            value = (
                value[:25]
                + "..."
                + value[-15:]
            )

        print(
            f"   🔹 {cookie.name}"
            f" = {value}"
            f" | domain={cookie.domain}"
            f" | path={cookie.path}"
            f" | secure={cookie.secure}"
        )


@pytest.fixture(scope="session")
def account() -> TestAccount:
    account = (
        create_test_account()
    )

    print_title(
        "🧪 创建随机测试账号"
    )

    print(
        f"📧 Email: "
        f"{account.email}"
    )

    print(
        f"👤 DisplayName: "
        f"{account.display_name}"
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
    ), (
        "❌ 获取 CSRF Token 失败"
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

    print(
        "✅ CSRF Token 获取成功"
    )

    print(
        f"🛡️ Header: "
        f"{header_name}"
    )

    print_cookies(
        client
    )

    return (
        token,
        header_name,
    )


def test_01_me_before_login_returns_401(
    client: httpx.Client,
) -> None:
    print_title(
        "1️⃣ 未登录访问 /me"
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
        == 401
    )

    print(
        "✅ 未登录正确返回 401"
    )


def test_02_register(
    client: httpx.Client,
    account: TestAccount,
) -> None:
    print_title(
        "2️⃣ 注册"
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

    assert (
        data["displayName"]
        == account.display_name
    )

    assert (
        data["familyName"]
        == account.family_name
    )

    assert data.get(
        "id"
    )

    print(
        "✅ Wolverine Register 正常"
    )


def test_03_session_login(
    client: httpx.Client,
    account: TestAccount,
) -> None:
    print_title(
        "3️⃣ Cookie Session 登录"
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

    print_cookies(
        client
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
        "Identity"
        in cookie_name
        for cookie_name
        in cookie_names
    ), (
        "❌ 登录返回 200，"
        "但 Cookie Jar 中没有 "
        "Identity Cookie"
    )

    print(
        "✅ Wolverine SessionLogin 正常"
    )

    print(
        "✅ Identity Cookie 已建立"
    )


def test_04_me_after_login(
    client: httpx.Client,
    account: TestAccount,
) -> None:
    print_title(
        "4️⃣ 登录后访问 /me"
    )

    print_cookies(
        client
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

    assert (
        data["displayName"]
        == account.display_name
    )

    assert (
        data["familyName"]
        == account.family_name
    )

    assert data.get(
        "id"
    )

    assert (
        data.get(
            "emailConfirmed"
        )
        is False
    )

    print(
        "✅ Wolverine GetMe 正常"
    )

    print(
        "✅ Cookie Authentication 正常"
    )

    print(
        "✅ 当前用户数据正确"
    )