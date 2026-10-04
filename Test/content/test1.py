from __future__ import annotations

import time
from collections.abc import Iterator
from typing import Any
from uuid import UUID, uuid4

import httpx
import pytest


GATEWAY_BASE_URL = "https://localhost:7161"

# 当前登录用户的 Access Token。
# 用于测试：
#   GET   /api/v1/users/me
#   PATCH /api/v1/users/me
ACCESS_TOKEN = ""

# 当前登录用户自己发布的一篇 Published 帖子。
# 用于验证 PostResponse.author 是否通过
# UserProfileProjection 自动更新。
KNOWN_POST_ID: str | None = None

REQUEST_TIMEOUT_SECONDS = 15.0

# UserService
# -> Wolverine Outbox
# -> RabbitMQ
# -> ContentService Durable Inbox
# -> UserProfileProjection
#
# 属于最终一致性链路，因此允许一定同步时间。
PROJECTION_TIMEOUT_SECONDS = 20.0

POLL_INTERVAL_SECONDS = 0.5


@pytest.fixture(scope="session")
def client() -> Iterator[httpx.Client]:
    print()
    print("=" * 72)
    print("👤 RedNote UserProfileProjection E2E pytest")
    print(f"🌐 Gateway       : {GATEWAY_BASE_URL}")
    print(
        f"🆔 Known Post ID : "
        f"{KNOWN_POST_ID or '<not configured>'}"
    )
    print(
        f"🔐 Access Token  : "
        f"{'<configured>' if ACCESS_TOKEN else '<not configured>'}"
    )
    print("=" * 72)

    headers: dict[str, str] = {
        "Accept": "application/json",
    }

    if ACCESS_TOKEN:
        headers["Authorization"] = (
            f"Bearer {ACCESS_TOKEN}"
        )

    with httpx.Client(
        base_url=GATEWAY_BASE_URL,
        headers=headers,
        timeout=REQUEST_TIMEOUT_SECONDS,
        follow_redirects=False,
        verify=False,
    ) as http_client:
        yield http_client


def print_response(
    method: str,
    path: str,
    response: httpx.Response,
) -> None:
    print()
    print("-" * 72)
    print(f"🌐 {method} {path}")
    print(f"📡 HTTP {response.status_code}")

    if response.content:
        print("📦 Response:")
        print(response.text[:5000])

    print("-" * 72)


def require_access_token() -> None:
    if not ACCESS_TOKEN:
        pytest.skip(
            "⏭️ ACCESS_TOKEN 未配置，"
            "跳过需要认证的 Projection E2E 测试。"
        )


def require_known_post_id() -> str:
    if KNOWN_POST_ID is None:
        pytest.skip(
            "⏭️ KNOWN_POST_ID 未配置，"
            "跳过依赖真实帖子的 Projection 测试。"
        )

    UUID(KNOWN_POST_ID)

    return KNOWN_POST_ID


def get_me(
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

    assert response.status_code == 200, (
        "❌ GET /users/me 失败，"
        f"HTTP {response.status_code}\n"
        f"{response.text}"
    )

    body: Any = response.json()

    assert isinstance(
        body,
        dict,
    )

    return body


def patch_me(
    client: httpx.Client,
    *,
    nickname: str | None,
    avatar_url: str | None,
    bio: str | None,
) -> dict[str, Any]:
    path = "/api/v1/users/me"

    response = client.patch(
        path,
        json={
            "nickname": nickname,
            "avatarUrl": avatar_url,
            "bio": bio,
        },
    )

    print_response(
        "PATCH",
        path,
        response,
    )

    assert response.status_code == 200, (
        "❌ PATCH /users/me 失败，"
        f"HTTP {response.status_code}\n"
        f"{response.text}"
    )

    body: Any = response.json()

    assert isinstance(
        body,
        dict,
    )

    return body


def get_post(
    client: httpx.Client,
    post_id: str,
) -> dict[str, Any]:
    path = (
        f"/api/v1/posts/"
        f"{post_id}"
    )

    response = client.get(
        path,
    )

    print_response(
        "GET",
        path,
        response,
    )

    assert response.status_code == 200, (
        "❌ GET post 失败，"
        f"HTTP {response.status_code}\n"
        f"{response.text}"
    )

    body: Any = response.json()

    assert isinstance(
        body,
        dict,
    )

    return body


def get_comments(
    client: httpx.Client,
    post_id: str,
) -> dict[str, Any]:
    path = (
        f"/api/v1/posts/"
        f"{post_id}/comments"
    )

    response = client.get(
        path,
        params={
            "page": 1,
            "pageSize": 100,
        },
    )

    print_response(
        "GET",
        path,
        response,
    )

    assert response.status_code == 200, (
        "❌ GET comments 失败，"
        f"HTTP {response.status_code}\n"
        f"{response.text}"
    )

    body: Any = response.json()

    assert isinstance(
        body,
        dict,
    )

    return body


def assert_author_response_contract(
    author: dict[str, Any],
) -> None:
    expected_fields = {
        "userId",
        "nickname",
        "avatarUrl",
    }

    actual_fields = set(
        author.keys(),
    )

    missing_fields = (
        expected_fields
        - actual_fields
    )

    assert not missing_fields, (
        "❌ AuthorResponse 缺少字段："
        f"{sorted(missing_fields)}"
    )

    assert isinstance(
        author["userId"],
        str,
    )

    UUID(
        author["userId"],
    )

    assert (
        author["nickname"] is None
        or isinstance(
            author["nickname"],
            str,
        )
    )

    assert (
        author["avatarUrl"] is None
        or isinstance(
            author["avatarUrl"],
            str,
        )
    )


def assert_post_author_contract(
    post: dict[str, Any],
) -> None:
    assert "authorUserId" in post, (
        "❌ PostResponse 缺少 authorUserId。"
    )

    assert "author" in post, (
        "❌ PostResponse 缺少 author。"
    )

    author_user_id = post[
        "authorUserId"
    ]

    author = post[
        "author"
    ]

    assert isinstance(
        author_user_id,
        str,
    )

    UUID(
        author_user_id,
    )

    assert isinstance(
        author,
        dict,
    )

    assert_author_response_contract(
        author,
    )

    assert (
        author["userId"].lower()
        == author_user_id.lower()
    ), (
        "❌ PostResponse.author.userId "
        "与 authorUserId 不一致。\n"
        f"authorUserId = {author_user_id}\n"
        f"author.userId = {author['userId']}"
    )


def assert_comment_author_contract(
    comment: dict[str, Any],
) -> None:
    assert "authorUserId" in comment, (
        "❌ CommentResponse 缺少 authorUserId。"
    )

    assert "author" in comment, (
        "❌ CommentResponse 缺少 author。"
    )

    author_user_id = comment[
        "authorUserId"
    ]

    author = comment[
        "author"
    ]

    assert isinstance(
        author_user_id,
        str,
    )

    UUID(
        author_user_id,
    )

    assert isinstance(
        author,
        dict,
    )

    assert_author_response_contract(
        author,
    )

    assert (
        author["userId"].lower()
        == author_user_id.lower()
    ), (
        "❌ CommentResponse.author.userId "
        "与 authorUserId 不一致。\n"
        f"authorUserId = {author_user_id}\n"
        f"author.userId = {author['userId']}"
    )


def find_comment_by_author(
    payload: dict[str, Any],
    user_id: str,
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

        comment_author_user_id = str(
            comment.get(
                "authorUserId",
                "",
            ),
        )

        if (
            comment_author_user_id.lower()
            == user_id.lower()
        ):
            return comment

        replies = comment.get(
            "replies",
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

            reply_author_user_id = str(
                reply.get(
                    "authorUserId",
                    "",
                ),
            )

            if (
                reply_author_user_id.lower()
                == user_id.lower()
            ):
                return reply

    return None


def wait_for_post_projection(
    client: httpx.Client,
    *,
    post_id: str,
    expected_nickname: str | None,
    expected_avatar_url: str | None,
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
            post_id,
        )

        author = post.get(
            "author",
        )

        last_author = author

        print()
        print(
            f"🔎 Projection 检查 #{attempt}"
        )

        print(
            f"👤 Post author = {author}"
        )

        if isinstance(
            author,
            dict,
        ):
            if (
                author.get(
                    "nickname",
                )
                == expected_nickname
                and
                author.get(
                    "avatarUrl",
                )
                == expected_avatar_url
            ):
                print()
                print(
                    "✅ PostResponse.author "
                    "已读取最新 Projection"
                )

                return post

        print(
            "⏳ Projection 尚未同步，"
            f"{POLL_INTERVAL_SECONDS}s 后重试..."
        )

        time.sleep(
            POLL_INTERVAL_SECONDS,
        )

    pytest.fail(
        "\n".join(
            [
                "❌ UserProfileProjection 同步超时。",
                "",
                (
                    "Timeout = "
                    f"{PROJECTION_TIMEOUT_SECONDS}s"
                ),
                (
                    "Last author = "
                    f"{last_author}"
                ),
                "",
                "请检查：",
                "1. UserService Outbox",
                "2. RabbitMQ user-events",
                "3. content-user-profile-events",
                "4. ContentService Durable Inbox",
                "5. UserProfileChangedHandler",
                "6. UserProfileProjections 表",
                (
                    "7. PostResponseQueryService "
                    "是否读取 Projection"
                ),
            ]
        ),
        pytrace=False,
    )


def wait_for_comment_projection(
    client: httpx.Client,
    *,
    post_id: str,
    user_id: str,
    expected_nickname: str | None,
    expected_avatar_url: str | None,
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

        comments = get_comments(
            client,
            post_id,
        )

        comment = find_comment_by_author(
            comments,
            user_id,
        )

        last_comment = comment

        print()
        print(
            f"🔎 Comment Projection 检查 #{attempt}"
        )

        if comment is not None:
            author = comment.get(
                "author",
            )

            print(
                f"👤 Comment author = {author}"
            )

            if isinstance(
                author,
                dict,
            ):
                if (
                    author.get(
                        "nickname",
                    )
                    == expected_nickname
                    and
                    author.get(
                        "avatarUrl",
                    )
                    == expected_avatar_url
                ):
                    print()
                    print(
                        "✅ CommentResponse.author "
                        "已读取最新 Projection"
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
                (
                    "Last comment = "
                    f"{last_comment}"
                ),
                "",
                "请检查：",
                (
                    "1. GetPostCommentsEndpoint "
                    "是否读取 UserProfileProjections"
                ),
                (
                    "2. PostCommentResponse "
                    "是否包含 author"
                ),
                (
                    "3. UserProfileProjections "
                    "是否包含当前用户"
                ),
            ]
        ),
        pytrace=False,
    )


def test_get_me_is_reachable(
    client: httpx.Client,
) -> None:
    require_access_token()

    user = get_me(
        client,
    )

    assert "userId" in user

    assert isinstance(
        user["userId"],
        str,
    )

    UUID(
        user["userId"],
    )

    print()
    print("✅ UserService /users/me 可正常访问")
    print(f"🆔 userId    = {user['userId']}")
    print(
        f"👤 nickname  = {user.get('nickname')!r}"
    )
    print(
        f"🖼️ avatarUrl = {user.get('avatarUrl')!r}"
    )


def test_known_post_contains_author(
    client: httpx.Client,
) -> None:
    require_access_token()

    post_id = require_known_post_id()

    user = get_me(
        client,
    )

    post = get_post(
        client,
        post_id,
    )

    assert_post_author_contract(
        post,
    )

    assert (
        post["authorUserId"].lower()
        == user["userId"].lower()
    ), (
        "❌ KNOWN_POST_ID 必须属于当前用户。\n"
        f"userId       = {user['userId']}\n"
        f"authorUserId = {post['authorUserId']}"
    )

    print()
    print("✅ PostResponse.author Contract 正确")
    print(
        f"👤 nickname  = "
        f"{post['author']['nickname']!r}"
    )
    print(
        f"🖼️ avatarUrl = "
        f"{post['author']['avatarUrl']!r}"
    )


def test_comments_contain_author(
    client: httpx.Client,
) -> None:
    post_id = require_known_post_id()

    payload = get_comments(
        client,
        post_id,
    )

    items = payload.get(
        "items",
    )

    assert isinstance(
        items,
        list,
    )

    if not items:
        pytest.skip(
            "⏭️ 当前帖子没有评论，"
            "跳过 CommentResponse.author 测试。"
        )

    top_level_count = 0
    reply_count = 0

    for comment in items:
        assert isinstance(
            comment,
            dict,
        )

        assert_comment_author_contract(
            comment,
        )

        top_level_count += 1

        replies = comment.get(
            "replies",
        )

        assert isinstance(
            replies,
            list,
        )

        for reply in replies:
            assert isinstance(
                reply,
                dict,
            )

            assert_comment_author_contract(
                reply,
            )

            reply_count += 1

    print()
    print("✅ CommentResponse.author Contract 正确")
    print(
        f"💬 顶层评论 = {top_level_count}"
    )
    print(
        f"↩️ 回复     = {reply_count}"
    )


def test_user_profile_projection_end_to_end(
    client: httpx.Client,
) -> None:
    require_access_token()

    post_id = require_known_post_id()

    print()
    print("=" * 72)
    print("🧪 UserProfileProjection 完整 E2E")
    print("=" * 72)

    original = get_me(
        client,
    )

    user_id = original[
        "userId"
    ]

    original_nickname = original.get(
        "nickname",
    )

    original_avatar_url = original.get(
        "avatarUrl",
    )

    original_bio = original.get(
        "bio",
    )

    post_before = get_post(
        client,
        post_id,
    )

    assert (
        post_before["authorUserId"].lower()
        == user_id.lower()
    ), (
        "❌ KNOWN_POST_ID 必须属于当前登录用户。"
    )

    temporary_nickname = (
        f"pytest-"
        f"{uuid4().hex[:8]}"
    )

    print()
    print(
        f"👤 UserId      = {user_id}"
    )
    print(
        f"🏷️ 原昵称      = "
        f"{original_nickname!r}"
    )
    print(
        f"🧪 临时昵称    = "
        f"{temporary_nickname!r}"
    )

    try:
        print()
        print(
            "✏️ Step 1：修改 UserService 用户资料"
        )

        updated = patch_me(
            client,
            nickname=temporary_nickname,
            avatar_url=original_avatar_url,
            bio=original_bio,
        )

        assert (
            updated["nickname"]
            == temporary_nickname
        )

        print()
        print(
            "✅ UserService 更新成功"
        )

        print()
        print(
            "📨 Step 2：等待 RabbitMQ Projection"
        )

        synced_post = wait_for_post_projection(
            client,
            post_id=post_id,
            expected_nickname=
                temporary_nickname,
            expected_avatar_url=
                original_avatar_url,
        )

        assert_post_author_contract(
            synced_post,
        )

        print()
        print(
            "✅ PostResponse.author "
            "已同步新昵称"
        )

        print()
        print(
            "💬 Step 3：验证评论作者 Projection"
        )

        comments = get_comments(
            client,
            post_id,
        )

        own_comment = (
            find_comment_by_author(
                comments,
                user_id,
            )
        )

        if own_comment is None:
            print()
            print(
                "⚠️ 当前用户在该帖子下"
                "没有评论或回复。"
            )

            print(
                "✅ Post Projection 已验证成功。"
            )

            return

        synced_comment = (
            wait_for_comment_projection(
                client,
                post_id=post_id,
                user_id=user_id,
                expected_nickname=
                    temporary_nickname,
                expected_avatar_url=
                    original_avatar_url,
            )
        )

        assert_comment_author_contract(
            synced_comment,
        )

        print()
        print(
            "🎉 Projection 完整链路测试成功"
        )

        print(
            "✅ UserService"
        )
        print(
            "   ↓"
        )
        print(
            "✅ Wolverine Outbox"
        )
        print(
            "   ↓"
        )
        print(
            "✅ RabbitMQ"
        )
        print(
            "   ↓"
        )
        print(
            "✅ ContentService Durable Inbox"
        )
        print(
            "   ↓"
        )
        print(
            "✅ UserProfileProjection"
        )
        print(
            "   ↓"
        )
        print(
            "✅ PostResponse.author"
        )
        print(
            "   ↓"
        )
        print(
            "✅ CommentResponse.author"
        )

    finally:
        print()
        print(
            "♻️ Step 4：恢复原用户资料"
        )

        restored = patch_me(
            client,
            nickname=
                original_nickname,
            avatar_url=
                original_avatar_url,
            bio=
                original_bio,
        )

        assert (
            restored.get(
                "nickname",
            )
            == original_nickname
        )

        print()
        print(
            "⏳ 等待原资料同步回 Projection..."
        )

        wait_for_post_projection(
            client,
            post_id=post_id,
            expected_nickname=
                original_nickname,
            expected_avatar_url=
                original_avatar_url,
        )

        print()
        print(
            "✅ 原用户资料已恢复"
        )