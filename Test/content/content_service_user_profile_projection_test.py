from __future__ import annotations

import base64
import hashlib
import secrets
import time
from collections.abc import Iterator
from typing import Any
from urllib.parse import parse_qs, urlparse
from uuid import UUID, uuid4

import httpx
import pytest


# ============================================================
# RedNote E2E 配置
# ============================================================

# IdentityService 的公开地址。
IDENTITY_SERVICE_BASE_URL = "http://localhost:5000"

# Gateway 地址。
GATEWAY_BASE_URL = "https://localhost:7161"

# OpenIddictSeeder 中固定配置。
OIDC_CLIENT_ID = "rednote-web"

# OpenIddictSeeder 本身没有写死 RedirectUri，
# 它来自：
#
# OpenIddict:Clients:RedNoteWeb:RedirectUri
#
# 当前 RedNote 实际运行日志使用：
# http://localhost:3000/auth/rednote
OIDC_REDIRECT_URI = "http://localhost:3000/auth/rednote"

OIDC_SCOPE = "profile email rednote-api"

REQUEST_TIMEOUT_SECONDS = 15.0

PROJECTION_TIMEOUT_SECONDS = 30.0

POLL_INTERVAL_SECONDS = 0.5


# ============================================================
# 测试上下文
# ============================================================


class TestContext:
    username: str = ""
    email: str = ""
    password: str = ""

    user_id: str = ""

    access_token: str = ""

    post_id: str = ""
    comment_id: str = ""

    initial_nickname: str = ""
    updated_nickname: str = ""

    avatar_url: str = ""


context = TestContext()


# ============================================================
# pytest Client
# ============================================================


@pytest.fixture(scope="session")
def identity_client() -> Iterator[httpx.Client]:
    print()
    print("=" * 80)
    print("🔐 RedNote IdentityService E2E Client")
    print(f"🌐 IdentityService : {IDENTITY_SERVICE_BASE_URL}")
    print("=" * 80)

    with httpx.Client(
        base_url=IDENTITY_SERVICE_BASE_URL,
        timeout=REQUEST_TIMEOUT_SECONDS,
        follow_redirects=False,
    ) as client:
        yield client


@pytest.fixture(scope="session")
def gateway_client() -> Iterator[httpx.Client]:
    print()
    print("=" * 80)
    print("🌐 RedNote Gateway E2E Client")
    print(f"🚪 Gateway : {GATEWAY_BASE_URL}")
    print("=" * 80)

    with httpx.Client(
        base_url=GATEWAY_BASE_URL,
        timeout=REQUEST_TIMEOUT_SECONDS,
        follow_redirects=False,

        # Aspire 本地开发 HTTPS 证书。
        verify=False,
    ) as client:
        yield client


# ============================================================
# Debug Helper
# ============================================================


def print_response(
    method: str,
    path: str,
    response: httpx.Response,
) -> None:
    print()
    print("-" * 80)
    print(f"🌐 {method} {path}")
    print(f"📡 HTTP {response.status_code}")

    location = response.headers.get("location")

    if location:
        print(f"↪️ Location: {location}")

    if response.content:
        print("📦 Response:")

        text = response.text

        if len(text) > 5000:
            text = (
                text[:5000]
                + "\n... <response truncated>"
            )

        print(text)

    print("-" * 80)


def assert_status(
    response: httpx.Response,
    expected: int | tuple[int, ...],
) -> None:
    expected_statuses = (
        (expected,)
        if isinstance(expected, int)
        else expected
    )

    if response.status_code not in expected_statuses:
        print_response(
            response.request.method,
            str(response.request.url),
            response,
        )

    assert response.status_code in expected_statuses, (
        "❌ HTTP 状态码不正确。\n"
        f"Expected = {expected_statuses}\n"
        f"Actual   = {response.status_code}\n"
        f"Response = {response.text}"
    )


# ============================================================
# PKCE
# ============================================================


def create_pkce_verifier() -> str:
    """
    RFC 7636：
    code_verifier 长度需要在 43~128 字符之间。
    """
    return secrets.token_urlsafe(
        64,
    )


def create_pkce_challenge(
    verifier: str,
) -> str:
    digest = hashlib.sha256(
        verifier.encode("ascii"),
    ).digest()

    return (
        base64
        .urlsafe_b64encode(digest)
        .decode("ascii")
        .rstrip("=")
    )


# ============================================================
# CSRF
# ============================================================


def get_csrf(
    client: httpx.Client,
) -> tuple[str, str]:
    path = "/api/v1/auth/csrf"

    response = client.get(
        path,
    )

    print_response(
        "GET",
        path,
        response,
    )

    assert_status(
        response,
        200,
    )

    body: Any = response.json()

    assert isinstance(
        body,
        dict,
    )

    token = body.get(
        "token",
    )

    header_name = body.get(
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
        f"🛡️ CSRF Header = {header_name}"
    )

    return (
        token,
        header_name,
    )


# ============================================================
# 自动注册
# ============================================================


def register_test_user(
    client: httpx.Client,
) -> None:
    suffix = uuid4().hex[:10]

    context.username = (
        f"pytest_{suffix}"
    )

    context.email = (
        f"pytest_{suffix}"
        "@example.test"
    )

    context.password = (
        "RedNoteTest12345"
    )

    csrf_token, csrf_header = (
        get_csrf(client)
    )

    path = "/api/v1/auth/register"

    body = {
        "username":
            context.username,

        "email":
            context.email,

        "password":
            context.password,

        "confirmPassword":
            context.password,
    }

    print()
    print("👤 自动注册测试用户")
    print(
        f"👤 Username = "
        f"{context.username}"
    )
    print(
        f"📧 Email    = "
        f"{context.email}"
    )

    response = client.post(
        path,
        headers={
            csrf_header:
                csrf_token,
        },
        json=body,
    )

    print_response(
        "POST",
        path,
        response,
    )

    # 当前 AccountEndpoints 返回 201。
    # 兼容之前版本 RegisterEndpoint 返回 200。
    assert_status(
        response,
        (
            200,
            201,
        ),
    )

    result: Any = response.json()

    assert isinstance(
        result,
        dict,
    )

    user_id = (
        result.get("id")
        or result.get("userId")
    )

    assert isinstance(
        user_id,
        str,
    ), (
        "❌ 注册响应中没有用户 ID。"
    )

    UUID(
        user_id,
    )

    context.user_id = user_id

    print()
    print("✅ 用户注册成功")
    print(
        f"🆔 UserId = "
        f"{context.user_id}"
    )


# ============================================================
# Cookie 登录
# ============================================================


def login_test_user(
    client: httpx.Client,
) -> None:
    csrf_token, csrf_header = (
        get_csrf(client)
    )

    path = "/api/v1/auth/login"

    response = client.post(
        path,
        headers={
            csrf_header:
                csrf_token,
        },
        json={
            "identifier":
                context.email,

            "password":
                context.password,

            "rememberMe":
                False,

            "returnUrl":
                None,
        },
    )

    print_response(
        "POST",
        path,
        response,
    )

    assert_status(
        response,
        200,
    )

    body: Any = response.json()

    assert isinstance(
        body,
        dict,
    )

    print()
    print(
        "✅ ASP.NET Core Identity "
        "Cookie 登录成功"
    )

    print(
        "🍪 当前 Cookie:"
    )

    for cookie in client.cookies.jar:
        print(
            f"   {cookie.name}"
        )


# ============================================================
# Authorization Code + PKCE
# ============================================================


def acquire_access_token(
    client: httpx.Client,
) -> str:
    verifier = (
        create_pkce_verifier()
    )

    challenge = (
        create_pkce_challenge(
            verifier,
        )
    )

    state = (
        secrets.token_urlsafe(
            32,
        )
    )

    authorize_path = (
        "/connect/authorize"
    )

    params = {
        "client_id":
            OIDC_CLIENT_ID,

        "response_type":
            "code",

        "redirect_uri":
            OIDC_REDIRECT_URI,

        "scope":
            OIDC_SCOPE,

        "code_challenge":
            challenge,

        "code_challenge_method":
            "S256",

        "state":
            state,
    }

    print()
    print(
        "🔐 请求 Authorization Code"
    )

    print(
        f"🧩 ClientId    = "
        f"{OIDC_CLIENT_ID}"
    )

    print(
        f"↩️ RedirectUri = "
        f"{OIDC_REDIRECT_URI}"
    )

    response = client.get(
        authorize_path,
        params=params,
    )

    print_response(
        "GET",
        authorize_path,
        response,
    )

    assert response.status_code in (
        302,
        303,
    ), (
        "❌ /connect/authorize "
        "没有返回授权回调重定向。\n"
        "\n"
        "如果这里跳转到了 Nuxt 登录页，"
        "说明 Identity Cookie 没有建立成功。\n"
        "\n"
        f"HTTP = {response.status_code}\n"
        f"Location = "
        f"{response.headers.get('location')}"
    )

    location = (
        response.headers.get(
            "location",
        )
    )

    assert location, (
        "❌ Authorize 响应缺少 Location。"
    )

    parsed = urlparse(
        location,
    )

    query = parse_qs(
        parsed.query,
    )

    returned_state = (
        query.get(
            "state",
            [None],
        )[0]
    )

    assert (
        returned_state
        == state
    ), (
        "❌ OIDC state 不匹配。"
    )

    code = (
        query.get(
            "code",
            [None],
        )[0]
    )

    error = (
        query.get(
            "error",
            [None],
        )[0]
    )

    if error:
        error_description = (
            query.get(
                "error_description",
                [None],
            )[0]
        )

        pytest.fail(
            "❌ OpenIddict authorize 失败。\n"
            f"error = {error}\n"
            f"description = "
            f"{error_description}",
            pytrace=False,
        )

    assert isinstance(
        code,
        str,
    )

    assert code

    print()
    print(
        "✅ Authorization Code 已获取"
    )

    token_path = (
        "/connect/token"
    )

    response = client.post(
        token_path,
        data={
            "grant_type":
                "authorization_code",

            "client_id":
                OIDC_CLIENT_ID,

            "code":
                code,

            "redirect_uri":
                OIDC_REDIRECT_URI,

            "code_verifier":
                verifier,
        },
        headers={
            "Content-Type":
                "application/x-www-form-urlencoded",
        },
    )

    print_response(
        "POST",
        token_path,
        response,
    )

    assert_status(
        response,
        200,
    )

    token_response: Any = (
        response.json()
    )

    assert isinstance(
        token_response,
        dict,
    )

    access_token = (
        token_response.get(
            "access_token",
        )
    )

    assert isinstance(
        access_token,
        str,
    )

    assert access_token

    print()
    print(
        "✅ Access Token 获取成功"
    )

    print(
        f"🎫 token_type = "
        f"{token_response.get('token_type')}"
    )

    print(
        f"⏱️ expires_in = "
        f"{token_response.get('expires_in')}"
    )

    context.access_token = (
        access_token
    )

    return access_token


# ============================================================
# Gateway Auth
# ============================================================


def set_gateway_token(
    client: httpx.Client,
) -> None:
    client.headers[
        "Authorization"
    ] = (
        f"Bearer "
        f"{context.access_token}"
    )


# ============================================================
# UserService
# ============================================================


def get_user_profile(
    client: httpx.Client,
) -> dict[str, Any]:
    path = "/api/v1/users/me"

    response = client.get(
        path,
    )

    print_response(
        "GET",
        path,
        response,
    )

    assert_status(
        response,
        200,
    )

    body: Any = response.json()

    assert isinstance(
        body,
        dict,
    )

    return body


def update_user_profile(
    client: httpx.Client,
    *,
    nickname: str,
    avatar_url: str,
) -> dict[str, Any]:
    path = "/api/v1/users/me"

    response = client.patch(
        path,
        json={
            "nickname":
                nickname,

            "avatarUrl":
                avatar_url,

            "bio":
                "pytest automated "
                "projection test",
        },
    )

    print_response(
        "PATCH",
        path,
        response,
    )

    assert_status(
        response,
        200,
    )

    body: Any = response.json()

    assert isinstance(
        body,
        dict,
    )

    assert (
        body.get("nickname")
        == nickname
    )

    assert (
        body.get("avatarUrl")
        == avatar_url
    )

    return body


# ============================================================
# Post
# ============================================================


def create_post(
    client: httpx.Client,
) -> str:
    path = "/api/v1/posts"

    title = (
        "pytest projection "
        f"{uuid4().hex[:8]}"
    )

    response = client.post(
        path,
        json={
            "title":
                title,

            "content":
                "这是 pytest 自动创建的 "
                "UserProfileProjection "
                "集成测试帖子。",

            "mediaIds":
                [],

            "tags":
                [
                    "pytest",
                    "projection",
                ],
        },
    )

    print_response(
        "POST",
        path,
        response,
    )

    assert_status(
        response,
        201,
    )

    body: Any = response.json()

    assert isinstance(
        body,
        dict,
    )

    # 兼容：
    #
    # 新版：
    # {
    #   "id": "..."
    # }
    #
    # 以及部分旧版：
    # {
    #   "postId": "..."
    # }
    post_id = (
        body.get("id")
        or body.get("postId")
    )

    if not isinstance(
        post_id,
        str,
    ):
        location = (
            response.headers.get(
                "location",
            )
        )

        if location:
            post_id = (
                location
                .rstrip("/")
                .split("/")[-1]
            )

    assert isinstance(
        post_id,
        str,
    ), (
        "❌ 创建帖子成功但无法取得 PostId。\n"
        f"Body = {body}\n"
        f"Location = "
        f"{response.headers.get('location')}"
    )

    UUID(
        post_id,
    )

    context.post_id = post_id

    print()
    print("✅ 自动发帖成功")
    print(
        f"📝 PostId = "
        f"{context.post_id}"
    )

    return post_id


def get_post(
    client: httpx.Client,
) -> dict[str, Any]:
    path = (
        "/api/v1/posts/"
        f"{context.post_id}"
    )

    response = client.get(
        path,
    )

    print_response(
        "GET",
        path,
        response,
    )

    assert_status(
        response,
        200,
    )

    body: Any = response.json()

    assert isinstance(
        body,
        dict,
    )

    return body


# ============================================================
# Comment
# ============================================================


def create_comment(
    client: httpx.Client,
) -> str:
    path = (
        "/api/v1/posts/"
        f"{context.post_id}"
        "/comments"
    )

    response = client.post(
        path,
        json={
            "content":
                "💬 pytest 自动评论 "
                f"{uuid4().hex[:8]}",

            "parentCommentId":
                None,
        },
    )

    print_response(
        "POST",
        path,
        response,
    )

    assert_status(
        response,
        201,
    )

    body: Any = response.json()

    assert isinstance(
        body,
        dict,
    )

    comment_id = (
        body.get("id")
    )

    assert isinstance(
        comment_id,
        str,
    )

    UUID(
        comment_id,
    )

    context.comment_id = (
        comment_id
    )

    print()
    print(
        "✅ 自动发表评论成功"
    )

    print(
        f"💬 CommentId = "
        f"{comment_id}"
    )

    return comment_id


def get_comments(
    client: httpx.Client,
) -> dict[str, Any]:
    path = (
        "/api/v1/posts/"
        f"{context.post_id}"
        "/comments"
    )

    response = client.get(
        path,
        params={
            "page":
                1,

            "pageSize":
                100,
        },
    )

    print_response(
        "GET",
        path,
        response,
    )

    assert_status(
        response,
        200,
    )

    body: Any = response.json()

    assert isinstance(
        body,
        dict,
    )

    return body


def find_created_comment(
    payload: dict[str, Any],
) -> dict[str, Any] | None:
    items = payload.get(
        "items",
    )

    if not isinstance(
        items,
        list,
    ):
        return None

    for comment in items:
        if not isinstance(
            comment,
            dict,
        ):
            continue

        if (
            str(
                comment.get("id")
            ).lower()
            == context.comment_id.lower()
        ):
            return comment

        replies = (
            comment.get(
                "replies",
            )
        )

        if not isinstance(
            replies,
            list,
        ):
            continue

        for reply in replies:
            if not isinstance(
                reply,
                dict,
            ):
                continue

            if (
                str(
                    reply.get("id")
                ).lower()
                == context.comment_id.lower()
            ):
                return reply

    return None


# ============================================================
# Projection Contract
# ============================================================


def assert_author(
    author: Any,
    *,
    expected_nickname: str,
    expected_avatar_url: str,
) -> None:
    assert isinstance(
        author,
        dict,
    ), (
        "❌ author 不存在。\n"
        f"Actual = {author}"
    )

    assert (
        str(
            author.get(
                "userId",
                "",
            )
        ).lower()
        == context.user_id.lower()
    ), (
        "❌ author.userId 不正确。\n"
        f"Expected = {context.user_id}\n"
        f"Actual   = "
        f"{author.get('userId')}"
    )

    assert (
        author.get(
            "nickname",
        )
        == expected_nickname
    ), (
        "❌ nickname 不正确。\n"
        f"Expected = "
        f"{expected_nickname!r}\n"
        f"Actual   = "
        f"{author.get('nickname')!r}"
    )

    assert (
        author.get(
            "avatarUrl",
        )
        == expected_avatar_url
    ), (
        "❌ avatarUrl 不正确。\n"
        f"Expected = "
        f"{expected_avatar_url!r}\n"
        f"Actual   = "
        f"{author.get('avatarUrl')!r}"
    )


# ============================================================
# Projection Polling
# ============================================================


def wait_for_post_projection(
    client: httpx.Client,
    *,
    nickname: str,
    avatar_url: str,
) -> dict[str, Any]:
    deadline = (
        time.monotonic()
        + PROJECTION_TIMEOUT_SECONDS
    )

    attempt = 0
    last_author: Any = None

    while (
        time.monotonic()
        < deadline
    ):
        attempt += 1

        post = get_post(
            client,
        )

        author = (
            post.get(
                "author",
            )
        )

        last_author = author

        print()
        print(
            "🔎 Post Projection "
            f"检查 #{attempt}"
        )

        print(
            f"👤 author = {author}"
        )

        if isinstance(
            author,
            dict,
        ):
            if (
                author.get(
                    "nickname",
                )
                == nickname
                and
                author.get(
                    "avatarUrl",
                )
                == avatar_url
            ):
                print()
                print(
                    "✅ Post Projection "
                    "同步成功"
                )

                return post

        time.sleep(
            POLL_INTERVAL_SECONDS,
        )

    pytest.fail(
        "\n".join(
            [
                "❌ Post Projection 同步超时。",
                "",
                f"Expected nickname = {nickname}",
                f"Expected avatar   = {avatar_url}",
                f"Last author       = {last_author}",
                "",
                "检查：",
                "1. UserService Outbox",
                "2. RabbitMQ user-events",
                "3. content-user-profile-events",
                "4. ContentService Durable Inbox",
                "5. UserProfileChangedHandler",
                "6. UserProfileProjections",
                "7. PostResponseQueryService",
            ]
        ),
        pytrace=False,
    )


def wait_for_comment_projection(
    client: httpx.Client,
    *,
    nickname: str,
    avatar_url: str,
) -> dict[str, Any]:
    deadline = (
        time.monotonic()
        + PROJECTION_TIMEOUT_SECONDS
    )

    attempt = 0
    last_comment: Any = None

    while (
        time.monotonic()
        < deadline
    ):
        attempt += 1

        payload = get_comments(
            client,
        )

        comment = (
            find_created_comment(
                payload,
            )
        )

        last_comment = comment

        print()
        print(
            "🔎 Comment Projection "
            f"检查 #{attempt}"
        )

        if comment is not None:
            author = (
                comment.get(
                    "author",
                )
            )

            print(
                f"👤 author = {author}"
            )

            if isinstance(
                author,
                dict,
            ):
                if (
                    author.get(
                        "nickname",
                    )
                    == nickname
                    and
                    author.get(
                        "avatarUrl",
                    )
                    == avatar_url
                ):
                    print()
                    print(
                        "✅ Comment Projection "
                        "同步成功"
                    )

                    return comment

        time.sleep(
            POLL_INTERVAL_SECONDS,
        )

    pytest.fail(
        "\n".join(
            [
                "❌ Comment Projection 同步超时。",
                "",
                f"Expected nickname = {nickname}",
                f"Expected avatar   = {avatar_url}",
                f"Last comment      = {last_comment}",
                "",
                "检查：",
                "1. UserProfileProjections",
                "2. GetPostCommentsEndpoint",
                "3. PostCommentResponse.Author",
                "4. CommentAuthorResponse",
            ]
        ),
        pytrace=False,
    )


# ============================================================
# Cleanup
# ============================================================


def delete_test_post(
    client: httpx.Client,
) -> None:
    if not context.post_id:
        return

    path = (
        "/api/v1/posts/"
        f"{context.post_id}"
    )

    try:
        response = client.delete(
            path,
        )

        print_response(
            "DELETE",
            path,
            response,
        )

        if response.status_code in (
            200,
            204,
            404,
        ):
            print(
                "🧹 测试帖子已清理"
            )
        else:
            print(
                "⚠️ 测试帖子清理失败，"
                f"HTTP {response.status_code}"
            )

    except httpx.HTTPError as error:
        print(
            "⚠️ 清理测试帖子时发生异常："
            f"{error}"
        )


# ============================================================
# 完整 E2E
# ============================================================


def test_user_profile_projection_full_e2e(
    identity_client: httpx.Client,
    gateway_client: httpx.Client,
) -> None:
    print()
    print("=" * 80)
    print(
        "🚀 RedNote UserProfileProjection "
        "FULL E2E"
    )
    print("=" * 80)

    # --------------------------------------------------------
    # 1. Register
    # --------------------------------------------------------

    print()
    print(
        "1️⃣ 自动注册"
    )

    register_test_user(
        identity_client,
    )

    # --------------------------------------------------------
    # 2. Cookie Login
    # --------------------------------------------------------

    print()
    print(
        "2️⃣ 自动登录"
    )

    login_test_user(
        identity_client,
    )

    # --------------------------------------------------------
    # 3. Authorization Code + PKCE
    # --------------------------------------------------------

    print()
    print(
        "3️⃣ 自动执行 PKCE 授权"
    )

    acquire_access_token(
        identity_client,
    )

    set_gateway_token(
        gateway_client,
    )

    # --------------------------------------------------------
    # 4. Bootstrap UserProfile
    # --------------------------------------------------------

    print()
    print(
        "4️⃣ 初始化 UserService Profile"
    )

    profile = get_user_profile(
        gateway_client,
    )

    print()
    print(
        f"👤 UserProfile = {profile}"
    )

    profile_user_id = (
        profile.get(
            "userId",
        )
    )

    assert isinstance(
        profile_user_id,
        str,
    )

    assert (
        profile_user_id.lower()
        == context.user_id.lower()
    )

    # --------------------------------------------------------
    # 5. 设置初始资料
    # --------------------------------------------------------

    context.initial_nickname = (
        "pytest-user-"
        f"{uuid4().hex[:8]}"
    )

    context.avatar_url = (
        "/api/media/"
        f"{uuid4()}"
    )

    print()
    print(
        "5️⃣ 设置初始昵称和头像"
    )

    update_user_profile(
        gateway_client,
        nickname=
            context.initial_nickname,
        avatar_url=
            context.avatar_url,
    )

    # --------------------------------------------------------
    # 6. Create post
    # --------------------------------------------------------

    print()
    print(
        "6️⃣ 自动发帖"
    )

    create_post(
        gateway_client,
    )

    # --------------------------------------------------------
    # 7. Create comment
    # --------------------------------------------------------

    print()
    print(
        "7️⃣ 自动发表评论"
    )

    create_comment(
        gateway_client,
    )

    try:
        # ----------------------------------------------------
        # 8. 初始 Projection
        # ----------------------------------------------------

        print()
        print(
            "8️⃣ 验证初始帖子 Projection"
        )

        post = (
            wait_for_post_projection(
                gateway_client,
                nickname=
                    context.initial_nickname,
                avatar_url=
                    context.avatar_url,
            )
        )

        assert_author(
            post.get(
                "author",
            ),
            expected_nickname=
                context.initial_nickname,
            expected_avatar_url=
                context.avatar_url,
        )

        print()
        print(
            "9️⃣ 验证初始评论 Projection"
        )

        comment = (
            wait_for_comment_projection(
                gateway_client,
                nickname=
                    context.initial_nickname,
                avatar_url=
                    context.avatar_url,
            )
        )

        assert_author(
            comment.get(
                "author",
            ),
            expected_nickname=
                context.initial_nickname,
            expected_avatar_url=
                context.avatar_url,
        )

        # ----------------------------------------------------
        # 10. Update profile again
        # ----------------------------------------------------

        context.updated_nickname = (
            "pytest-updated-"
            f"{uuid4().hex[:8]}"
        )

        print()
        print(
            "🔟 再次修改昵称，"
            "验证实时事件更新"
        )

        update_user_profile(
            gateway_client,
            nickname=
                context.updated_nickname,
            avatar_url=
                context.avatar_url,
        )

        # ----------------------------------------------------
        # 11. Updated Post Projection
        # ----------------------------------------------------

        print()
        print(
            "1️⃣1️⃣ 等待帖子作者更新"
        )

        updated_post = (
            wait_for_post_projection(
                gateway_client,
                nickname=
                    context.updated_nickname,
                avatar_url=
                    context.avatar_url,
            )
        )

        assert_author(
            updated_post.get(
                "author",
            ),
            expected_nickname=
                context.updated_nickname,
            expected_avatar_url=
                context.avatar_url,
        )

        # ----------------------------------------------------
        # 12. Updated Comment Projection
        # ----------------------------------------------------

        print()
        print(
            "1️⃣2️⃣ 等待评论作者更新"
        )

        updated_comment = (
            wait_for_comment_projection(
                gateway_client,
                nickname=
                    context.updated_nickname,
                avatar_url=
                    context.avatar_url,
            )
        )

        assert_author(
            updated_comment.get(
                "author",
            ),
            expected_nickname=
                context.updated_nickname,
            expected_avatar_url=
                context.avatar_url,
        )

        print()
        print("=" * 80)
        print(
            "🎉🎉🎉 FULL E2E 全部通过"
        )
        print("=" * 80)

        print()
        print(
            "✅ 自动注册"
        )
        print(
            "✅ 自动登录"
        )
        print(
            "✅ Authorization Code + PKCE"
        )
        print(
            "✅ Access Token"
        )
        print(
            "✅ UserProfile"
        )
        print(
            "✅ 自动发帖"
        )
        print(
            "✅ 自动评论"
        )
        print(
            "✅ Wolverine Outbox"
        )
        print(
            "✅ RabbitMQ"
        )
        print(
            "✅ Durable Inbox"
        )
        print(
            "✅ UserProfileProjection"
        )
        print(
            "✅ Post author"
        )
        print(
            "✅ Comment author"
        )

    finally:
        print()
        print(
            "🧹 清理测试数据..."
        )

        delete_test_post(
            gateway_client,
        )