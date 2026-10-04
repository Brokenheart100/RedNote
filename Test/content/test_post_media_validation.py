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
) -> httpx.Response:
    return client.post(
        "/api/v1/posts",
        headers=bearer_headers(user),
        json={
            "title": "Media Validation Post",
            "content": "Media validation test.",
            "mediaIds": media_ids,
        },
    )


def test_create_post_accepts_owned_media(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    media_id = upload_image(
        client,
        authenticated_user,
        file_name="owned.jpg",
    )

    response = create_post(
        client,
        authenticated_user,
        media_ids=[
            media_id,
        ],
    )

    assert response.status_code == 201

    body = response.json()

    assert body["mediaIds"] == [
        media_id
    ]


def test_create_post_rejects_missing_media(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    missing_media_id = (
        "11111111-1111-1111-1111-111111111111"
    )

    response = create_post(
        client,
        authenticated_user,
        media_ids=[
            missing_media_id,
        ],
    )

    assert response.status_code == 400

    body = response.json()

    assert "errors" in body
    assert "mediaIds" in body["errors"]


def test_create_post_rejects_other_users_media(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
    create_user,
) -> None:
    owner = authenticated_user

    other_user = create_user(
        prefix="post_media_other",
        display_name="Post Media Other",
    )

    media_id = upload_image(
        client,
        owner,
        file_name="other-user-media.jpg",
    )

    response = create_post(
        client,
        other_user,
        media_ids=[
            media_id,
        ],
    )

    assert response.status_code == 403


def test_create_post_accepts_multiple_owned_media(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    first_media_id = upload_image(
        client,
        authenticated_user,
        file_name="first-owned.jpg",
    )

    second_media_id = upload_image(
        client,
        authenticated_user,
        file_name="second-owned.jpg",
    )

    response = create_post(
        client,
        authenticated_user,
        media_ids=[
            first_media_id,
            second_media_id,
        ],
    )

    assert response.status_code == 201

    body = response.json()

    assert body["mediaIds"] == [
        first_media_id,
        second_media_id,
    ]


def test_create_post_deduplicates_media_before_validation(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    media_id = upload_image(
        client,
        authenticated_user,
        file_name="duplicate-owned.jpg",
    )

    response = create_post(
        client,
        authenticated_user,
        media_ids=[
            media_id,
            media_id,
            media_id,
        ],
    )

    assert response.status_code == 201

    assert response.json()["mediaIds"] == [
        media_id
    ]


def test_create_post_rejects_empty_media_id(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    response = create_post(
        client,
        authenticated_user,
        media_ids=[
            "00000000-0000-0000-0000-000000000000",
        ],
    )

    assert response.status_code == 400


def test_create_post_rejects_more_than_9_media(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    media_ids = [
        f"00000000-0000-0000-0000-{index:012d}"
        for index in range(1, 11)
    ]

    response = create_post(
        client,
        authenticated_user,
        media_ids=media_ids,
    )

    assert response.status_code == 400