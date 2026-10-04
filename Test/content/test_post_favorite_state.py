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


def test_new_post_is_not_favorited(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="New Favorite State",
    )

    assert post["isFavorited"] is False


def test_authenticated_user_sees_is_favorited_true(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Favorited State",
    )

    favorite_post(
        client,
        authenticated_user,
        post["id"],
    )

    response = client.get(
        f"/api/v1/posts/{post['id']}",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert response.status_code == 200

    body = response.json()

    assert body["isFavorited"] is True


def test_anonymous_user_does_not_see_favorited_state(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Anonymous Favorite State",
    )

    favorite_post(
        client,
        authenticated_user,
        post["id"],
    )

    response = client.get(
        f"/api/v1/posts/{post['id']}"
    )

    assert response.status_code == 200

    body = response.json()

    assert body["isFavorited"] is False


def test_unfavorite_updates_favorite_state(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Unfavorite State",
    )

    favorite_post(
        client,
        authenticated_user,
        post["id"],
    )

    unfavorite_response = client.delete(
        f"/api/v1/posts/{post['id']}/favorites",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert unfavorite_response.status_code == 204

    response = client.get(
        f"/api/v1/posts/{post['id']}",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert response.status_code == 200

    body = response.json()

    assert body["isFavorited"] is False


def test_other_user_does_not_see_my_favorite_state(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
    create_user,
) -> None:
    second_user = create_user(
        prefix="favorite_state_other",
        display_name="Favorite State Other",
    )

    post = create_post(
        client,
        authenticated_user,
        title="Favorite Per User",
    )

    favorite_post(
        client,
        authenticated_user,
        post["id"],
    )

    response = client.get(
        f"/api/v1/posts/{post['id']}",
        headers=bearer_headers(
            second_user
        ),
    )

    assert response.status_code == 200

    body = response.json()

    assert body["isFavorited"] is False


def test_post_list_returns_favorite_state(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    user_response = client.get(
        "/api/v1/users/me",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert user_response.status_code == 200

    author_user_id = (
        user_response.json()["userId"]
    )

    post = create_post(
        client,
        authenticated_user,
        title="List Favorite State",
    )

    favorite_post(
        client,
        authenticated_user,
        post["id"],
    )

    response = client.get(
        "/api/v1/posts",
        headers=bearer_headers(
            authenticated_user
        ),
        params={
            "authorUserId":
                author_user_id,
            "page":
                1,
            "pageSize":
                100,
        },
    )

    assert response.status_code == 200

    body = response.json()

    item = next(
        item
        for item in body["items"]
        if item["id"] == post["id"]
    )

    assert item["isFavorited"] is True