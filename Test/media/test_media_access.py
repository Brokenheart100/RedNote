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
) -> dict:
    response = client.post(
        "/api/v1/media/images",
        headers=bearer_headers(user),
        files={
            "file": (
                "media-access-test.jpg",
                BytesIO(JPEG_BYTES),
                "image/jpeg",
            )
        },
    )

    assert response.status_code == 201

    return response.json()


def get_media(
    client: httpx.Client,
    media_id: str,
) -> dict:
    response = client.get(
        f"/api/v1/media/{media_id}"
    )

    assert response.status_code == 200

    return response.json()


def test_presigned_url_can_download_image(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    uploaded = upload_image(
        client,
        authenticated_user,
    )

    media = get_media(
        client,
        uploaded["id"],
    )

    url = media["url"]
    print("Presigned URL:", url)

    assert isinstance(url, str)
    assert url

    with httpx.Client(
        timeout=10,
        follow_redirects=True,
    ) as public_client:
        response = public_client.get(
            url
        )

    assert response.status_code == 200

    assert response.content == JPEG_BYTES

    assert (
        response.headers
        .get("content-type", "")
        .startswith("image/jpeg")
    )


def test_presigned_url_contains_signature(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    uploaded = upload_image(
        client,
        authenticated_user,
    )

    media = get_media(
        client,
        uploaded["id"],
    )

    url = media["url"]

    lowered_url = url.lower()

    assert "x-amz-signature=" in lowered_url
    assert "x-amz-expires=" in lowered_url
    assert "x-amz-credential=" in lowered_url


def test_private_object_is_not_exposed_by_media_api(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    uploaded = upload_image(    
        client,
        authenticated_user,
    )

    media = get_media(
        client,
        uploaded["id"],
    )

    assert "objectKey" not in media