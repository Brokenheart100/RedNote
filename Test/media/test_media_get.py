from __future__ import annotations

import sys
from io import BytesIO
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


def upload_image(
    client: httpx.Client,
    user: AuthenticatedUser,
) -> dict:
    response = client.post(
        "/api/v1/media/images",
        headers=bearer_headers(user),
        files={
            "file": (
                "get-media-test.jpg",
                BytesIO(
                    b"\xff\xd8\xff\xd9"
                ),
                "image/jpeg",
            )
        },
    )

    assert response.status_code == 201

    return response.json()


def test_get_media_returns_metadata(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    uploaded = upload_image(
        client,
        authenticated_user,
    )

    media_id = uploaded["id"]

    response = client.get(
        f"/api/v1/media/{media_id}"
    )

    assert response.status_code == 200

    body = response.json()

    assert body["id"] == media_id

    assert (
        body["fileName"]
        == "get-media-test.jpg"
    )

    assert (
        body["contentType"]
        == "image/jpeg"
    )

    assert body["size"] == 4

    assert isinstance(
        body["ownerUserId"],
        str,
    )

    assert isinstance(
        body["createdAtUtc"],
        str,
    )

    assert isinstance(
        body["url"],
        str,
    )

    assert body["url"]


def test_get_media_presigned_url_looks_valid(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    uploaded = upload_image(
        client,
        authenticated_user,
    )

    response = client.get(
        f"/api/v1/media/{uploaded['id']}"
    )

    assert response.status_code == 200

    url = response.json()["url"]

    assert isinstance(
        url,
        str,
    )

    assert url.startswith(
        ("http://", "https://")
    )

    assert (
        "X-Amz-Signature" in url
        or "x-amz-signature" in url.lower()
    )


def test_get_missing_media_returns_404(
    client: httpx.Client,
) -> None:
    response = client.get(
        (
            "/api/v1/media/"
            "11111111-1111-1111-1111-111111111111"
        )
    )

    assert response.status_code == 404


def test_get_media_is_anonymous(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    uploaded = upload_image(
        client,
        authenticated_user,
    )

    response = client.get(
        f"/api/v1/media/{uploaded['id']}"
    )

    assert response.status_code == 200