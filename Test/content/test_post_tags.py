from __future__ import annotations

import sys
from pathlib import Path

import httpx


PROJECT_ROOT = Path(__file__).resolve().parents[2]

if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))


from Test.support import AuthenticatedUser


def bearer_headers(
    user: AuthenticatedUser,
) -> dict[str, str]:
    return {
        "Authorization":
            f"Bearer {user.access_token}"
    }


def create_post(
    client: httpx.Client,
    user: AuthenticatedUser,
    *,
    title: str,
    content: str,
    tags: list[str] | None = None,
) -> httpx.Response:
    return client.post(
        "/api/v1/posts",
        headers=bearer_headers(user),
        json={
            "title": title,
            "content": content,
            "mediaIds": [],
            "tags": tags,
        },
    )


def get_post(
    client: httpx.Client,
    post_id: str,
) -> httpx.Response:
    return client.get(
        f"/api/v1/posts/{post_id}"
    )


def test_create_post_without_tags(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    response = create_post(
        client,
        authenticated_user,
        title="Post Without Tags",
        content="No tags",
        tags=None,
    )

    assert response.status_code == 201

    body = response.json()

    assert body["tags"] == []


def test_create_post_with_tags(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    response = create_post(
        client,
        authenticated_user,
        title="Tagged Post",
        content="Tagged content",
        tags=[
            "aspire",
            "nuxt",
        ],
    )

    assert response.status_code == 201

    body = response.json()

    assert set(body["tags"]) == {
        "aspire",
        "nuxt",
    }


def test_tags_are_trimmed(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    response = create_post(
        client,
        authenticated_user,
        title="Trimmed Tags",
        content="Trimmed tag content",
        tags=[
            "  aspire  ",
            " nuxt ",
        ],
    )

    assert response.status_code == 201

    body = response.json()

    assert set(body["tags"]) == {
        "aspire",
        "nuxt",
    }


def test_tags_are_deduplicated_case_insensitively(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    response = create_post(
        client,
        authenticated_user,
        title="Duplicate Tags",
        content="Duplicate tags",
        tags=[
            "Nuxt",
            "nuxt",
            " NUXT ",
            "Aspire",
        ],
    )

    assert response.status_code == 201

    body = response.json()

    assert len(body["tags"]) == 2

    lowered = {
        tag.lower()
        for tag in body["tags"]
    }

    assert lowered == {
        "nuxt",
        "aspire",
    }


def test_empty_tag_is_rejected(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    response = create_post(
        client,
        authenticated_user,
        title="Empty Tag",
        content="Empty tag test",
        tags=[
            "aspire",
            "   ",
        ],
    )

    assert response.status_code == 400


def test_tag_longer_than_30_characters_is_rejected(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    response = create_post(
        client,
        authenticated_user,
        title="Long Tag",
        content="Long tag test",
        tags=[
            "x" * 31,
        ],
    )

    assert response.status_code == 400


def test_30_character_tag_is_allowed(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    tag = "x" * 30

    response = create_post(
        client,
        authenticated_user,
        title="Max Length Tag",
        content="Max tag length",
        tags=[
            tag,
        ],
    )

    assert response.status_code == 201

    assert response.json()["tags"] == [
        tag
    ]


def test_more_than_10_tags_is_rejected(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    tags = [
        f"tag-{index}"
        for index in range(11)
    ]

    response = create_post(
        client,
        authenticated_user,
        title="Too Many Tags",
        content="Too many tags",
        tags=tags,
    )

    assert response.status_code == 400


def test_duplicate_tags_do_not_count_toward_limit(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    tags = [
        "TagOne",
        "tagone",
        " TAGONE ",
    ]

    tags.extend(
        f"tag-{index}"
        for index in range(9)
    )

    response = create_post(
        client,
        authenticated_user,
        title="Duplicate Tag Limit",
        content="Duplicate tag limit test",
        tags=tags,
    )

    assert response.status_code == 201

    assert len(
        response.json()["tags"]
    ) == 10


def test_post_detail_returns_tags(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    create_response = create_post(
        client,
        authenticated_user,
        title="Detail Tags",
        content="Detail tag content",
        tags=[
            "csharp",
            "aspire",
        ],
    )

    assert create_response.status_code == 201

    post_id = create_response.json()["id"]

    response = get_post(
        client,
        post_id,
    )

    assert response.status_code == 200

    body = response.json()

    assert set(body["tags"]) == {
        "csharp",
        "aspire",
    }


def test_update_post_preserves_tags(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    create_response = create_post(
        client,
        authenticated_user,
        title="Preserve Tags",
        content="Original content",
        tags=[
            "nuxt",
            "aspire",
        ],
    )

    assert create_response.status_code == 201

    post_id = create_response.json()["id"]

    update_response = client.patch(
        f"/api/v1/posts/{post_id}",
        headers=bearer_headers(
            authenticated_user
        ),
        json={
            "title": "Updated Preserve Tags",
            "content": "Updated content",
        },
    )

    assert update_response.status_code == 200

    body = update_response.json()

    assert set(body["tags"]) == {
        "nuxt",
        "aspire",
    }


def test_user_post_list_returns_tags(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    me_response = client.get(
        "/api/v1/users/me",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert me_response.status_code == 200

    user_id = me_response.json()["userId"]

    create_response = create_post(
        client,
        authenticated_user,
        title="User List Tags",
        content="User list tags",
        tags=[
            "dotnet",
            "postgres",
        ],
    )

    assert create_response.status_code == 201

    post_id = create_response.json()["id"]

    response = client.get(
        "/api/v1/posts",
        params={
            "authorUserId":
                user_id,
            "page":
                1,
            "pageSize":
                100,
        },
    )

    assert response.status_code == 200

    item = next(
        item
        for item in response.json()["items"]
        if item["id"] == post_id
    )

    assert set(item["tags"]) == {
        "dotnet",
        "postgres",
    }


def test_latest_feed_returns_tags(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    create_response = create_post(
        client,
        authenticated_user,
        title="Feed Tags",
        content="Feed tags",
        tags=[
            "feed",
            "aspire",
        ],
    )

    assert create_response.status_code == 201

    post_id = create_response.json()["id"]

    response = client.get(
        "/api/v1/posts/feed",
        params={
            "page": 1,
            "pageSize": 100,
        },
    )
    print()
    print("STATUS:", response.status_code)
    print("BODY:", response.text)
    assert response.status_code == 200

    item = next(
        item
        for item in response.json()["items"]
        if item["id"] == post_id
    )

    assert set(item["tags"]) == {
        "feed",
        "aspire",
    }


def test_favorite_list_returns_tags(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    create_response = create_post(
        client,
        authenticated_user,
        title="Favorite Tags",
        content="Favorite tag content",
        tags=[
            "favorite",
            "nuxt",
        ],
    )

    assert create_response.status_code == 201

    post_id = create_response.json()["id"]

    favorite_response = client.post(
        f"/api/v1/posts/{post_id}/favorites",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert favorite_response.status_code == 204

    response = client.get(
        "/api/v1/posts/favorites",
        headers=bearer_headers(
            authenticated_user
        ),
        params={
            "page": 1,
            "pageSize": 100,
        },
    )

    assert response.status_code == 200

    item = next(
        item
        for item in response.json()["items"]
        if item["id"] == post_id
    )

    assert set(item["tags"]) == {
        "favorite",
        "nuxt",
    }


def test_search_result_returns_tags(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    keyword = "UniqueTagSearchResult"

    create_response = create_post(
        client,
        authenticated_user,
        title=keyword,
        content="Search result tags",
        tags=[
            "search",
            "csharp",
        ],
    )

    assert create_response.status_code == 201

    post_id = create_response.json()["id"]

    response = client.get(
        "/api/v1/posts/search",
        params={
            "q": keyword,
            "page": 1,
            "pageSize": 100,
        },
    )

    assert response.status_code == 200

    item = next(
        item
        for item in response.json()["items"]
        if item["id"] == post_id
    )

    assert set(item["tags"]) == {
        "search",
        "csharp",
    }