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


def test_upload_jpeg_image(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    response = client.post(
        "/api/v1/media/images",
        headers=bearer_headers(
            authenticated_user
        ),
        files={
            "file": (
                "test.jpg",
                BytesIO(JPEG_BYTES),
                "image/jpeg",
            )
        },
    )

    assert response.status_code == 201

    body = response.json()

    assert isinstance(
        body["id"],
        str,
    )

    assert body["fileName"] == "test.jpg"
    assert body["contentType"] == "image/jpeg"
    assert body["size"] == len(JPEG_BYTES)

    assert body["objectKey"].startswith(
        "images/"
    )

    assert body["objectKey"].endswith(
        ".jpg"
    )


def test_upload_without_token_returns_401(
    client: httpx.Client,
) -> None:
    response = client.post(
        "/api/v1/media/images",
        files={
            "file": (
                "test.jpg",
                BytesIO(JPEG_BYTES),
                "image/jpeg",
            )
        },
    )
    
    assert response.status_code == 401


def test_upload_rejects_unsupported_content_type(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    response = client.post(
        "/api/v1/media/images",
        headers=bearer_headers(
            authenticated_user
        ),
        files={
            "file": (
                "test.gif",
                BytesIO(b"GIF89a"),
                "image/gif",
            )
        },
    )

    assert response.status_code == 400


def test_upload_rejects_empty_file(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    response = client.post(
        "/api/v1/media/images",
        headers=bearer_headers(
            authenticated_user
        ),
        files={
            "file": (
                "empty.jpg",
                BytesIO(b""),
                "image/jpeg",
            )
        },
    )

    assert response.status_code == 400