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


def upload_image(
    client: httpx.Client,
    user: AuthenticatedUser,
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


def test_create_post_with_media(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    first_media_id = upload_image(
        client,
        authenticated_user,
        "first.jpg",
    )

    second_media_id = upload_image(
        client,
        authenticated_user,
        "second.jpg",
    )

    response = client.post(
        "/api/v1/posts",
        headers=bearer_headers(
            authenticated_user
        ),
        json={
            "title": "Post With Media",
            "content": "Media test",
            "mediaIds": [
                first_media_id,
                second_media_id,
            ],
        },
    )

    assert response.status_code == 201

    body = response.json()

    assert body["mediaIds"] == [
        first_media_id,
        second_media_id,
    ]


def test_create_post_without_media(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    response = client.post(
        "/api/v1/posts",
        headers=bearer_headers(
            authenticated_user
        ),
        json={
            "title": "No Media",
            "content": "Text only",
            "mediaIds": [],
        },
    )

    assert response.status_code == 201

    assert response.json()["mediaIds"] == []


def test_create_post_rejects_more_than_9_media(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    media_ids = [
        "11111111-1111-1111-1111-111111111111",
        "22222222-2222-2222-2222-222222222222",
        "33333333-3333-3333-3333-333333333333",
        "44444444-4444-4444-4444-444444444444",
        "55555555-5555-5555-5555-555555555555",
        "66666666-6666-6666-6666-666666666666",
        "77777777-7777-7777-7777-777777777777",
        "88888888-8888-8888-8888-888888888888",
        "99999999-9999-9999-9999-999999999999",
        "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
    ]

    response = client.post(
        "/api/v1/posts",
        headers=bearer_headers(
            authenticated_user
        ),
        json={
            "title": "Too Many Media",
            "content": "Validation",
            "mediaIds": media_ids,
        },
    )

    assert response.status_code == 400


def test_create_post_deduplicates_media_ids(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    media_id = upload_image(
        client,
        authenticated_user,
        "duplicate.jpg",
    )

    response = client.post(
        "/api/v1/posts",
        headers=bearer_headers(
            authenticated_user
        ),
        json={
            "title": "Duplicate Media",
            "content": "Validation",
            "mediaIds": [
                media_id,
                media_id,
            ],
        },
    )

    assert response.status_code == 201

    assert response.json()["mediaIds"] == [
        media_id
    ]