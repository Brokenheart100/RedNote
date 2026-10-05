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
    *,
    file_name: str,
) -> dict:
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

    return response.json()


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


def test_get_post_returns_media_details(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    uploaded = upload_image(
        client,
        authenticated_user,
        file_name="post-details.jpg",
    )

    created = create_post(
        client,
        authenticated_user,
        title="Post Media Details",
        media_ids=[
            uploaded["id"],
        ],
    )

    response = client.get(
        f"/api/v1/posts/{created['id']}"
    )

    assert response.status_code == 200

    body = response.json()

    assert body["mediaIds"] == [
        uploaded["id"]
    ]

    assert len(body["media"]) == 1

    media = body["media"][0]

    assert media["id"] == uploaded["id"]
    assert media["fileName"] == "post-details.jpg"
    assert media["contentType"] == "image/jpeg"
    assert media["size"] == len(JPEG_BYTES)

    assert isinstance(
        media["url"],
        str,
    )

    assert media["url"]


def test_get_post_media_preserves_order(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    first = upload_image(
        client,
        authenticated_user,
        file_name="details-first.jpg",
    )

    second = upload_image(
        client,
        authenticated_user,
        file_name="details-second.jpg",
    )

    third = upload_image(
        client,
        authenticated_user,
        file_name="details-third.jpg",
    )

    expected_ids = [
        third["id"],
        first["id"],
        second["id"],
    ]

    created = create_post(
        client,
        authenticated_user,
        title="Post Media Detail Order",
        media_ids=expected_ids,
    )

    response = client.get(
        f"/api/v1/posts/{created['id']}"
    )

    assert response.status_code == 200

    body = response.json()

    actual_ids = [
        media["id"]
        for media in body["media"]
    ]

    assert actual_ids == expected_ids


def test_post_media_url_can_download_image(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    uploaded = upload_image(
        client,
        authenticated_user,
        file_name="post-download.jpg",
    )

    created = create_post(
        client,
        authenticated_user,
        title="Post Media Download",
        media_ids=[
            uploaded["id"],
        ],
    )

    response = client.get(
        f"/api/v1/posts/{created['id']}"
    )

    assert response.status_code == 200

    url = response.json()["media"][0]["url"]

    with httpx.Client(
        timeout=10,
        follow_redirects=True,
    ) as public_client:
        download_response = public_client.get(
            url
        )

    assert download_response.status_code == 200

    assert (
        download_response.content
        == JPEG_BYTES
    )

    assert (
        download_response.headers
        .get("content-type", "")
        .startswith("image/jpeg")
    )


def test_post_list_returns_media_details(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    user_id = get_user_id(
        client,
        authenticated_user,
    )

    first = upload_image(
        client,
        authenticated_user,
        file_name="list-details-first.jpg",
    )

    second = upload_image(
        client,
        authenticated_user,
        file_name="list-details-second.jpg",
    )

    created = create_post(
        client,
        authenticated_user,
        title="List Media Details",
        media_ids=[
            first["id"],
            second["id"],
        ],
    )

    response = client.get(
        "/api/v1/posts",
        params={
            "authorUserId":
                user_id,
            "page":
                1,
            "pageSize":
                100,
        },
    )

    assert response.status_code == 200

    body = response.json()

    post = next(
        item
        for item in body["items"]
        if item["id"] == created["id"]
    )

    assert post["mediaIds"] == [
        first["id"],
        second["id"],
    ]

    assert [
        media["id"]
        for media in post["media"]
    ] == [
        first["id"],
        second["id"],
    ]

    assert (
        post["media"][0]["fileName"]
        == "list-details-first.jpg"
    )

    assert (
        post["media"][1]["fileName"]
        == "list-details-second.jpg"
    )


def test_post_without_media_returns_empty_media(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    created = create_post(
        client,
        authenticated_user,
        title="Post Without Media Details",
        media_ids=[],
    )

    response = client.get(
        f"/api/v1/posts/{created['id']}"
    )

    assert response.status_code == 200

    body = response.json()

    assert body["mediaIds"] == []
    assert body["media"] == []