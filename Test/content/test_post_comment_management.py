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
            "parentCommentId": parent_comment_id,
        },
    )

    assert response.status_code == 201

    return response.json()


def update_comment(
    client: httpx.Client,
    user: AuthenticatedUser,
    *,
    post_id: str,
    comment_id: str,
    content: str,
) -> httpx.Response:
    return client.patch(
        (
            f"/api/v1/posts/{post_id}"
            f"/comments/{comment_id}"
        ),
        headers=bearer_headers(user),
        json={
            "content": content
        },
    )


def delete_comment(
    client: httpx.Client,
    user: AuthenticatedUser,
    *,
    post_id: str,
    comment_id: str,
) -> httpx.Response:
    return client.delete(
        (
            f"/api/v1/posts/{post_id}"
            f"/comments/{comment_id}"
        ),
        headers=bearer_headers(user),
    )


def get_comments(
    client: httpx.Client,
    *,
    post_id: str,
) -> dict:
    response = client.get(
        f"/api/v1/posts/{post_id}/comments",
        params={
            "page": 1,
            "pageSize": 100,
        },
    )

    assert response.status_code == 200

    return response.json()


def test_author_can_update_comment(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Update Own Comment",
    )

    comment = create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="Before update",
    )

    response = update_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        comment_id=comment["id"],
        content="After update",
    )

    assert response.status_code == 200

    body = response.json()

    assert body["id"] == comment["id"]
    assert body["content"] == "After update"
    assert body["updatedAtUtc"]


def test_other_user_cannot_update_comment(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
    create_user,
) -> None:
    other_user = create_user(
        prefix="comment_update_other",
        display_name="Comment Update Other",
    )

    post = create_post(
        client,
        authenticated_user,
        title="Update Other Comment",
    )

    comment = create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="Owner comment",
    )

    response = update_comment(
        client,
        other_user,
        post_id=post["id"],
        comment_id=comment["id"],
        content="Should fail",
    )

    assert response.status_code == 403


def test_update_comment_requires_authentication(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Update Comment Auth",
    )

    comment = create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="Protected comment",
    )

    response = client.patch(
        (
            f"/api/v1/posts/{post['id']}"
            f"/comments/{comment['id']}"
        ),
        json={
            "content": "Anonymous update"
        },
    )

    assert response.status_code == 401


def test_update_comment_validates_empty_content(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Update Comment Empty",
    )

    comment = create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="Original",
    )

    response = update_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        comment_id=comment["id"],
        content="   ",
    )

    assert response.status_code == 400


def test_update_comment_validates_max_length(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Update Comment Length",
    )

    comment = create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="Original",
    )

    response = update_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        comment_id=comment["id"],
        content="x" * 1001,
    )

    assert response.status_code == 400


def test_author_can_delete_comment(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Delete Own Comment",
    )

    comment = create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="Delete me",
    )

    response = delete_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        comment_id=comment["id"],
    )

    assert response.status_code == 204


def test_delete_comment_is_idempotent(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Delete Comment Idempotent",
    )

    comment = create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="Delete twice",
    )

    first_response = delete_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        comment_id=comment["id"],
    )

    second_response = delete_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        comment_id=comment["id"],
    )

    assert first_response.status_code == 204
    assert second_response.status_code == 204


def test_other_user_cannot_delete_comment(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
    create_user,
) -> None:
    other_user = create_user(
        prefix="comment_delete_other",
        display_name="Comment Delete Other",
    )

    post = create_post(
        client,
        authenticated_user,
        title="Delete Other Comment",
    )

    comment = create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="Owner comment",
    )

    response = delete_comment(
        client,
        other_user,
        post_id=post["id"],
        comment_id=comment["id"],
    )

    assert response.status_code == 403


def test_delete_comment_requires_authentication(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Delete Comment Auth",
    )

    comment = create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="Protected delete",
    )

    response = client.delete(
        (
            f"/api/v1/posts/{post['id']}"
            f"/comments/{comment['id']}"
        )
    )

    assert response.status_code == 401


def test_deleted_root_comment_is_not_returned(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Deleted Root Hidden",
    )

    comment = create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="Hidden root",
    )

    delete_response = delete_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        comment_id=comment["id"],
    )

    assert delete_response.status_code == 204

    body = get_comments(
        client,
        post_id=post["id"],
    )

    ids = {
        item["id"]
        for item in body["items"]
    }

    assert comment["id"] not in ids


def test_deleted_reply_is_not_returned(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Deleted Reply Hidden",
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

    delete_response = delete_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        comment_id=reply["id"],
    )

    assert delete_response.status_code == 204

    body = get_comments(
        client,
        post_id=post["id"],
    )

    parent_item = next(
        item
        for item in body["items"]
        if item["id"] == parent["id"]
    )

    reply_ids = {
        item["id"]
        for item in parent_item["replies"]
    }

    assert reply["id"] not in reply_ids


def test_deleted_comment_cannot_be_updated(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Deleted Comment Update",
    )

    comment = create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="Delete then update",
    )

    delete_response = delete_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        comment_id=comment["id"],
    )

    assert delete_response.status_code == 204

    update_response = update_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        comment_id=comment["id"],
        content="Should not update",
    )

    assert update_response.status_code == 404