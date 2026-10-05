from __future__ import annotations

import sys
from io import BytesIO
from pathlib import Path

import httpx


PROJECT_ROOT = Path(__file__).resolve().parents[2]

if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))


from Test.support import AuthenticatedUser, JPEG_BYTES


def bearer_headers(
    user: AuthenticatedUser,
) -> dict[str, str]:
    return {
        "Authorization":
            f"Bearer {user.access_token}"
    }


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


def upload_image(
    client: httpx.Client,
    user: AuthenticatedUser,
    *,
    file_name: str,
) -> str:
    response = client.post(
        "/api/v1/media/images",
        headers=bearer_headers(user),
        files={
            "file": (
                file_name,
                BytesIO(JPEG_BYTES),
                "image/jpeg",
            )
        },
    )

    assert response.status_code == 201

    media_id = response.json()["id"]

    assert isinstance(media_id, str)

    return media_id


def create_post(
    client: httpx.Client,
    user: AuthenticatedUser,
    *,
    title: str,
    media_ids: list[str],
) -> dict:
    response = client.post(
        "/api/v1/posts",
        headers=bearer_headers(user),
        json={
            "title": title,
            "content":
                f"Content for {title}",
            "mediaIds": media_ids,
        },
    )

    assert response.status_code == 201

    return response.json()


def get_user_posts(
    client: httpx.Client,
    *,
    author_user_id: str,
    page: int = 1,
    page_size: int = 20,
) -> dict:
    response = client.get(
        "/api/v1/posts",
        params={
            "authorUserId":
                author_user_id,
            "page":
                page,
            "pageSize":
                page_size,
        },
    )

    assert response.status_code == 200

    return response.json()


def find_post(
    body: dict,
    post_id: str,
) -> dict:
    for item in body["items"]:
        if item["id"] == post_id:
            return item

    raise AssertionError(
        f"Post '{post_id}' was not found."
    )


def test_user_posts_returns_media_ids(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    user_id = get_user_id(
        client,
        authenticated_user,
    )

    first_media_id = upload_image(
        client,
        authenticated_user,
        file_name="list-first.jpg",
    )

    second_media_id = upload_image(
        client,
        authenticated_user,
        file_name="list-second.jpg",
    )

    created = create_post(
        client,
        authenticated_user,
        title="List Media Test",
        media_ids=[
            first_media_id,
            second_media_id,
        ],
    )

    body = get_user_posts(
        client,
        author_user_id=user_id,
    )

    post = find_post(
        body,
        created["id"],
    )

    assert post["mediaIds"] == [
        first_media_id,
        second_media_id,
    ]


def test_user_posts_preserves_media_order(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    user_id = get_user_id(
        client,
        authenticated_user,
    )

    first_media_id = upload_image(
        client,
        authenticated_user,
        file_name="order-first.jpg",
    )

    second_media_id = upload_image(
        client,
        authenticated_user,
        file_name="order-second.jpg",
    )

    third_media_id = upload_image(
        client,
        authenticated_user,
        file_name="order-third.jpg",
    )

    expected_media_ids = [
        third_media_id,
        first_media_id,
        second_media_id,
    ]

    created = create_post(
        client,
        authenticated_user,
        title="List Media Order",
        media_ids=expected_media_ids,
    )

    body = get_user_posts(
        client,
        author_user_id=user_id,
    )

    post = find_post(
        body,
        created["id"],
    )

    assert (
        post["mediaIds"]
        == expected_media_ids
    )


def test_user_posts_without_media_returns_empty_list(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    user_id = get_user_id(
        client,
        authenticated_user,
    )

    created = create_post(
        client,
        authenticated_user,
        title="List No Media",
        media_ids=[],
    )

    body = get_user_posts(
        client,
        author_user_id=user_id,
    )

    post = find_post(
        body,
        created["id"],
    )

    assert post["mediaIds"] == []


def test_deleted_post_is_not_returned_in_user_posts(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    user_id = get_user_id(
        client,
        authenticated_user,
    )

    media_id = upload_image(
        client,
        authenticated_user,
        file_name="list-deleted.jpg",
    )

    created = create_post(
        client,
        authenticated_user,
        title="Deleted List Post",
        media_ids=[
            media_id,
        ],
    )

    delete_response = client.delete(
        f"/api/v1/posts/{created['id']}",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert delete_response.status_code == 204

    body = get_user_posts(
        client,
        author_user_id=user_id,
    )

    ids = {
        item["id"]
        for item in body["items"]
    }

    assert created["id"] not in ids