from __future__ import annotations

from collections.abc import Iterator
from typing import Any
from uuid import UUID, uuid4

import httpx
import pytest


CONTENT_SERVICE_BASE_URL = "http://localhost:5012"

# 这里可以填你数据库里真实存在、且状态为 Published 的帖子 ID。
# 留空时，依赖真实数据的测试会自动 skip。
KNOWN_POST_ID: str | None = None

REQUEST_TIMEOUT_SECONDS = 15.0


@pytest.fixture(scope="session")
def client() -> Iterator[httpx.Client]:
    print()
    print("=" * 72)
    print("📦 RedNote ContentService posts/batch pytest")
    print(f"🌐 ContentService : {CONTENT_SERVICE_BASE_URL}")
    print(f"🆔 Known Post ID  : {KNOWN_POST_ID or '<not configured>'}")
    print("=" * 72)

    with httpx.Client(
        base_url=CONTENT_SERVICE_BASE_URL,
        timeout=REQUEST_TIMEOUT_SECONDS,
        follow_redirects=False,
    ) as http_client:
        yield http_client


def post_batch(
    client: httpx.Client,
    post_ids: list[str],
) -> httpx.Response:
    path = "/api/v1/posts/batch"

    print()
    print("-" * 72)
    print(f"🌐 POST {path}")
    print(f"📦 postIds = {post_ids}")

    response = client.post(
        path,
        json={
            "postIds": post_ids,
        },
    )

    print(f"📡 HTTP {response.status_code}")

    if response.content:
        print("📦 Response:")
        print(response.text[:5000])

    print("-" * 72)

    return response


def assert_post_response_contract(
    post: dict[str, Any],
) -> None:
    expected_fields = {
        "id",
        "authorUserId",
        "title",
        "content",
        "mediaIds",
        "media",
        "tags",
        "likeCount",
        "commentCount",
        "isLiked",
        "isFavorited",
        "createdAtUtc",
        "updatedAtUtc",
    }

    actual_fields = set(post.keys())

    missing_fields = expected_fields - actual_fields

    assert not missing_fields, (
        "❌ PostResponse 缺少字段："
        f"{sorted(missing_fields)}"
    )

    assert isinstance(post["id"], str)
    UUID(post["id"])

    assert isinstance(post["authorUserId"], str)
    UUID(post["authorUserId"])

    assert isinstance(post["title"], str)
    assert isinstance(post["content"], str)

    assert isinstance(post["mediaIds"], list)
    assert isinstance(post["media"], list)

    tags = post["tags"]

    assert tags is None or isinstance(tags, list)

    assert isinstance(post["likeCount"], int)
    assert post["likeCount"] >= 0

    assert isinstance(post["commentCount"], int)
    assert post["commentCount"] >= 0

    assert isinstance(post["isLiked"], bool)
    assert isinstance(post["isFavorited"], bool)

    assert isinstance(post["createdAtUtc"], str)
    assert isinstance(post["updatedAtUtc"], str)


def test_batch_endpoint_is_reachable(
    client: httpx.Client,
) -> None:
    """
    空数组应该合法，并直接返回 []。
    """
    response = post_batch(
        client,
        [],
    )

    assert response.status_code == 200, (
        f"❌ /posts/batch 返回 HTTP {response.status_code}\n"
        f"{response.text}"
    )

    body: Any = response.json()

    assert isinstance(body, list)
    assert body == []

    print("✅ posts/batch endpoint 可访问")


def test_nonexistent_post_is_ignored(
    client: httpx.Client,
) -> None:
    """
    一个不存在的 Post ID 不应该导致整个 batch 失败。
    """
    missing_post_id = str(uuid4())

    response = post_batch(
        client,
        [missing_post_id],
    )

    assert response.status_code == 200

    body: Any = response.json()

    assert isinstance(body, list)
    assert body == []

    print("✅ 不存在的帖子会被安全忽略")


def test_duplicate_post_ids_do_not_duplicate_results(
    client: httpx.Client,
) -> None:
    if KNOWN_POST_ID is None:
        pytest.skip(
            "⏭️ KNOWN_POST_ID 未配置，跳过真实帖子重复 ID 测试。"
        )

    response = post_batch(
        client,
        [
            KNOWN_POST_ID,
            KNOWN_POST_ID,
            KNOWN_POST_ID,
        ],
    )

    assert response.status_code == 200

    body: Any = response.json()

    assert isinstance(body, list)

    assert len(body) == 1, (
        "❌ 重复 Post ID 不应该返回重复帖子。"
    )

    assert body[0]["id"].lower() == KNOWN_POST_ID.lower()

    print("✅ 重复 Post ID 已正确去重")


def test_known_post_can_be_resolved(
    client: httpx.Client,
) -> None:
    if KNOWN_POST_ID is None:
        pytest.skip(
            "⏭️ KNOWN_POST_ID 未配置，跳过真实 PostResponse 测试。"
        )

    response = post_batch(
        client,
        [KNOWN_POST_ID],
    )

    assert response.status_code == 200, (
        f"❌ 已知帖子 batch 查询失败，HTTP {response.status_code}\n"
        f"{response.text}"
    )

    body: Any = response.json()

    assert isinstance(body, list)

    assert len(body) == 1, (
        "❌ 已知 Published 帖子应该返回 1 条结果。"
    )

    post = body[0]

    assert isinstance(post, dict)

    assert_post_response_contract(post)

    assert post["id"].lower() == KNOWN_POST_ID.lower()

    print()
    print("✅ 已知帖子成功补齐为完整 PostResponse")
    print(f"🆔 id           = {post['id']}")
    print(f"📝 title        = {post['title']!r}")
    print(f"🖼️ mediaCount   = {len(post['media'])}")
    print(f"🏷️ tags         = {post['tags']}")
    print(f"❤️ likeCount    = {post['likeCount']}")
    print(f"💬 commentCount = {post['commentCount']}")
    print(f"👍 isLiked      = {post['isLiked']}")
    print(f"⭐ isFavorited  = {post['isFavorited']}")


def test_batch_preserves_requested_order(
    client: httpx.Client,
) -> None:
    """
    需要至少两个真实帖子才能真正验证排序。

    暂时不硬编码测试数据，因此这里只在没有数据时跳过。
    后续可以增加：
        KNOWN_POST_IDS = ["...", "..."]
    专门验证 OpenSearch 顺序不会被 EF 查询打乱。
    """
    pytest.skip(
        "⏭️ 当前未配置两个真实 Published Post ID，"
        "暂时跳过 batch 顺序测试。"
    )


def test_more_than_100_post_ids_returns_400(
    client: httpx.Client,
) -> None:
    post_ids = [
        str(uuid4())
        for _ in range(101)
    ]

    response = post_batch(
        client,
        post_ids,
    )

    assert response.status_code == 400, (
        "❌ 超过 100 个 Post ID 应返回 HTTP 400，"
        f"实际 HTTP {response.status_code}\n"
        f"{response.text}"
    )

    body: Any = response.json()

    assert isinstance(body, dict)

    errors = body.get("errors")

    assert isinstance(errors, dict)
    assert "postIds" in errors

    print("✅ 最大 batch size=100 校验正确")


def test_100_post_ids_is_allowed(
    client: httpx.Client,
) -> None:
    post_ids = [
        str(uuid4())
        for _ in range(100)
    ]

    response = post_batch(
        client,
        post_ids,
    )

    assert response.status_code == 200, (
        "❌ 100 个 Post ID 应该是合法边界值，"
        f"实际 HTTP {response.status_code}\n"
        f"{response.text}"
    )

    body: Any = response.json()

    assert isinstance(body, list)

    print("✅ batch size=100 边界值正确")