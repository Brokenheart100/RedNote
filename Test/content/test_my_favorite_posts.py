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


def favorite_post(
    client: httpx.Client,
    user: AuthenticatedUser,
    post_id: str,
) -> None:
    response = client.post(
        f"/api/v1/posts/{post_id}/favorites",
        headers=bearer_headers(user),
    )

    assert response.status_code == 204


def unfavorite_post(
    client: httpx.Client,
    user: AuthenticatedUser,
    post_id: str,
) -> None:
    response = client.delete(
        f"/api/v1/posts/{post_id}/favorites",
        headers=bearer_headers(user),
    )

    assert response.status_code == 204


def get_favorites(
    client: httpx.Client,
    user: AuthenticatedUser,
    *,
    page: int = 1,
    page_size: int = 20,
) -> httpx.Response:
    return client.get(
        "/api/v1/posts/favorites",
        headers=bearer_headers(user),
        params={
            "page": page,
            "pageSize": page_size,
        },
    )


def test_favorite_list_requires_authentication(
    client: httpx.Client,
) -> None:
    response = client.get(
        "/api/v1/posts/favorites",
        params={
            "page": 1,
            "pageSize": 20,
        },
    )

    assert response.status_code == 401


def test_empty_favorite_list_returns_empty_items(
    client: httpx.Client,
    create_user,
) -> None:
    user = create_user(
        prefix="favorite_empty",
        display_name="Favorite Empty",
    )

    response = get_favorites(
        client,
        user,
    )

    assert response.status_code == 200

    body = response.json()

    assert body["page"] == 1
    assert body["pageSize"] == 20
    assert body["totalCount"] == 0
    assert body["items"] == []


def test_favorite_list_returns_favorited_posts(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    first_post = create_post(
        client,
        authenticated_user,
        title="Favorite List First",
    )

    second_post = create_post(
        client,
        authenticated_user,
        title="Favorite List Second",
    )

    favorite_post(
        client,
        authenticated_user,
        first_post["id"],
    )

    favorite_post(
        client,
        authenticated_user,
        second_post["id"],
    )

    response = get_favorites(
        client,
        authenticated_user,
    )

    assert response.status_code == 200

    body = response.json()

    assert body["totalCount"] >= 2

    ids = {
        item["id"]
        for item in body["items"]
    }

    assert first_post["id"] in ids
    assert second_post["id"] in ids


def test_favorite_list_orders_by_favorite_time(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    first_post = create_post(
        client,
        authenticated_user,
        title="Favorite Order First",
    )

    second_post = create_post(
        client,
        authenticated_user,
        title="Favorite Order Second",
    )

    favorite_post(
        client,
        authenticated_user,
        first_post["id"],
    )

    favorite_post(
        client,
        authenticated_user,
        second_post["id"],
    )

    response = get_favorites(
        client,
        authenticated_user,
        page=1,
        page_size=100,
    )

    assert response.status_code == 200

    items = response.json()["items"]

    indexes = {
        item["id"]: index
        for index, item in enumerate(items)
    }

    assert second_post["id"] in indexes
    assert first_post["id"] in indexes

    assert (
        indexes[second_post["id"]]
        <
        indexes[first_post["id"]]
    )


def test_unfavorite_removes_post_from_favorite_list(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Favorite Remove",
    )

    favorite_post(
        client,
        authenticated_user,
        post["id"],
    )

    before_response = get_favorites(
        client,
        authenticated_user,
        page=1,
        page_size=100,
    )

    assert before_response.status_code == 200

    before_ids = {
        item["id"]
        for item in before_response.json()["items"]
    }

    assert post["id"] in before_ids

    unfavorite_post(
        client,
        authenticated_user,
        post["id"],
    )

    after_response = get_favorites(
        client,
        authenticated_user,
        page=1,
        page_size=100,
    )

    assert after_response.status_code == 200

    after_ids = {
        item["id"]
        for item in after_response.json()["items"]
    }

    assert post["id"] not in after_ids


def test_favorites_are_isolated_between_users(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
    create_user,
) -> None:
    second_user = create_user(
        prefix="favorite_isolation",
        display_name="Favorite Isolation",
    )

    post = create_post(
        client,
        authenticated_user,
        title="Favorite Isolation Post",
    )

    favorite_post(
        client,
        authenticated_user,
        post["id"],
    )

    second_response = get_favorites(
        client,
        second_user,
        page=1,
        page_size=100,
    )

    assert second_response.status_code == 200

    second_ids = {
        item["id"]
        for item in second_response.json()["items"]
    }

    assert post["id"] not in second_ids


def test_favorite_list_marks_items_as_favorited(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Favorite State In List",
    )

    favorite_post(
        client,
        authenticated_user,
        post["id"],
    )

    response = get_favorites(
        client,
        authenticated_user,
        page=1,
        page_size=100,
    )

    assert response.status_code == 200

    item = next(
        item
        for item in response.json()["items"]
        if item["id"] == post["id"]
    )

    assert item["isFavorited"] is True


def test_favorite_list_returns_like_state(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Favorite Like State",
    )

    favorite_post(
        client,
        authenticated_user,
        post["id"],
    )

    like_response = client.post(
        f"/api/v1/posts/{post['id']}/likes",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert like_response.status_code == 204

    response = get_favorites(
        client,
        authenticated_user,
        page=1,
        page_size=100,
    )

    assert response.status_code == 200

    item = next(
        item
        for item in response.json()["items"]
        if item["id"] == post["id"]
    )

    assert item["likeCount"] >= 1
    assert item["isLiked"] is True


def test_deleted_post_is_not_returned_in_favorites(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Deleted Favorite Item",
    )

    favorite_post(
        client,
        authenticated_user,
        post["id"],
    )

    delete_response = client.delete(
        f"/api/v1/posts/{post['id']}",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert delete_response.status_code == 204

    response = get_favorites(
        client,
        authenticated_user,
        page=1,
        page_size=100,
    )

    assert response.status_code == 200

    ids = {
        item["id"]
        for item in response.json()["items"]
    }

    assert post["id"] not in ids


def test_favorite_list_pagination(
    client: httpx.Client,
    create_user,
) -> None:
    user = create_user(
        prefix="favorite_paging",
        display_name="Favorite Paging",
    )

    created_ids: set[str] = set()

    for index in range(3):
        post = create_post(
            client,
            user,
            title=f"Favorite Paging {index}",
        )

        created_ids.add(
            post["id"]
        )

        favorite_post(
            client,
            user,
            post["id"],
        )

    first_page = get_favorites(
        client,
        user,
        page=1,
        page_size=2,
    )

    second_page = get_favorites(
        client,
        user,
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


def test_favorite_list_validates_pagination(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    invalid_page = get_favorites(
        client,
        authenticated_user,
        page=0,
        page_size=20,
    )

    assert invalid_page.status_code == 400

    invalid_page_size = get_favorites(
        client,
        authenticated_user,
        page=1,
        page_size=101,
    )

    assert invalid_page_size.status_code == 400