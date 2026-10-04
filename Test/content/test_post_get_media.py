from __future__ import annotations

import sys
from io import BytesIO
from pathlib import Path

import httpx


PROJECT_ROOT = Path(__file__).resolve().parents[2]

if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))


from Test.support import AuthenticatedUser


JPEG_BYTES = b"\xff\xd8\xff\xd9"


def bearer_headers(
    user: AuthenticatedUser,
) -> dict[str, str]:
    return {
        "Authorization":
            f"Bearer {user.access_token}"
    }


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
    media_ids: list[str],
) -> dict:
    response = client.post(
        "/api/v1/posts",
        headers=bearer_headers(user),
        json={
            "title": "Get Post Media Test",
            "content": "Post media query test.",
            "mediaIds": media_ids,
        },
    )

    assert response.status_code == 201

    return response.json()


def test_get_post_returns_media_ids(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    first_media_id = upload_image(
        client,
        authenticated_user,
        file_name="first.jpg",
    )

    second_media_id = upload_image(
        client,
        authenticated_user,
        file_name="second.jpg",
    )

    created = create_post(
        client,
        authenticated_user,
        media_ids=[
            first_media_id,
            second_media_id,
        ],
    )

    response = client.get(
        f"/api/v1/posts/{created['id']}"
    )

    assert response.status_code == 200

    body = response.json()

    assert body["id"] == created["id"]

    assert body["mediaIds"] == [
        first_media_id,
        second_media_id,
    ]


def test_get_post_preserves_media_order(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
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
        media_ids=expected_media_ids,
    )

    response = client.get(
        f"/api/v1/posts/{created['id']}"
    )

    assert response.status_code == 200

    assert (
        response.json()["mediaIds"]
        == expected_media_ids
    )


def test_get_post_without_media_returns_empty_list(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    created = create_post(
        client,
        authenticated_user,
        media_ids=[],
    )

    response = client.get(
        f"/api/v1/posts/{created['id']}"
    )

    assert response.status_code == 200

    body = response.json()

    assert body["mediaIds"] == []


def test_deleted_post_with_media_returns_404(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    media_id = upload_image(
        client,
        authenticated_user,
        file_name="deleted-post.jpg",
    )

    created = create_post(
        client,
        authenticated_user,
        media_ids=[
            media_id,
        ],
    )

    post_id = created["id"]

    delete_response = client.delete(
        f"/api/v1/posts/{post_id}",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert delete_response.status_code == 204

    response = client.get(
        f"/api/v1/posts/{post_id}"
    )

    assert response.status_code == 404