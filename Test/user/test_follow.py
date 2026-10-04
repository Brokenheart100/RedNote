from __future__ import annotations

import httpx

from conftest import (
    AuthenticatedUser,
    create_authenticated_user,
)


def bearer_headers(
    user: AuthenticatedUser,
) -> dict[str, str]:
    return {
        "Authorization":
            f"Bearer {user.access_token}"
    }


def get_user_id(
    client: httpx.Client,
    user: AuthenticatedUser,
) -> str:
    response = client.get(
        "/api/v1/users/me",
        headers=bearer_headers(user),
    )

    assert response.status_code == 200

    user_id = response.json().get(
        "userId"
    )

    assert isinstance(user_id, str)

    return user_id


def test_follow_and_unfollow(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    user_a = authenticated_user

    user_b = create_authenticated_user(
        client,
        prefix="follow_b",
        display_name="Follow B",
    )

    user_b_id = get_user_id(
        client,
        user_b,
    )

    follow_response = client.post(
        (
            f"/api/v1/users/"
            f"{user_b_id}/follow"
        ),
        headers=bearer_headers(user_a),
    )

    assert follow_response.status_code == 204

    unfollow_response = client.delete(
        (
            f"/api/v1/users/"
            f"{user_b_id}/follow"
        ),
        headers=bearer_headers(user_a),
    )

    assert unfollow_response.status_code == 204


def test_duplicate_follow_is_idempotent(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    user_b = create_authenticated_user(
        client,
        prefix="duplicate_follow_b",
        display_name="Duplicate Follow B",
    )

    user_b_id = get_user_id(
        client,
        user_b,
    )

    url = (
        f"/api/v1/users/"
        f"{user_b_id}/follow"
    )

    first = client.post(
        url,
        headers=bearer_headers(
            authenticated_user
        ),
    )

    second = client.post(
        url,
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert first.status_code == 204
    assert second.status_code == 204


def test_duplicate_unfollow_is_idempotent(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    user_b = create_authenticated_user(
        client,
        prefix="duplicate_unfollow_b",
        display_name="Duplicate Unfollow B",
    )

    user_b_id = get_user_id(
        client,
        user_b,
    )

    url = (
        f"/api/v1/users/"
        f"{user_b_id}/follow"
    )

    client.post(
        url,
        headers=bearer_headers(
            authenticated_user
        ),
    )

    first = client.delete(
        url,
        headers=bearer_headers(
            authenticated_user
        ),
    )

    second = client.delete(
        url,
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert first.status_code == 204
    assert second.status_code == 204


def test_cannot_follow_self(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    user_id = get_user_id(
        client,
        authenticated_user,
    )

    response = client.post(
        (
            f"/api/v1/users/"
            f"{user_id}/follow"
        ),
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert response.status_code == 400


def test_follow_missing_user_returns_404(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    response = client.post(
        (
            "/api/v1/users/"
            "11111111-1111-1111-1111-111111111111"
            "/follow"
        ),
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert response.status_code == 404