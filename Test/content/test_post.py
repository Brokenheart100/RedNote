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


def test_create_and_get_post(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    create_response = client.post(
        "/api/v1/posts",
        headers=bearer_headers(
            authenticated_user
        ),
        json={
            "title": "My first RedNote post",
            "content": "ContentService E2E test.",
        },
    )

    assert create_response.status_code == 201

    created = create_response.json()

    post_id = created.get("id")

    assert isinstance(post_id, str)
    assert post_id

    assert (
        created["title"]
        == "My first RedNote post"
    )

    assert (
        created["content"]
        == "ContentService E2E test."
    )

    assert created["authorUserId"]

    location = create_response.headers.get(
        "Location"
    )

    assert location == (
        f"/api/v1/posts/{post_id}"
    )

    get_response = client.get(
        f"/api/v1/posts/{post_id}"
    )

    assert get_response.status_code == 200

    post = get_response.json()

    assert post["id"] == post_id
    assert (
        post["authorUserId"]
        == created["authorUserId"]
    )
    assert (
        post["title"]
        == created["title"]
    )
    assert (
        post["content"]
        == created["content"]
    )


def test_create_post_without_token_returns_401(
    client: httpx.Client,
) -> None:
    response = client.post(
        "/api/v1/posts",
        json={
            "title": "Unauthorized",
            "content": "Should not be created.",
        },
    )

    assert response.status_code == 401


def test_get_missing_post_returns_404(
    client: httpx.Client,
) -> None:
    response = client.get(
        (
            "/api/v1/posts/"
            "11111111-1111-1111-1111-111111111111"
        )
    )

    assert response.status_code == 404


def test_create_post_validates_title(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    response = client.post(
        "/api/v1/posts",
        headers=bearer_headers(
            authenticated_user
        ),
        json={
            "title": "",
            "content": "Valid content.",
        },
    )

    assert response.status_code == 400


def test_create_post_validates_content(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    response = client.post(
        "/api/v1/posts",
        headers=bearer_headers(
            authenticated_user
        ),
        json={
            "title": "Valid title",
            "content": "",
        },
    )

    assert response.status_code == 400