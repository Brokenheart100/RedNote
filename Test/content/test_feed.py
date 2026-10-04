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
) -> dict:
    response = client.post(
        "/api/v1/posts",
        headers=bearer_headers(user),
        json={
            "title": title,
            "content": f"Content for {title}",
            "mediaIds": [],
        },
    )

    assert response.status_code == 201

    return response.json()


def get_feed(
    client: httpx.Client,
    *,
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
        "/api/v1/posts/feed",
        headers=headers,
        params={
            "page": page,
            "pageSize": page_size,
        },
    )


def test_feed_allows_anonymous_access(
    client: httpx.Client,
) -> None:
    response = get_feed(
        client,
    )

    assert response.status_code == 200


def test_feed_returns_published_posts(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Feed Published Post",
    )

    response = get_feed(
        client,
        page=1,
        page_size=100,
    )

    assert response.status_code == 200

    ids = {
        item["id"]
        for item in response.json()["items"]
    }

    assert post["id"] in ids


def test_deleted_post_is_not_in_feed(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Feed Deleted Post",
    )

    delete_response = client.delete(
        f"/api/v1/posts/{post['id']}",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert delete_response.status_code == 204

    response = get_feed(
        client,
        page=1,
        page_size=100,
    )

    assert response.status_code == 200

    ids = {
        item["id"]
        for item in response.json()["items"]
    }

    assert post["id"] not in ids


def test_feed_orders_by_created_at_desc(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    first_post = create_post(
        client,
        authenticated_user,
        title="Feed Order First",
    )

    time.sleep(0.01)

    second_post = create_post(
        client,
        authenticated_user,
        title="Feed Order Second",
    )

    response = get_feed(
        client,
        page=1,
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


def test_feed_returns_like_count(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Feed Like Count",
    )

    like_response = client.post(
        f"/api/v1/posts/{post['id']}/likes",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert like_response.status_code == 204

    response = get_feed(
        client,
        page=1,
        page_size=100,
    )

    assert response.status_code == 200

    item = next(
        item
        for item in response.json()["items"]
        if item["id"] == post["id"]
    )

    assert item["likeCount"] == 1


def test_feed_returns_comment_count(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Feed Comment Count",
    )

    comment_response = client.post(
        f"/api/v1/posts/{post['id']}/comments",
        headers=bearer_headers(
            authenticated_user
        ),
        json={
            "content": "Feed comment",
            "parentCommentId": None,
        },
    )

    assert comment_response.status_code == 201

    response = get_feed(
        client,
        page=1,
        page_size=100,
    )

    assert response.status_code == 200

    item = next(
        item
        for item in response.json()["items"]
        if item["id"] == post["id"]
    )

    assert item["commentCount"] == 1


def test_anonymous_feed_has_false_user_state(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Anonymous Feed State",
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

    response = get_feed(
        client,
        page=1,
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


def test_authenticated_feed_returns_user_state(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Authenticated Feed State",
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

    response = get_feed(
        client,
        user=authenticated_user,
        page=1,
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


def test_feed_pagination(
    client: httpx.Client,
    create_user,
) -> None:
    user = create_user(
        prefix="feed_paging",
        display_name="Feed Paging",
    )

    created_ids: set[str] = set()

    for index in range(3):
        post = create_post(
            client,
            user,
            title=f"Feed Paging {index}",
        )

        created_ids.add(
            post["id"]
        )

        time.sleep(0.01)

    first_page = get_feed(
        client,
        page=1,
        page_size=2,
    )

    second_page = get_feed(
        client,
        page=2,
        page_size=2,
    )

    assert first_page.status_code == 200
    assert second_page.status_code == 200

    first_body = first_page.json()
    second_body = second_page.json()

    assert len(first_body["items"]) == 2
    assert len(second_body["items"]) >= 1

    returned_ids = {
        item["id"]
        for item in (
            first_body["items"]
            + second_body["items"]
        )
    }

    assert created_ids.issubset(
        returned_ids
    )


def test_feed_validates_pagination(
    client: httpx.Client,
) -> None:
    invalid_page = get_feed(
        client,
        page=0,
        page_size=20,
    )

    assert invalid_page.status_code == 400

    invalid_page_size = get_feed(
        client,
        page=1,
        page_size=101,
    )

    assert invalid_page_size.status_code == 400