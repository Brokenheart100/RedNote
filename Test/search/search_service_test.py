from __future__ import annotations

from collections.abc import Iterator
from typing import Any

import httpx
import pytest


SEARCH_SERVICE_BASE_URL = "http://localhost:5147"
SEARCH_TEST_TERM = "opensearch"

REQUEST_TIMEOUT_SECONDS = 10.0


@pytest.fixture(scope="session")
def client() -> Iterator[httpx.Client]:
    print()
    print("=" * 72)
    print("🔎 RedNote SearchService pytest")
    print(f"🌐 SearchService : {SEARCH_SERVICE_BASE_URL}")
    print(f"🔍 Search term   : {SEARCH_TEST_TERM}")
    print("=" * 72)

    with httpx.Client(
        base_url=SEARCH_SERVICE_BASE_URL,
        timeout=REQUEST_TIMEOUT_SECONDS,
        follow_redirects=False,
    ) as http_client:
        yield http_client


def search_posts(
    client: httpx.Client,
    *,
    query: str,
    page: int = 1,
    page_size: int = 20,
) -> httpx.Response:
    path = "/api/v1/search/posts"

    print()
    print("-" * 72)
    print(f"🌐 GET {path}")
    print(f"🔍 q        = {query!r}")
    print(f"📄 page     = {page}")
    print(f"📦 pageSize = {page_size}")

    response = client.get(
        path,
        params={
            "q": query,
            "page": page,
            "pageSize": page_size,
        },
    )

    print(f"📡 HTTP {response.status_code}")

    if response.content:
        print("📦 Response:")
        print(response.text[:5000])

    print("-" * 72)

    return response


def assert_problem_details(response: httpx.Response) -> dict[str, Any]:
    content_type = response.headers.get("content-type", "")

    assert content_type.startswith(
        "application/problem+json"
    ), (
        "❌ Content-Type 不正确。"
        f"期望 application/problem+json，实际 {content_type!r}"
    )

    body: Any = response.json()

    assert isinstance(body, dict), "❌ ProblemDetails 响应不是 JSON Object。"
    assert "status" in body, "❌ ProblemDetails 缺少 status。"

    return body


def test_search_service_is_reachable(
    client: httpx.Client,
) -> None:
    """
    验证：
    - SearchService 可以访问；
    - Wolverine HTTP Endpoint 已正确映射；
    - OpenSearch 查询能够正常执行。
    """
    response = search_posts(
        client,
        query="__rednote_nonexistent_93f7c84d__",
    )

    assert response.status_code == 200, (
        f"❌ SearchService 返回 HTTP {response.status_code}\n"
        f"{response.text}"
    )

    body: Any = response.json()

    assert isinstance(body, dict)
    assert body["page"] == 1
    assert body["pageSize"] == 20
    assert isinstance(body["totalCount"], int)
    assert body["totalCount"] >= 0
    assert isinstance(body["items"], list)

    print()
    print("✅ SearchService 搜索接口正常")
    print(f"📊 totalCount    = {body['totalCount']}")
    print(f"📦 returnedCount = {len(body['items'])}")


def test_known_document_can_be_searched(
    client: httpx.Client,
) -> None:
    """
    使用已知存在的 OpenSearch 关键词验证搜索结果。

    当前 posts-v1 已确认存在包含：
        opensearch

    的测试文档。
    """
    response = search_posts(
        client,
        query=SEARCH_TEST_TERM,
    )

    assert response.status_code == 200, (
        f"❌ 搜索请求失败，HTTP {response.status_code}\n"
        f"{response.text}"
    )

    body: Any = response.json()

    assert isinstance(body, dict)

    total_count = body["totalCount"]
    items = body["items"]

    assert isinstance(total_count, int)
    assert isinstance(items, list)

    print()
    print("🔎 OpenSearch 搜索结果")
    print(f"🔍 keyword       = {SEARCH_TEST_TERM!r}")
    print(f"📊 totalCount    = {total_count}")
    print(f"📦 returnedCount = {len(items)}")

    assert total_count > 0, (
        "\n"
        "❌ SearchService 请求成功，但没有搜索到已知文档。\n"
        f"🔍 keyword = {SEARCH_TEST_TERM!r}\n"
        "\n"
        "请检查：\n"
        "1. posts-v1 中是否仍存在对应文档\n"
        "2. OpenSearch analyzer / query 是否发生变化\n"
        "3. SearchService 是否连接了正确的 OpenSearch 实例\n"
    )

    assert len(items) > 0

    print("✅ 已知 OpenSearch 文档可以正常搜索")


def test_search_result_contract(
    client: httpx.Client,
) -> None:
    """
    SearchService 现在只暴露搜索命中信息：

        postId
        score

    完整 PostResponse 由 ContentService 负责。
    """
    response = search_posts(
        client,
        query=SEARCH_TEST_TERM,
    )

    assert response.status_code == 200

    body: Any = response.json()

    assert isinstance(body, dict)

    items = body["items"]

    assert isinstance(items, list)

    if not items:
        pytest.skip(
            "⏭️ 当前没有匹配文档，无法验证 SearchPostHitResponse 契约。"
        )

    item = items[0]

    assert isinstance(item, dict)

    expected_fields = {
        "postId",
        "score",
    }

    actual_fields = set(item.keys())

    missing_fields = expected_fields - actual_fields

    assert not missing_fields, (
        "❌ SearchPostHitResponse 缺少字段："
        f"{sorted(missing_fields)}"
    )

    unexpected_fields = actual_fields - expected_fields

    assert not unexpected_fields, (
        "❌ SearchService 暴露了不应存在的帖子展示字段："
        f"{sorted(unexpected_fields)}"
    )

    post_id = item["postId"]
    score = item["score"]

    assert isinstance(post_id, str)
    assert post_id.strip(), "❌ postId 不能为空。"

    assert score is None or isinstance(
        score,
        (int, float),
    )

    print()
    print("✅ SearchPostHitResponse 契约正确")
    print(f"🆔 postId = {post_id}")
    print(f"📈 score  = {score}")


def test_search_results_have_unique_post_ids(
    client: httpx.Client,
) -> None:
    """
    同一页搜索结果不应该出现重复帖子。
    """
    response = search_posts(
        client,
        query=SEARCH_TEST_TERM,
        page_size=100,
    )

    assert response.status_code == 200

    body: Any = response.json()

    assert isinstance(body, dict)

    items = body["items"]

    assert isinstance(items, list)

    post_ids = [
        item["postId"]
        for item in items
        if isinstance(item, dict)
        and isinstance(item.get("postId"), str)
    ]

    assert len(post_ids) == len(set(post_ids)), (
        "❌ 搜索结果中存在重复 postId。"
    )

    print(f"✅ 搜索结果 Post ID 无重复，共 {len(post_ids)} 条")


def test_pagination_contract(
    client: httpx.Client,
) -> None:
    response = search_posts(
        client,
        query=SEARCH_TEST_TERM,
        page=1,
        page_size=5,
    )

    assert response.status_code == 200

    body: Any = response.json()

    assert isinstance(body, dict)

    assert body["page"] == 1
    assert body["pageSize"] == 5
    assert isinstance(body["totalCount"], int)
    assert isinstance(body["items"], list)
    assert len(body["items"]) <= 5

    print()
    print("✅ 分页契约正确")
    print(f"📄 page       = {body['page']}")
    print(f"📦 pageSize   = {body['pageSize']}")
    print(f"📊 totalCount = {body['totalCount']}")


@pytest.mark.parametrize(
    ("query", "page", "page_size", "error_key"),
    [
        ("", 1, 20, "q"),
        ("test", 0, 20, "page"),
        ("test", 1, 0, "pageSize"),
        ("test", 1, 101, "pageSize"),
    ],
)
def test_invalid_search_parameters_return_400(
    client: httpx.Client,
    query: str,
    page: int,
    page_size: int,
    error_key: str,
) -> None:
    response = search_posts(
        client,
        query=query,
        page=page,
        page_size=page_size,
    )

    assert response.status_code == 400, (
        f"❌ 预期 HTTP 400，实际 HTTP {response.status_code}\n"
        f"{response.text}"
    )

    body = assert_problem_details(response)

    errors = body.get("errors")

    assert isinstance(errors, dict), (
        "❌ ValidationProblem 缺少 errors。"
    )

    assert error_key in errors, (
        "❌ ValidationProblem.errors 中缺少 "
        f"{error_key!r}。"
    )

    print(f"✅ 参数验证正确：{error_key}")


def test_query_longer_than_100_characters_returns_400(
    client: httpx.Client,
) -> None:
    response = search_posts(
        client,
        query="a" * 101,
    )

    assert response.status_code == 400

    body = assert_problem_details(response)

    errors = body.get("errors")

    assert isinstance(errors, dict)
    assert "q" in errors

    print("✅ q 最大长度 100 校验正确")


def test_query_length_100_is_allowed(
    client: httpx.Client,
) -> None:
    response = search_posts(
        client,
        query="a" * 100,
    )

    assert response.status_code == 200, (
        "❌ 长度恰好为 100 的 query 应该合法，"
        f"实际 HTTP {response.status_code}。"
    )

    print("✅ q=100 字符边界值正确")


def test_page_size_100_is_allowed(
    client: httpx.Client,
) -> None:
    response = search_posts(
        client,
        query=SEARCH_TEST_TERM,
        page=1,
        page_size=100,
    )

    assert response.status_code == 200, (
        "❌ pageSize=100 应该合法，"
        f"实际 HTTP {response.status_code}\n"
        f"{response.text}"
    )

    body: Any = response.json()

    assert isinstance(body, dict)
    assert body["page"] == 1
    assert body["pageSize"] == 100

    print("✅ pageSize=100 边界值正确")