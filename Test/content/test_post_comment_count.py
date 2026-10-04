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


def create_comment(
    client: httpx.Client,
    user: AuthenticatedUser,
    *,
    post_id: str,
    content: str,
    parent_comment_id: str | None = None,
) -> dict:
    response = client.post(
        f"/api/v1/posts/{post_id}/comments",
        headers=bearer_headers(user),
        json={
            "content": content,
            "parentCommentId":
                parent_comment_id,
        },
    )

    assert response.status_code == 201

    return response.json()


def delete_comment(
    client: httpx.Client,
    user: AuthenticatedUser,
    *,
    post_id: str,
    comment_id: str,
) -> None:
    response = client.delete(
        (
            f"/api/v1/posts/{post_id}"
            f"/comments/{comment_id}"
        ),
        headers=bearer_headers(user),
    )

    assert response.status_code == 204


def favorite_post(
    client: httpx.Client,
    user: AuthenticatedUser,
    *,
    post_id: str,
) -> None:
    response = client.post(
        f"/api/v1/posts/{post_id}/favorites",
        headers=bearer_headers(user),
    )

    assert response.status_code == 204


def get_user_id(
    client: httpx.Client,
    user: AuthenticatedUser,
) -> str:
    response = client.get(
        "/api/v1/users/me",
        headers=bearer_headers(user),
    )

    assert response.status_code == 200

    user_id = response.json()["userId"]

    assert isinstance(user_id, str)

    return user_id


def test_new_post_has_zero_comments(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Zero Comment Count",
    )

    assert post["commentCount"] == 0


def test_post_detail_returns_comment_count(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Post Comment Count",
    )

    create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="First comment",
    )

    create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="Second comment",
    )

    response = client.get(
        f"/api/v1/posts/{post['id']}"
    )

    assert response.status_code == 200

    body = response.json()

    assert body["commentCount"] == 2


def test_reply_is_included_in_comment_count(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Reply Comment Count",
    )

    parent = create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="Parent comment",
    )

    create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="Reply comment",
        parent_comment_id=parent["id"],
    )

    response = client.get(
        f"/api/v1/posts/{post['id']}"
    )

    assert response.status_code == 200

    assert response.json()["commentCount"] == 2


def test_deleted_comment_is_not_in_comment_count(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Deleted Comment Count",
    )

    first = create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="Keep comment",
    )

    second = create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="Delete comment",
    )

    delete_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        comment_id=second["id"],
    )

    response = client.get(
        f"/api/v1/posts/{post['id']}"
    )

    assert response.status_code == 200

    body = response.json()

    assert body["commentCount"] == 1

    assert first["id"] != second["id"]


def test_deleted_reply_is_not_in_comment_count(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Deleted Reply Count",
    )

    parent = create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="Parent",
    )

    reply = create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="Reply",
        parent_comment_id=parent["id"],
    )

    delete_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        comment_id=reply["id"],
    )

    response = client.get(
        f"/api/v1/posts/{post['id']}"
    )

    assert response.status_code == 200

    assert response.json()["commentCount"] == 1


def test_user_post_list_returns_comment_count(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    author_user_id = get_user_id(
        client,
        authenticated_user,
    )

    post = create_post(
        client,
        authenticated_user,
        title="List Comment Count",
    )

    create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="List comment one",
    )

    create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="List comment two",
    )

    response = client.get(
        "/api/v1/posts",
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

    item = next(
        item
        for item in response.json()["items"]
        if item["id"] == post["id"]
    )

    assert item["commentCount"] == 2


def test_favorite_list_returns_comment_count(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Favorite Comment Count",
    )

    create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="Favorite comment",
    )

    favorite_post(
        client,
        authenticated_user,
        post_id=post["id"],
    )

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
        if item["id"] == post["id"]
    )

    assert item["commentCount"] == 1


def test_update_post_preserves_comment_count(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Update Comment Count",
    )

    create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="Existing comment",
    )

    response = client.patch(
        f"/api/v1/posts/{post['id']}",
        headers=bearer_headers(
            authenticated_user
        ),
        json={
            "title": "Updated Comment Count",
            "content": "Updated content",
        },
    )

    assert response.status_code == 200

    body = response.json()

    assert body["commentCount"] == 1