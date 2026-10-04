from __future__ import annotations

import sys
import time
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
    tags: list[str],
) -> dict:
    response = client.post(
        "/api/v1/posts",
        headers=bearer_headers(user),
        json={
            "title": title,
            "content": f"Content for {title}",
            "mediaIds": [],
            "tags": tags,
        },
    )

    assert response.status_code == 201

    return response.json()


def get_posts_by_tag(
    client: httpx.Client,
    *,
    tag_name: str,
    user: AuthenticatedUser | None = None,
    page: int = 1,
    page_size: int = 20,
) -> httpx.Response:
    headers = (
        bearer_headers(user)
        if user is not None
        else None
    )

    return client.get(
        f"/api/v1/posts/tags/{tag_name}",
        headers=headers,
        params={
            "page": page,
            "pageSize": page_size,
        },
    )


def test_posts_by_tag_allows_anonymous_access(
    client: httpx.Client,
) -> None:
    response = get_posts_by_tag(
        client,
        tag_name="anonymous-tag-test",
    )

    assert response.status_code == 200


def test_posts_by_tag_returns_matching_post(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Tag Matching Post",
        tags=[
            "aspire",
            "nuxt",
        ],
    )

    response = get_posts_by_tag(
        client,
        tag_name="aspire",
        page_size=100,
    )

    assert response.status_code == 200

    ids = {
        item["id"]
        for item in response.json()["items"]
    }

    assert post["id"] in ids


def test_posts_by_tag_is_case_insensitive(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Case Insensitive Tag",
        tags=[
            "Aspire",
        ],
    )

    response = get_posts_by_tag(
        client,
        tag_name="ASPIRE",
        page_size=100,
    )

    assert response.status_code == 200

    ids = {
        item["id"]
        for item in response.json()["items"]
    }

    assert post["id"] in ids


def test_posts_by_tag_excludes_different_tag(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    matching_post = create_post(
        client,
        authenticated_user,
        title="Matching Tag",
        tags=[
            "aspire",
        ],
    )

    unrelated_post = create_post(
        client,
        authenticated_user,
        title="Unrelated Tag",
        tags=[
            "nuxt",
        ],
    )

    response = get_posts_by_tag(
        client,
        tag_name="aspire",
        page_size=100,
    )

    assert response.status_code == 200

    ids = {
        item["id"]
        for item in response.json()["items"]
    }

    assert matching_post["id"] in ids
    assert unrelated_post["id"] not in ids


def test_deleted_post_is_not_returned_by_tag(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Deleted Tagged Post",
        tags=[
            "deleted-tag-test",
        ],
    )

    delete_response = client.delete(
        f"/api/v1/posts/{post['id']}",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert delete_response.status_code == 204

    response = get_posts_by_tag(
        client,
        tag_name="deleted-tag-test",
        page_size=100,
    )

    assert response.status_code == 200

    ids = {
        item["id"]
        for item in response.json()["items"]
    }

    assert post["id"] not in ids


def test_posts_by_tag_returns_all_tags(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="All Tags Response",
        tags=[
            "aspire",
            "nuxt",
            "dotnet",
        ],
    )

    response = get_posts_by_tag(
        client,
        tag_name="aspire",
        page_size=100,
    )

    assert response.status_code == 200

    item = next(
        item
        for item in response.json()["items"]
        if item["id"] == post["id"]
    )

    assert set(item["tags"]) == {
        "aspire",
        "nuxt",
        "dotnet",
    }


def test_posts_by_tag_returns_like_and_comment_count(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Tag Interaction Count",
        tags=[
            "interaction-tag",
        ],
    )

    like_response = client.post(
        f"/api/v1/posts/{post['id']}/likes",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert like_response.status_code == 204

    comment_response = client.post(
        f"/api/v1/posts/{post['id']}/comments",
        headers=bearer_headers(
            authenticated_user
        ),
        json={
            "content": "Tag comment",
            "parentCommentId": None,
        },
    )

    assert comment_response.status_code == 201

    response = get_posts_by_tag(
        client,
        tag_name="interaction-tag",
        page_size=100,
    )

    assert response.status_code == 200

    item = next(
        item
        for item in response.json()["items"]
        if item["id"] == post["id"]
    )

    assert item["likeCount"] == 1
    assert item["commentCount"] == 1


def test_anonymous_posts_by_tag_has_false_user_state(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Anonymous Tag State",
        tags=[
            "anonymous-state-tag",
        ],
    )

    like_response = client.post(
        f"/api/v1/posts/{post['id']}/likes",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    favorite_response = client.post(
        f"/api/v1/posts/{post['id']}/favorites",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert like_response.status_code == 204
    assert favorite_response.status_code == 204

    response = get_posts_by_tag(
        client,
        tag_name="anonymous-state-tag",
        page_size=100,
    )

    assert response.status_code == 200

    item = next(
        item
        for item in response.json()["items"]
        if item["id"] == post["id"]
    )

    assert item["likeCount"] == 1
    assert item["isLiked"] is False
    assert item["isFavorited"] is False


def test_authenticated_posts_by_tag_returns_user_state(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Authenticated Tag State",
        tags=[
            "authenticated-state-tag",
        ],
    )

    like_response = client.post(
        f"/api/v1/posts/{post['id']}/likes",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    favorite_response = client.post(
        f"/api/v1/posts/{post['id']}/favorites",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert like_response.status_code == 204
    assert favorite_response.status_code == 204

    response = get_posts_by_tag(
        client,
        tag_name="authenticated-state-tag",
        user=authenticated_user,
        page_size=100,
    )

    assert response.status_code == 200

    item = next(
        item
        for item in response.json()["items"]
        if item["id"] == post["id"]
    )

    assert item["isLiked"] is True
    assert item["isFavorited"] is True


def test_posts_by_tag_orders_by_created_at_desc(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    tag_name = "tag-order-test"

    first_post = create_post(
        client,
        authenticated_user,
        title="Tag Order First",
        tags=[
            tag_name,
        ],
    )

    time.sleep(0.01)

    second_post = create_post(
        client,
        authenticated_user,
        title="Tag Order Second",
        tags=[
            tag_name,
        ],
    )

    response = get_posts_by_tag(
        client,
        tag_name=tag_name,
        page_size=100,
    )

    assert response.status_code == 200

    items = response.json()["items"]

    indexes = {
        item["id"]: index
        for index, item in enumerate(items)
    }

    assert first_post["id"] in indexes
    assert second_post["id"] in indexes

    assert (
        indexes[second_post["id"]]
        <
        indexes[first_post["id"]]
    )


def test_posts_by_tag_pagination(
    client: httpx.Client,
    create_user,
) -> None:
    user = create_user(
        prefix="tag_paging",
        display_name="Tag Paging",
    )

    tag_name = "tag-paging-unique"

    created_ids: set[str] = set()

    for index in range(3):
        post = create_post(
            client,
            user,
            title=f"Tag Paging {index}",
            tags=[
                tag_name,
            ],
        )

        created_ids.add(
            post["id"]
        )

        time.sleep(0.01)

    first_page = get_posts_by_tag(
        client,
        tag_name=tag_name,
        page=1,
        page_size=2,
    )

    second_page = get_posts_by_tag(
        client,
        tag_name=tag_name,
        page=2,
        page_size=2,
    )

    assert first_page.status_code == 200
    assert second_page.status_code == 200

    first_body = first_page.json()
    second_body = second_page.json()

    assert first_body["totalCount"] == 3
    assert second_body["totalCount"] == 3

    assert len(first_body["items"]) == 2
    assert len(second_body["items"]) == 1

    returned_ids = {
        item["id"]
        for item in (
            first_body["items"]
            + second_body["items"]
        )
    }

    assert returned_ids == created_ids


def test_posts_by_tag_returns_empty_result(
    client: httpx.Client,
) -> None:
    response = get_posts_by_tag(
        client,
        tag_name="tag-that-does-not-exist-xyz",
    )

    assert response.status_code == 200

    body = response.json()

    assert body["totalCount"] == 0
    assert body["items"] == []


def test_posts_by_tag_validates_tag_length(
    client: httpx.Client,
) -> None:
    response = get_posts_by_tag(
        client,
        tag_name="x" * 31,
    )

    assert response.status_code == 400


def test_posts_by_tag_validates_pagination(
    client: httpx.Client,
) -> None:
    invalid_page = get_posts_by_tag(
        client,
        tag_name="validation-tag",
        page=0,
        page_size=20,
    )

    assert invalid_page.status_code == 400

    invalid_page_size = get_posts_by_tag(
        client,
        tag_name="validation-tag",
        page=1,
        page_size=101,
    )

    assert invalid_page_size.status_code == 400