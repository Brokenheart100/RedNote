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
) -> httpx.Response:
    return client.post(
        f"/api/v1/posts/{post_id}/comments",
        headers=bearer_headers(user),
        json={
            "content": content,
            "parentCommentId": parent_comment_id,
        },
    )


def get_comments(
    client: httpx.Client,
    *,
    post_id: str,
    page: int = 1,
    page_size: int = 20,
) -> httpx.Response:
    return client.get(
        f"/api/v1/posts/{post_id}/comments",
        params={
            "page": page,
            "pageSize": page_size,
        },
    )


def test_create_comment_succeeds(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Comment Create",
    )

    response = create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="First comment",
    )

    assert response.status_code == 201

    body = response.json()

    assert body["postId"] == post["id"]
    assert body["content"] == "First comment"
    assert body["parentCommentId"] is None
    assert body["authorUserId"]


def test_create_comment_requires_authentication(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Comment Auth",
    )

    response = client.post(
        f"/api/v1/posts/{post['id']}/comments",
        json={
            "content": "Anonymous comment",
            "parentCommentId": None,
        },
    )

    assert response.status_code == 401


def test_create_comment_rejects_empty_content(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Comment Empty",
    )

    response = create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="   ",
    )

    assert response.status_code == 400


def test_create_comment_rejects_too_long_content(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Comment Too Long",
    )

    response = create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="x" * 1001,
    )

    assert response.status_code == 400


def test_create_comment_for_missing_post_returns_404(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    response = create_comment(
        client,
        authenticated_user,
        post_id=(
            "11111111-1111-1111-1111-111111111111"
        ),
        content="Missing post comment",
    )

    assert response.status_code == 404


def test_create_comment_for_deleted_post_returns_404(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Deleted Comment Post",
    )

    delete_response = client.delete(
        f"/api/v1/posts/{post['id']}",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert delete_response.status_code == 204

    response = create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="Should fail",
    )

    assert response.status_code == 404


def test_create_reply_to_comment_succeeds(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Comment Reply",
    )

    parent_response = create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="Parent comment",
    )

    assert parent_response.status_code == 201

    parent = parent_response.json()

    reply_response = create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="Reply comment",
        parent_comment_id=parent["id"],
    )

    assert reply_response.status_code == 201

    reply = reply_response.json()

    assert reply["parentCommentId"] == parent["id"]
    assert reply["content"] == "Reply comment"


def test_reply_to_missing_parent_returns_400(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Missing Parent",
    )

    response = create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="Reply to missing parent",
        parent_comment_id=(
            "11111111-1111-1111-1111-111111111111"
        ),
    )

    assert response.status_code == 400


def test_reply_to_comment_from_other_post_returns_400(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    first_post = create_post(
        client,
        authenticated_user,
        title="Reply Source Post",
    )

    second_post = create_post(
        client,
        authenticated_user,
        title="Reply Target Post",
    )

    parent_response = create_comment(
        client,
        authenticated_user,
        post_id=first_post["id"],
        content="Parent on first post",
    )

    assert parent_response.status_code == 201

    response = create_comment(
        client,
        authenticated_user,
        post_id=second_post["id"],
        content="Invalid cross post reply",
        parent_comment_id=(
            parent_response.json()["id"]
        ),
    )

    assert response.status_code == 400


def test_third_level_comment_is_rejected(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Comment Depth",
    )

    parent_response = create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="Level 1",
    )

    assert parent_response.status_code == 201

    reply_response = create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="Level 2",
        parent_comment_id=(
            parent_response.json()["id"]
        ),
    )

    assert reply_response.status_code == 201

    third_level_response = create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="Level 3",
        parent_comment_id=(
            reply_response.json()["id"]
        ),
    )

    assert third_level_response.status_code == 400


def test_comments_can_be_read_anonymously(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Anonymous Comments",
    )

    comment_response = create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="Public comment",
    )

    assert comment_response.status_code == 201

    response = get_comments(
        client,
        post_id=post["id"],
    )

    assert response.status_code == 200

    body = response.json()

    assert body["totalCount"] >= 1

    ids = {
        item["id"]
        for item in body["items"]
    }

    assert comment_response.json()["id"] in ids


def test_comment_list_returns_replies(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Comment List Replies",
    )

    parent_response = create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="Parent",
    )

    assert parent_response.status_code == 201

    parent = parent_response.json()

    first_reply_response = create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="First reply",
        parent_comment_id=parent["id"],
    )

    second_reply_response = create_comment(
        client,
        authenticated_user,
        post_id=post["id"],
        content="Second reply",
        parent_comment_id=parent["id"],
    )

    assert first_reply_response.status_code == 201
    assert second_reply_response.status_code == 201

    response = get_comments(
        client,
        post_id=post["id"],
        page=1,
        page_size=100,
    )

    assert response.status_code == 200

    body = response.json()

    item = next(
        item
        for item in body["items"]
        if item["id"] == parent["id"]
    )

    assert len(item["replies"]) == 2

    reply_ids = [
        reply["id"]
        for reply in item["replies"]
    ]

    assert first_reply_response.json()["id"] in reply_ids
    assert second_reply_response.json()["id"] in reply_ids


def test_comment_list_paginates_root_comments(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Comment Pagination",
    )

    created_ids: set[str] = set()

    for index in range(3):
        response = create_comment(
            client,
            authenticated_user,
            post_id=post["id"],
            content=f"Comment {index}",
        )

        assert response.status_code == 201

        created_ids.add(
            response.json()["id"]
        )

    first_page = get_comments(
        client,
        post_id=post["id"],
        page=1,
        page_size=2,
    )

    second_page = get_comments(
        client,
        post_id=post["id"],
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


def test_comment_list_validates_pagination(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Comment Pagination Validation",
    )

    invalid_page = get_comments(
        client,
        post_id=post["id"],
        page=0,
        page_size=20,
    )

    assert invalid_page.status_code == 400

    invalid_page_size = get_comments(
        client,
        post_id=post["id"],
        page=1,
        page_size=101,
    )

    assert invalid_page_size.status_code == 400