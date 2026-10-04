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


def get_batch(
    client: httpx.Client,
    media_ids: list[str],
) -> httpx.Response:
    return client.post(
        "/api/v1/media/batch",
        json={
            "mediaIds": media_ids
        },
    )


def test_batch_returns_multiple_media(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    first_media_id = upload_image(
        client,
        authenticated_user,
        file_name="batch-first.jpg",
    )

    second_media_id = upload_image(
        client,
        authenticated_user,
        file_name="batch-second.jpg",
    )

    response = get_batch(
        client,
        [
            first_media_id,
            second_media_id,
        ],
    )

    assert response.status_code == 200

    body = response.json()

    assert len(body) == 2

    ids = {
        item["id"]
        for item in body
    }

    assert ids == {
        first_media_id,
        second_media_id,
    }


def test_batch_returns_media_metadata(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    media_id = upload_image(
        client,
        authenticated_user,
        file_name="batch-metadata.jpg",
    )

    response = get_batch(
        client,
        [media_id],
    )

    assert response.status_code == 200

    body = response.json()

    assert len(body) == 1

    item = body[0]

    assert item["id"] == media_id
    assert item["fileName"] == "batch-metadata.jpg"
    assert item["contentType"] == "image/jpeg"
    assert item["size"] == len(JPEG_BYTES)

    assert isinstance(
        item["ownerUserId"],
        str,
    )

    assert isinstance(
        item["createdAtUtc"],
        str,
    )

    assert isinstance(
        item["url"],
        str,
    )

    assert item["url"]


def test_batch_deduplicates_media_ids(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    media_id = upload_image(
        client,
        authenticated_user,
        file_name="batch-duplicate.jpg",
    )

    response = get_batch(
        client,
        [
            media_id,
            media_id,
            media_id,
        ],
    )

    assert response.status_code == 200

    body = response.json()

    assert len(body) == 1
    assert body[0]["id"] == media_id


def test_batch_ignores_missing_media(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    existing_media_id = upload_image(
        client,
        authenticated_user,
        file_name="batch-existing.jpg",
    )

    missing_media_id = (
        "11111111-1111-1111-1111-111111111111"
    )

    response = get_batch(
        client,
        [
            existing_media_id,
            missing_media_id,
        ],
    )

    assert response.status_code == 200

    body = response.json()

    assert len(body) == 1
    assert body[0]["id"] == existing_media_id


def test_batch_empty_media_ids_returns_empty_list(
    client: httpx.Client,
) -> None:
    response = get_batch(
        client,
        [],
    )

    assert response.status_code == 200
    assert response.json() == []


def test_batch_rejects_empty_guid(
    client: httpx.Client,
) -> None:
    response = get_batch(
        client,
        [
            "00000000-0000-0000-0000-000000000000"
        ],
    )

    assert response.status_code == 400


def test_batch_rejects_more_than_100_media(
    client: httpx.Client,
) -> None:
    media_ids = [
        (
            "00000000-0000-0000-0000-"
            f"{index:012d}"
        )
        for index in range(1, 102)
    ]

    response = get_batch(
        client,
        media_ids,
    )

    assert response.status_code == 400


def test_batch_presigned_url_can_download_image(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    media_id = upload_image(
        client,
        authenticated_user,
        file_name="batch-download.jpg",
    )

    response = get_batch(
        client,
        [media_id],
    )

    assert response.status_code == 200

    url = response.json()[0]["url"]

    with httpx.Client(
        timeout=10,
        follow_redirects=True,
    ) as public_client:
        download_response = public_client.get(
            url
        )

    assert download_response.status_code == 200
    assert download_response.content == JPEG_BYTES

    assert (
        download_response.headers
        .get("content-type", "")
        .startswith("image/jpeg")
    )