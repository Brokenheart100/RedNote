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


def like_post(
    client: httpx.Client,
    user: AuthenticatedUser,
    post_id: str,
) -> None:
    response = client.post(
        f"/api/v1/posts/{post_id}/likes",
        headers=bearer_headers(user),
    )

    assert response.status_code == 204


def test_new_post_has_zero_likes(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Zero Likes",
    )

    assert post["likeCount"] == 0
    assert post["isLiked"] is False


def test_authenticated_user_sees_is_liked_true(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Liked State",
    )

    like_post(
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

    assert body["likeCount"] == 1
    assert body["isLiked"] is True


def test_anonymous_user_sees_like_count_but_not_is_liked(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Anonymous Like State",
    )

    like_post(
        client,
        authenticated_user,
        post["id"],
    )

    response = client.get(
        f"/api/v1/posts/{post['id']}"
    )

    assert response.status_code == 200

    body = response.json()

    assert body["likeCount"] == 1
    assert body["isLiked"] is False


def test_unlike_updates_like_state(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Unlike State",
    )

    like_post(
        client,
        authenticated_user,
        post["id"],
    )

    unlike_response = client.delete(
        f"/api/v1/posts/{post['id']}/likes",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert unlike_response.status_code == 204

    response = client.get(
        f"/api/v1/posts/{post['id']}",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert response.status_code == 200

    body = response.json()

    assert body["likeCount"] == 0
    assert body["isLiked"] is False


def test_multiple_users_increase_like_count(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
    create_user,
) -> None:
    second_user = create_user(
        prefix="like_state_second",
        display_name="Like State Second",
    )

    post = create_post(
        client,
        authenticated_user,
        title="Multiple Likes",
    )

    like_post(
        client,
        authenticated_user,
        post["id"],
    )

    like_post(
        client,
        second_user,
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

    assert body["likeCount"] == 2
    assert body["isLiked"] is True


def test_post_list_returns_like_state(
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
        title="List Like State",
    )

    like_post(
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

    assert item["likeCount"] == 1
    assert item["isLiked"] is True