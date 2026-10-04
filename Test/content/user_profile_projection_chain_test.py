from __future__ import annotations

import base64
import hashlib
import json
import secrets
import sys
import time
import uuid
from collections.abc import Generator
from dataclasses import dataclass
from datetime import datetime
from typing import Final
from urllib.parse import parse_qs, urlencode, urlparse

import httpx
import psycopg
import pytest


# =============================================================================
# 服务地址
# =============================================================================

IDENTITY_SERVICE_BASE_URL: Final[str] = "https://localhost:7025"
GATEWAY_BASE_URL: Final[str] = "https://localhost:7161"


# =============================================================================
# 现有测试账号
# =============================================================================

TEST_EMAIL: Final[str] = "test@test.com"
TEST_PASSWORD: Final[str] = "W945x089a27b038c"


# =============================================================================
# OpenID Connect
# =============================================================================

OIDC_CLIENT_ID: Final[str] = "rednote-web"

# 必须和 IdentityService 当前注册的 RedirectUri 完全一致。
OIDC_REDIRECT_URI: Final[str] = "http://localhost:3000/auth/rednote"

# 本测试只需要获取访问 API 的 Access Token。
OIDC_SCOPE: Final[str] = "openid profile email rednote-api"


# =============================================================================
# ContentService PostgreSQL
# =============================================================================

# ⚠️ 修改为 Aspire 当前 contentdb 的真实连接字符串。
#
# 示例：
# postgresql://postgres:password@localhost:6543/contentdb
CONTENT_DB_CONNECTION_STRING: Final[str] = (
    "postgresql://postgres:cS85CY0A5+}0umePd8PgpY@localhost:6543/contentdb"
)


# =============================================================================
# 测试配置
# =============================================================================

HTTP_TIMEOUT_SECONDS: Final[float] = 20.0

# UserProfileChanged 是异步事件链路，需要等待最终一致。
PROJECTION_TIMEOUT_SECONDS: Final[float] = 20.0
PROJECTION_POLL_INTERVAL_SECONDS: Final[float] = 0.5


# =============================================================================
# 数据类型
# =============================================================================


@dataclass(frozen=True, slots=True)
class AuthContext:
    access_token: str
    user_id: str


@dataclass(frozen=True, slots=True)
class Projection:
    user_id: str
    nickname: str | None
    avatar_url: str | None
    updated_at_utc: datetime


# =============================================================================
# Console
# =============================================================================


def configure_console() -> None:
    for stream in (sys.stdout, sys.stderr):
        reconfigure = getattr(stream, "reconfigure", None)

        if callable(reconfigure):
            reconfigure(
                encoding="utf-8",
                errors="replace",
            )


def section(title: str) -> None:
    print()
    print("=" * 100)
    print(f"🧪 {title}")
    print("=" * 100)


def info(message: str) -> None:
    print(f"ℹ️  {message}")


def success(message: str) -> None:
    print(f"✅ {message}")


def warning(message: str) -> None:
    print(f"⚠️  {message}")


# =============================================================================
# HTTP helpers
# =============================================================================


def print_response(
    response: httpx.Response,
    *,
    include_body: bool = False,
) -> None:
    print(
        f"🌐 {response.request.method} "
        f"{response.request.url} "
        f"→ HTTP {response.status_code}"
    )

    if not include_body or not response.content:
        return

    try:
        value = response.json()

        print(
            json.dumps(
                value,
                ensure_ascii=False,
                indent=2,
            )
        )

    except ValueError:
        print(response.text[:4000])


def require_status(
    response: httpx.Response,
    *expected: int,
) -> None:
    print_response(response)

    if response.status_code in expected:
        return

    print()
    print("❌ HTTP 请求失败")

    print_response(
        response,
        include_body=True,
    )

    pytest.fail(
        f"Unexpected HTTP status {response.status_code}; "
        f"expected {expected}."
    )


def require_json_object(
    response: httpx.Response,
) -> dict[str, object]:
    value = response.json()

    if not isinstance(value, dict):
        pytest.fail(
            "Expected HTTP response to contain a JSON object."
        )

    return value


def require_string(
    value: object | None,
    field_name: str,
) -> str:
    if not isinstance(value, str):
        pytest.fail(
            f"Expected '{field_name}' to be a string."
        )

    if not value:
        pytest.fail(
            f"Expected '{field_name}' to be non-empty."
        )

    return value


# =============================================================================
# IdentityService Cookie Login
# =============================================================================


def login(
    client: httpx.Client,
) -> None:
    section(
        "1. IdentityService Cookie 登录"
    )

    info(
        f"登录账号：{TEST_EMAIL}"
    )

    response = client.post(
        f"{IDENTITY_SERVICE_BASE_URL}/api/v1/auth/session/login",
        headers={
            "Accept": "application/json",
        },
        json={
            "email": TEST_EMAIL,
            "password": TEST_PASSWORD,
        },
    )

    require_status(
        response,
        200,
    )

    success(
        "IdentityService Cookie 登录成功"
    )

    info(
        f"当前 Cookie 数量：{len(client.cookies)}"
    )

    if len(client.cookies) == 0:
        warning(
            "登录成功但 httpx Client 中没有 Cookie，"
            "后续 /connect/authorize 可能会重新跳转登录页。"
        )


# =============================================================================
# PKCE
# =============================================================================


def create_pkce() -> tuple[str, str]:
    verifier = secrets.token_urlsafe(64)

    digest = hashlib.sha256(
        verifier.encode("ascii")
    ).digest()

    challenge = (
        base64.urlsafe_b64encode(digest)
        .decode("ascii")
        .rstrip("=")
    )

    return verifier, challenge


# =============================================================================
# OpenID Connect Authorization
# =============================================================================


def authorize(
    client: httpx.Client,
) -> tuple[str, str]:
    section(
        "2. OIDC Authorization Code + PKCE"
    )

    verifier, challenge = create_pkce()
    state = secrets.token_urlsafe(24)

    query = urlencode(
        {
            "client_id": OIDC_CLIENT_ID,
            "redirect_uri": OIDC_REDIRECT_URI,
            "response_type": "code",
            "scope": OIDC_SCOPE,
            "code_challenge": challenge,
            "code_challenge_method": "S256",
            "state": state,
        }
    )

    authorization_url = (
        f"{IDENTITY_SERVICE_BASE_URL}"
        f"/connect/authorize?{query}"
    )

    info(
        "请求 /connect/authorize ..."
    )

    response = client.get(
        authorization_url,
        follow_redirects=False,
    )

    require_status(
        response,
        302,
        303,
    )

    location = response.headers.get(
        "location"
    )

    if not location:
        pytest.fail(
            "Authorization response does not contain Location header."
        )

    print(
        f"↪️  Redirect → {location}"
    )

    parsed = urlparse(location)

    # 如果这里被重定向到 Nuxt 登录页，
    # 说明 Identity Cookie 没有被 Authorization Endpoint 识别。
    if parsed.path == "/login":
        pytest.fail(
            "OIDC Authorization Endpoint redirected to login page. "
            "Identity Cookie was not recognized."
        )

    parameters = parse_qs(
        parsed.query
    )

    returned_state = parameters.get(
        "state",
        [None],
    )[0]

    if returned_state != state:
        pytest.fail(
            "OIDC state validation failed."
        )

    error = parameters.get(
        "error",
        [None],
    )[0]

    if error is not None:
        error_description = parameters.get(
            "error_description",
            [None],
        )[0]

        pytest.fail(
            "OIDC authorization failed | "
            f"Error={error} | "
            f"Description={error_description}"
        )

    code = parameters.get(
        "code",
        [None],
    )[0]

    if not isinstance(code, str) or not code:
        pytest.fail(
            "OIDC callback does not contain authorization code."
        )

    success(
        "Authorization Code 获取成功"
    )

    return code, verifier


# =============================================================================
# OpenID Connect Token Exchange
# =============================================================================


def exchange_token(
    client: httpx.Client,
    code: str,
    verifier: str,
) -> str:
    section(
        "3. Authorization Code → Access Token"
    )

    response = client.post(
        f"{IDENTITY_SERVICE_BASE_URL}/connect/token",
        headers={
            "Accept": "application/json",
        },
        data={
            "grant_type": "authorization_code",
            "client_id": OIDC_CLIENT_ID,
            "redirect_uri": OIDC_REDIRECT_URI,
            "code": code,
            "code_verifier": verifier,
        },
    )

    require_status(
        response,
        200,
    )

    body = require_json_object(
        response
    )

    access_token = require_string(
        body.get("access_token"),
        "access_token",
    )

    success(
        "Access Token 获取成功"
    )

    info(
        f"TokenType={body.get('token_type')} | "
        f"ExpiresIn={body.get('expires_in')}"
    )

    return access_token


# =============================================================================
# UserService
# =============================================================================


def bootstrap_profile(
    client: httpx.Client,
) -> str:
    section(
        "4. GET /api/v1/users/me"
    )

    response = client.get(
        f"{GATEWAY_BASE_URL}/api/v1/users/me"
    )

    require_status(
        response,
        200,
    )

    body = require_json_object(
        response
    )

    user_id = require_string(
        body.get("userId"),
        "userId",
    )

    success(
        "UserService /users/me 请求成功"
    )

    info(
        f"UserId={user_id}"
    )

    info(
        f"Nickname={body.get('nickname')!r}"
    )

    info(
        f"AvatarUrl={body.get('avatarUrl')!r}"
    )

    return user_id


def update_profile(
    client: httpx.Client,
    *,
    nickname: str,
    avatar_url: str,
) -> None:
    print()
    print(
        "✏️  PATCH /api/v1/users/me"
    )

    print(
        f"   Nickname={nickname!r}"
    )

    print(
        f"   AvatarUrl={avatar_url!r}"
    )

    response = client.patch(
        f"{GATEWAY_BASE_URL}/api/v1/users/me",
        json={
            "nickname": nickname,
            "avatarUrl": avatar_url,
            "bio": "Python UserProfile event-chain integration test",
        },
    )

    require_status(
        response,
        200,
    )

    body = require_json_object(
        response
    )

    actual_nickname = body.get(
        "nickname"
    )

    actual_avatar_url = body.get(
        "avatarUrl"
    )

    assert (
        actual_nickname == nickname
    ), (
        "UserService returned unexpected nickname: "
        f"{actual_nickname!r} != {nickname!r}"
    )

    assert (
        actual_avatar_url == avatar_url
    ), (
        "UserService returned unexpected avatarUrl: "
        f"{actual_avatar_url!r} != {avatar_url!r}"
    )

    success(
        "UserService Profile 更新成功"
    )

    info(
        "现在等待："
        "UserProfileChanged → Wolverine Outbox → RabbitMQ "
        "→ ContentService → Projection"
    )


# =============================================================================
# PostgreSQL
# =============================================================================


def validate_database_configuration() -> None:
    if "YOUR_PASSWORD" in CONTENT_DB_CONNECTION_STRING:
        pytest.fail(
            "请先修改 CONTENT_DB_CONNECTION_STRING "
            "中的 PostgreSQL 密码。"
        )


def load_projection(
    user_id: str,
) -> Projection | None:
    with psycopg.connect(
        CONTENT_DB_CONNECTION_STRING
    ) as connection:
        with connection.cursor() as cursor:
            cursor.execute(
                """
                SELECT
                    "UserId",
                    "Nickname",
                    "AvatarUrl",
                    "UpdatedAtUtc"
                FROM "UserProfileProjections"
                WHERE "UserId" = %s
                LIMIT 1;
                """,
                (
                    user_id,
                ),
            )

            row = cursor.fetchone()

    if row is None:
        return None

    db_user_id = row[0]
    nickname = row[1]
    avatar_url = row[2]
    updated_at_utc = row[3]

    if not isinstance(
        db_user_id,
        uuid.UUID,
    ):
        pytest.fail(
            "UserProfileProjections.UserId "
            "was not returned as uuid.UUID."
        )

    if (
        nickname is not None
        and not isinstance(
            nickname,
            str,
        )
    ):
        pytest.fail(
            "UserProfileProjections.Nickname "
            "has unexpected database type."
        )

    if (
        avatar_url is not None
        and not isinstance(
            avatar_url,
            str,
        )
    ):
        pytest.fail(
            "UserProfileProjections.AvatarUrl "
            "has unexpected database type."
        )

    if not isinstance(
        updated_at_utc,
        datetime,
    ):
        pytest.fail(
            "UserProfileProjections.UpdatedAtUtc "
            "was not returned as datetime."
        )

    return Projection(
        user_id=str(
            db_user_id
        ),
        nickname=nickname,
        avatar_url=avatar_url,
        updated_at_utc=updated_at_utc,
    )


# =============================================================================
# Projection polling
# =============================================================================


def wait_for_projection(
    *,
    user_id: str,
    expected_nickname: str,
    expected_avatar_url: str,
) -> Projection:
    deadline = (
        time.monotonic()
        + PROJECTION_TIMEOUT_SECONDS
    )

    attempt = 0
    last_projection: Projection | None = None

    while time.monotonic() < deadline:
        attempt += 1

        projection = load_projection(
            user_id
        )

        last_projection = projection

        if projection is None:
            print(
                f"⏳ [{attempt:02}] "
                "Projection 尚未创建..."
            )

        else:
            print(
                f"🔍 [{attempt:02}] "
                f"UserId={projection.user_id} | "
                f"Nickname={projection.nickname!r} | "
                f"AvatarUrl={projection.avatar_url!r} | "
                f"UpdatedAtUtc={projection.updated_at_utc.isoformat()}"
            )

            if (
                projection.nickname == expected_nickname
                and projection.avatar_url == expected_avatar_url
            ):
                success(
                    "ContentService Projection 已同步到最新值"
                )

                return projection

        time.sleep(
            PROJECTION_POLL_INTERVAL_SECONDS
        )

    print()
    print(
        "❌ Projection 等待超时"
    )

    if last_projection is None:
        print()
        print(
            "数据库中完全没有找到当前 UserId 的 Projection："
        )

        print(
            f"   UserId={user_id}"
        )

        print()
        print(
            "说明问题位于下面这条链路中的某一层："
        )

        print(
            "   ① UserService PublishAsync(UserProfileChanged)"
        )

        print(
            "   ② Wolverine Outbox"
        )

        print(
            "   ③ RabbitMQ user-events exchange"
        )

        print(
            "   ④ user-events → content-user-profile-events binding"
        )

        print(
            "   ⑤ content-user-profile-events consumer"
        )

        print(
            "   ⑥ Wolverine Handler discovery"
        )

        print(
            "   ⑦ UserProfileChangedHandler"
        )

        print(
            "   ⑧ EF Core UserProfileProjections"
        )

        pytest.fail(
            "UserProfileProjection was never created."
        )

    print()
    print(
        "Projection 已存在，但内容不是本次 PATCH 后的值："
    )

    print()
    print(
        "Expected:"
    )

    print(
        f"   Nickname={expected_nickname!r}"
    )

    print(
        f"   AvatarUrl={expected_avatar_url!r}"
    )

    print()
    print(
        "Actual:"
    )

    print(
        f"   Nickname={last_projection.nickname!r}"
    )

    print(
        f"   AvatarUrl={last_projection.avatar_url!r}"
    )

    print(
        f"   UpdatedAtUtc="
        f"{last_projection.updated_at_utc.isoformat()}"
    )

    pytest.fail(
        "UserProfileProjection exists, "
        "but did not update to the expected values."
    )


# =============================================================================
# Fixtures
# =============================================================================


@pytest.fixture(
    scope="session",
)
def auth_context() -> AuthContext:
    configure_console()

    section(
        "准备认证上下文"
    )

    info(
        f"使用已有账号：{TEST_EMAIL}"
    )

    # 本机 Aspire 开发环境通常使用 dotnet dev certificate。
    #
    # Python httpx/certifi 不一定信任该证书，
    # 所以本地集成测试使用 verify=False。
    with httpx.Client(
        timeout=HTTP_TIMEOUT_SECONDS,
        follow_redirects=False,
        verify=False,
    ) as identity_client:
        login(
            identity_client
        )

        code, verifier = authorize(
            identity_client
        )

        access_token = exchange_token(
            identity_client,
            code,
            verifier,
        )

    with httpx.Client(
        timeout=HTTP_TIMEOUT_SECONDS,
        verify=False,
        headers={
            "Authorization":
                f"Bearer {access_token}",
            "Accept":
                "application/json",
        },
    ) as gateway_client:
        user_id = bootstrap_profile(
            gateway_client
        )

    success(
        "认证上下文准备完成"
    )

    return AuthContext(
        access_token=access_token,
        user_id=user_id,
    )


@pytest.fixture
def gateway_client(
    auth_context: AuthContext,
) -> Generator[
    httpx.Client,
    None,
    None,
]:
    with httpx.Client(
        timeout=HTTP_TIMEOUT_SECONDS,
        verify=False,
        headers={
            "Authorization":
                f"Bearer {auth_context.access_token}",
            "Accept":
                "application/json",
        },
    ) as client:
        yield client


# =============================================================================
# E2E
# =============================================================================


def test_user_profile_projection_chain(
    gateway_client: httpx.Client,
    auth_context: AuthContext,
) -> None:
    """
    验证：

    UserService
        ↓
    UserProfileChanged
        ↓
    Wolverine Outbox
        ↓
    RabbitMQ user-events
        ↓
    content-user-profile-events
        ↓
    Wolverine Durable Inbox
        ↓
    UserProfileChangedHandler
        ↓
    UserProfileProjections

    同时覆盖：
    - Projection 初次同步
    - Projection 后续更新
    """

    validate_database_configuration()

    section(
        "5. UserProfileChanged 完整事件链路"
    )

    info(
        f"UserId={auth_context.user_id}"
    )

    print()
    print(
        "目标链路："
    )

    print(
        "UserService"
        " → Wolverine Outbox"
        " → RabbitMQ"
        " → ContentService"
        " → UserProfileChangedHandler"
        " → UserProfileProjections"
    )

    # =========================================================================
    # 第一次更新
    # =========================================================================

    section(
        "6. 第一次 PATCH → 验证 Projection 同步"
    )

    first_suffix = uuid.uuid4().hex[:8]

    first_nickname = (
        f"链路测试-{first_suffix}"
    )

    first_avatar_url = (
        "https://example.invalid/"
        f"avatars/{first_suffix}.webp"
    )

    update_profile(
        gateway_client,
        nickname=first_nickname,
        avatar_url=first_avatar_url,
    )

    first_projection = wait_for_projection(
        user_id=auth_context.user_id,
        expected_nickname=first_nickname,
        expected_avatar_url=first_avatar_url,
    )

    assert (
        first_projection.user_id
        == auth_context.user_id
    )

    assert (
        first_projection.nickname
        == first_nickname
    )

    assert (
        first_projection.avatar_url
        == first_avatar_url
    )

    success(
        "第一次 Projection 同步成功"
    )

    # =========================================================================
    # 第二次更新
    # =========================================================================

    section(
        "7. 第二次 PATCH → 验证 Projection UPDATE"
    )

    # 避免极端情况下 UpdatedAtUtc 精度过近。
    time.sleep(
        0.1
    )

    second_suffix = uuid.uuid4().hex[:8]

    second_nickname = (
        f"链路测试-更新-{second_suffix}"
    )

    second_avatar_url = (
        "https://example.invalid/"
        f"avatars/{second_suffix}-2.webp"
    )

    update_profile(
        gateway_client,
        nickname=second_nickname,
        avatar_url=second_avatar_url,
    )

    second_projection = wait_for_projection(
        user_id=auth_context.user_id,
        expected_nickname=second_nickname,
        expected_avatar_url=second_avatar_url,
    )

    assert (
        second_projection.user_id
        == auth_context.user_id
    )

    assert (
        second_projection.nickname
        == second_nickname
    )

    assert (
        second_projection.avatar_url
        == second_avatar_url
    )

    assert (
        second_projection.updated_at_utc
        >= first_projection.updated_at_utc
    )

    success(
        "Projection UPDATE 路径验证成功"
    )

    # =========================================================================
    # 最终结果
    # =========================================================================

    section(
        "🎉 最终结果"
    )

    print(
        "✅ IdentityService Cookie Login"
    )

    print(
        "✅ OpenIddict Authorization Code + PKCE"
    )

    print(
        "✅ OpenIddict Access Token"
    )

    print(
        "✅ Gateway"
    )

    print(
        "✅ UserService GET /users/me"
    )

    print(
        "✅ UserService PATCH /users/me"
    )

    print(
        "✅ UserProfileChanged"
    )

    print(
        "✅ Wolverine Outbox"
    )

    print(
        "✅ RabbitMQ user-events"
    )

    print(
        "✅ RabbitMQ Queue Binding"
    )

    print(
        "✅ ContentService Consumer"
    )

    print(
        "✅ Wolverine Handler Discovery"
    )

    print(
        "✅ UserProfileChangedHandler"
    )

    print(
        "✅ UserProfileProjections"
    )

    print()
    print(
        "最终 Projection："
    )

    print(
        f"👤 UserId={second_projection.user_id}"
    )

    print(
        f"🏷️  Nickname={second_projection.nickname}"
    )

    print(
        f"🖼️  AvatarUrl={second_projection.avatar_url}"
    )

    print(
        f"🕒 UpdatedAtUtc="
        f"{second_projection.updated_at_utc.isoformat()}"
    )

    print()
    print(
        "🎉 UserProfile Projection "
        "完整消息链路验证通过！"
    )


def require_list(
    value: object | None,
    field_name: str,
) -> list[object]:
    if not isinstance(value, list):
        pytest.fail(
            f"Expected '{field_name}' to be a list."
        )

    return value


def require_dict(
    value: object | None,
    field_name: str,
) -> dict[str, object]:
    if not isinstance(value, dict):
        pytest.fail(
            f"Expected '{field_name}' to be an object."
        )

    return value


def create_post(
    client: httpx.Client,
    *,
    title: str,
    content: str,
) -> dict[str, object]:
    print()
    print("📝 创建测试帖子...")

    response = client.post(
        f"{GATEWAY_BASE_URL}/api/v1/posts",
        json={
            "title": title,
            "content": content,
            "mediaIds": [],
            "tags": [
                "pytest",
                "projection-test",
            ],
        },
    )

    require_status(
        response,
        200,
        201,
    )

    body = require_json_object(
        response
    )

    post_id = require_string(
        body.get("id"),
        "id",
    )

    success(
        f"测试帖子创建成功 | PostId={post_id}"
    )

    return body


def create_comment(
    client: httpx.Client,
    *,
    post_id: str,
    content: str,
) -> dict[str, object]:
    print()
    print("💬 创建测试评论...")

    response = client.post(
        f"{GATEWAY_BASE_URL}"
        f"/api/v1/posts/{post_id}/comments",
        json={
            "content": content,
            "parentCommentId": None,
        },
    )

    require_status(
        response,
        200,
        201,
    )

    body = require_json_object(
        response
    )

    comment_id = require_string(
        body.get("id"),
        "id",
    )

    success(
        f"测试评论创建成功 | CommentId={comment_id}"
    )

    return body


def get_post(
    client: httpx.Client,
    post_id: str,
) -> dict[str, object]:
    response = client.get(
        f"{GATEWAY_BASE_URL}"
        f"/api/v1/posts/{post_id}"
    )

    require_status(
        response,
        200,
    )

    return require_json_object(
        response
    )


def get_feed(
    client: httpx.Client,
) -> dict[str, object]:
    response = client.get(
        f"{GATEWAY_BASE_URL}"
        "/api/v1/posts/feed",
        params={
            "page": 1,
            "pageSize": 100,
        },
    )

    require_status(
        response,
        200,
    )

    return require_json_object(
        response
    )


def get_comments(
    client: httpx.Client,
    post_id: str,
) -> dict[str, object]:
    response = client.get(
        f"{GATEWAY_BASE_URL}"
        f"/api/v1/posts/{post_id}/comments",
        params={
            "page": 1,
            "pageSize": 100,
        },
    )

    require_status(
        response,
        200,
    )

    return require_json_object(
        response
    )


def assert_author(
    value: object | None,
    *,
    expected_user_id: str,
    expected_nickname: str,
    expected_avatar_url: str,
    context: str,
) -> None:
    author = require_dict(
        value,
        f"{context}.author",
    )

    actual_user_id = author.get(
        "userId"
    )

    actual_nickname = author.get(
        "nickname"
    )

    actual_avatar_url = author.get(
        "avatarUrl"
    )

    print(
        f"👤 [{context}] "
        f"UserId={actual_user_id!r} | "
        f"Nickname={actual_nickname!r} | "
        f"AvatarUrl={actual_avatar_url!r}"
    )

    assert (
        actual_user_id
        == expected_user_id
    ), (
        f"{context}: unexpected author.userId | "
        f"{actual_user_id!r} != {expected_user_id!r}"
    )

    assert (
        actual_nickname
        == expected_nickname
    ), (
        f"{context}: unexpected author.nickname | "
        f"{actual_nickname!r} != {expected_nickname!r}"
    )

    assert (
        actual_avatar_url
        == expected_avatar_url
    ), (
        f"{context}: unexpected author.avatarUrl | "
        f"{actual_avatar_url!r} != {expected_avatar_url!r}"
    )


def find_item_by_id(
    items: list[object],
    expected_id: str,
) -> dict[str, object] | None:
    for value in items:
        if not isinstance(
            value,
            dict,
        ):
            continue

        if value.get("id") == expected_id:
            return value

    return None


def test_post_and_comment_author_projection_read_chain(
    gateway_client: httpx.Client,
    auth_context: AuthContext,
) -> None:
    """
    验证读取链：

    UserService Profile
        ↓
    UserProfileChanged
        ↓
    UserProfileProjections
        ↓
    PostResponseQueryService
        ↓
    GET /posts/{id}
    GET /posts/feed

    以及：

    UserProfileProjections
        ↓
    GetPostCommentsEndpoint
        ↓
    GET /posts/{id}/comments
    """

    validate_database_configuration()

    section(
        "📖 Post / Comment 作者 Projection 读取链路"
    )

    # =====================================================================
    # 1. 生成本次测试唯一资料
    # =====================================================================

    suffix = uuid.uuid4().hex[:8]

    expected_nickname = (
        f"作者读取测试-{suffix}"
    )

    expected_avatar_url = (
        "https://example.invalid/"
        f"avatars/read-{suffix}.webp"
    )

    info(
        f"UserId={auth_context.user_id}"
    )

    info(
        f"ExpectedNickname={expected_nickname!r}"
    )

    info(
        f"ExpectedAvatarUrl={expected_avatar_url!r}"
    )

    # =====================================================================
    # 2. 更新 UserService
    # =====================================================================

    section(
        "1. 更新用户资料"
    )

    update_profile(
        gateway_client,
        nickname=expected_nickname,
        avatar_url=expected_avatar_url,
    )

    # =====================================================================
    # 3. 等待 ContentService Projection
    # =====================================================================

    section(
        "2. 等待 UserProfileProjection"
    )

    projection = wait_for_projection(
        user_id=auth_context.user_id,
        expected_nickname=expected_nickname,
        expected_avatar_url=expected_avatar_url,
    )

    assert (
        projection.nickname
        == expected_nickname
    )

    assert (
        projection.avatar_url
        == expected_avatar_url
    )

    success(
        "用户 Projection 已同步"
    )

    # =====================================================================
    # 4. 创建帖子
    # =====================================================================

    section(
        "3. 创建测试帖子"
    )

    post_title = (
        f"Projection Read Test {suffix}"
    )

    post_content = (
        f"pytest author projection read test {suffix}"
    )

    created_post = create_post(
        gateway_client,
        title=post_title,
        content=post_content,
    )

    post_id = require_string(
        created_post.get("id"),
        "post.id",
    )

    # 创建接口如果本身返回 Author，也一并检查。
    if "author" in created_post:
        assert_author(
            created_post.get("author"),
            expected_user_id=
                auth_context.user_id,
            expected_nickname=
                expected_nickname,
            expected_avatar_url=
                expected_avatar_url,
            context=
                "CreatePost",
        )

    # =====================================================================
    # 5. GET 单帖
    # =====================================================================

    section(
        "4. GET /posts/{id}"
    )

    post = get_post(
        gateway_client,
        post_id,
    )

    assert (
        post.get("authorUserId")
        == auth_context.user_id
    )

    assert_author(
        post.get("author"),
        expected_user_id=
            auth_context.user_id,
        expected_nickname=
            expected_nickname,
        expected_avatar_url=
            expected_avatar_url,
        context=
            "GetPost",
    )

    success(
        "单帖作者 Projection 正确"
    )

    # =====================================================================
    # 6. Feed
    # =====================================================================

    section(
        "5. GET /posts/feed"
    )

    feed = get_feed(
        gateway_client
    )

    feed_items = require_list(
        feed.get("items"),
        "feed.items",
    )

    feed_post = find_item_by_id(
        feed_items,
        post_id,
    )

    if feed_post is None:
        pytest.fail(
            f"新创建的帖子没有出现在 Feed 中 | "
            f"PostId={post_id}"
        )

    assert (
        feed_post.get("authorUserId")
        == auth_context.user_id
    )

    assert_author(
        feed_post.get("author"),
        expected_user_id=
            auth_context.user_id,
        expected_nickname=
            expected_nickname,
        expected_avatar_url=
            expected_avatar_url,
        context=
            "Feed",
    )

    success(
        "Feed 作者 Projection 正确"
    )

    # =====================================================================
    # 7. 创建评论
    # =====================================================================

    section(
        "6. 创建评论"
    )

    comment_content = (
        f"pytest comment projection test {suffix}"
    )

    created_comment = create_comment(
        gateway_client,
        post_id=post_id,
        content=comment_content,
    )

    comment_id = require_string(
        created_comment.get("id"),
        "comment.id",
    )

    # 当前 PostCommentResponse 已定义 Author。
    #
    # 如果 CreateCommentEndpoint 已同步升级为这个 DTO，
    # 创建响应应该直接包含 author。
    if "author" in created_comment:
        assert_author(
            created_comment.get("author"),
            expected_user_id=
                auth_context.user_id,
            expected_nickname=
                expected_nickname,
            expected_avatar_url=
                expected_avatar_url,
            context=
                "CreateComment",
        )

    # =====================================================================
    # 8. GET comments
    # =====================================================================

    section(
        "7. GET /posts/{id}/comments"
    )

    comments_response = get_comments(
        gateway_client,
        post_id,
    )

    comment_items = require_list(
        comments_response.get("items"),
        "comments.items",
    )

    comment = find_item_by_id(
        comment_items,
        comment_id,
    )

    if comment is None:
        pytest.fail(
            f"新创建的评论没有出现在评论列表中 | "
            f"CommentId={comment_id}"
        )

    assert (
        comment.get("authorUserId")
        == auth_context.user_id
    )

    assert (
        comment.get("content")
        == comment_content
    )

    assert_author(
        comment.get("author"),
        expected_user_id=
            auth_context.user_id,
        expected_nickname=
            expected_nickname,
        expected_avatar_url=
            expected_avatar_url,
        context=
            "Comments",
    )

    success(
        "评论作者 Projection 正确"
    )

    # =====================================================================
    # 9. 最终结果
    # =====================================================================

    section(
        "🎉 作者读取链测试通过"
    )

    print(
        "✅ UserService Profile"
    )

    print(
        "✅ UserProfileChanged"
    )

    print(
        "✅ UserProfileProjections"
    )

    print(
        "✅ Create Post"
    )

    print(
        "✅ GET Post author"
    )

    print(
        "✅ Feed author"
    )

    print(
        "✅ Create Comment"
    )

    print(
        "✅ GET Comments author"
    )

    print()
    print(
        f"👤 UserId={auth_context.user_id}"
    )

    print(
        f"🏷️  Nickname={expected_nickname}"
    )

    print(
        f"🖼️  AvatarUrl={expected_avatar_url}"
    )

    print(
        f"📝 PostId={post_id}"
    )

    print(
        f"💬 CommentId={comment_id}"
    )

    print()
    print(
        "🎉 ContentService 作者 Projection "
        "完整读取链路验证通过！"
    )