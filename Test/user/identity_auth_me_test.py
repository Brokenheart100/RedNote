from __future__ import annotations

import json
import sys
from typing import Any

import requests
from requests import Response, Session


BASE_URL = "http://localhost:7161"

EMAIL = "test@test.com"
PASSWORD = "W945x089a27b038c"

TIMEOUT_SECONDS = 15


def print_separator() -> None:
    print("=" * 80)


def print_response(response: Response) -> None:
    print(f"📡 HTTP {response.status_code} {response.request.method} {response.url}")

    content_type = response.headers.get("Content-Type", "")

    if "application/json" in content_type.lower():
        try:
            data = response.json()

            print(
                json.dumps(
                    data,
                    ensure_ascii=False,
                    indent=2,
                )
            )

            return
        except ValueError:
            pass

    text = response.text.strip()

    if text:
        print(text[:4000])


def get_json(response: Response) -> dict[str, Any]:
    try:
        value = response.json()
    except ValueError as exc:
        raise RuntimeError(
            f"响应不是有效 JSON。HTTP {response.status_code}: "
            f"{response.text[:1000]}"
        ) from exc

    if not isinstance(value, dict):
        raise RuntimeError(
            f"预期 JSON object，实际得到 {type(value).__name__}。"
        )

    return value


def show_cookie_summary(session: Session) -> None:
    cookie_names = [cookie.name for cookie in session.cookies]

    print(
        "🍪 当前 Session Cookie:",
        cookie_names if cookie_names else "(none)",
    )


def get_csrf_token(session: Session) -> tuple[str, str]:
    url = f"{BASE_URL}/api/v1/auth/csrf"

    print_separator()
    print("🛡️ 1. 获取 CSRF Token")
    print(f"➡️ GET {url}")

    response = session.get(
        url,
        timeout=TIMEOUT_SECONDS,
    )

    print_response(response)

    if not response.ok:
        raise RuntimeError(
            f"获取 CSRF Token 失败，HTTP {response.status_code}。"
        )

    data = get_json(response)

    token = data.get("token")
    header_name = data.get("headerName")

    if not isinstance(token, str) or not token:
        raise RuntimeError(
            "CSRF 响应缺少有效的 token。"
        )

    if not isinstance(header_name, str) or not header_name:
        raise RuntimeError(
            "CSRF 响应缺少有效的 headerName。"
        )

    print(f"✅ CSRF Token 获取成功")
    print(f"🏷️ Header: {header_name}")
    print("🔐 Token: ***")

    show_cookie_summary(session)

    return header_name, token


def login(
    session: Session,
    csrf_header_name: str,
    csrf_token: str,
) -> None:
    url = f"{BASE_URL}/api/v1/auth/session/login"

    print_separator()
    print("🔑 2. Identity Cookie 登录")
    print(f"➡️ POST {url}")
    print(f"📧 Email: {EMAIL}")
    print("🔒 Password: ***")

    response = session.post(
        url,
        json={
            "email": EMAIL,
            "password": PASSWORD,
        },
        headers={
            csrf_header_name: csrf_token,
        },
        timeout=TIMEOUT_SECONDS,
    )

    print_response(response)

    if not response.ok:
        raise RuntimeError(
            f"登录失败，HTTP {response.status_code}。"
        )

    print("✅ Identity Cookie 登录成功")

    show_cookie_summary(session)


def get_current_identity_user(
    session: Session,
) -> dict[str, Any]:
    url = f"{BASE_URL}/api/v1/auth/me"

    print_separator()
    print("👤 3. 获取 IdentityService 当前用户")
    print(f"➡️ GET {url}")

    response = session.get(
        url,
        timeout=TIMEOUT_SECONDS,
    )

    print_response(response)

    if not response.ok:
        raise RuntimeError(
            f"获取当前用户失败，HTTP {response.status_code}。"
        )

    return get_json(response)


def print_result(user: dict[str, Any]) -> None:
    user_id = user.get("id")
    email = user.get("email")
    display_name = user.get("displayName")
    family_name = user.get("familyName")
    created_at = user.get("createdAtUtc")

    print_separator()
    print("📋 IdentityService 当前登录用户")
    print(f"🆔 Id:          {user_id}")
    print(f"📧 Email:       {email}")
    print(f"👤 DisplayName: {display_name}")
    print(f"👤 FamilyName:  {family_name}")
    print(f"🕐 CreatedAt:   {created_at}")

    print()

    if isinstance(email, str) and email.lower() == EMAIL.lower():
        print("✅ Identity Cookie 对应的是本次测试账号。")
    else:
        print("❌ Identity Cookie 对应的邮箱与测试账号不一致！")
        print(f"   预期: {EMAIL}")
        print(f"   实际: {email}")

    expected_problem_user_id = (
        "b77c8391-ba82-45b4-958d-eb2edeee51f5"
    )

    if (
        isinstance(user_id, str)
        and user_id.lower()
        == expected_problem_user_id.lower()
    ):
        print()
        print("⚠️ 当前 Identity 用户的 ID 正是此前 UserService 中的旧 Profile ID：")
        print(f"   {expected_problem_user_id}")
        print()
        print("这说明不是 UserService 查错用户。")
        print("IdentityService 当前账号本身就在使用这个 UserId。")
    else:
        print()
        print("ℹ️ 当前 Identity 用户 ID 与此前旧 Profile ID 不同。")
        print("如果 JWT sub 仍是旧 ID，就需要继续检查 OpenIddict ClaimsPrincipal。")


def main() -> int:
    print()
    print("🚀 RedNote IdentityService 登录诊断")
    print(f"🌐 Gateway: {BASE_URL}")
    print(f"📧 Account: {EMAIL}")
    print()

    if PASSWORD == "这里填写这个测试账号的密码":
        print("❌ 请先修改脚本顶部的 PASSWORD。")
        return 1

    session = requests.Session()

    session.headers.update(
        {
            "Accept": "application/json",
            "User-Agent": "RedNote-Identity-Diagnostic/1.0",
        }
    )

    try:
        csrf_header_name, csrf_token = get_csrf_token(
            session
        )

        login(
            session,
            csrf_header_name,
            csrf_token,
        )

        user = get_current_identity_user(
            session
        )

        print_result(user)

        print_separator()
        print("✅ 测试完成")

        return 0

    except requests.RequestException as exc:
        print_separator()
        print("❌ HTTP 请求失败")
        print(f"{type(exc).__name__}: {exc}")

        return 2

    except RuntimeError as exc:
        print_separator()
        print("❌ 测试失败")
        print(str(exc))

        return 3


if __name__ == "__main__":
    sys.exit(main())