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


def create_post(
    client: httpx.Client,
    user: AuthenticatedUser,
    *,
    title: str,
) -> dict:
    response = client.post(
        "/api/v1/posts",
        headers=bearer_headers(user),
        json={
            "title": title,
            "content": f"Content for {title}",
            "mediaIds": [],
        },
    )

    assert response.status_code == 201

    return response.json()


def test_favorite_post_succeeds(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Favorite Post",
    )

    response = client.post(
        f"/api/v1/posts/{post['id']}/favorites",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert response.status_code == 204


def test_favorite_post_is_idempotent(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Favorite Idempotent",
    )

    first_response = client.post(
        f"/api/v1/posts/{post['id']}/favorites",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    second_response = client.post(
        f"/api/v1/posts/{post['id']}/favorites",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert first_response.status_code == 204
    assert second_response.status_code == 204


def test_unfavorite_post_succeeds(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Unfavorite Post",
    )

    favorite_response = client.post(
        f"/api/v1/posts/{post['id']}/favorites",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert favorite_response.status_code == 204

    unfavorite_response = client.delete(
        f"/api/v1/posts/{post['id']}/favorites",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert unfavorite_response.status_code == 204


def test_unfavorite_post_is_idempotent(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Unfavorite Idempotent",
    )

    first_response = client.delete(
        f"/api/v1/posts/{post['id']}/favorites",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    second_response = client.delete(
        f"/api/v1/posts/{post['id']}/favorites",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert first_response.status_code == 204
    assert second_response.status_code == 204


def test_favorite_missing_post_returns_404(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    response = client.post(
        (
            "/api/v1/posts/"
            "11111111-1111-1111-1111-111111111111"
            "/favorites"
        ),
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert response.status_code == 404


def test_favorite_deleted_post_returns_404(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Deleted Favorite Post",
    )

    delete_response = client.delete(
        f"/api/v1/posts/{post['id']}",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert delete_response.status_code == 204

    favorite_response = client.post(
        f"/api/v1/posts/{post['id']}/favorites",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert favorite_response.status_code == 404


def test_favorite_requires_authentication(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Favorite Authentication",
    )

    response = client.post(
        f"/api/v1/posts/{post['id']}/favorites"
    )

    assert response.status_code == 401


def test_unfavorite_requires_authentication(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Unfavorite Authentication",
    )

    response = client.delete(
        f"/api/v1/posts/{post['id']}/favorites"
    )

    assert response.status_code == 401