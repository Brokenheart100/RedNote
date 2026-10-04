from __future__ import annotations

import httpx

from conftest import (
    AuthenticatedUser,
)


def test_get_me(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    response = client.get(
        "/api/v1/users/me",
        headers={
            "Authorization":
                f"Bearer "
                f"{authenticated_user.access_token}",
        },
    )

    assert response.status_code == 200

    profile = response.json()

    assert profile["userId"]
    assert "followersCount" in profile
    assert "followingCount" in profile


def test_update_me(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    response = client.patch(
        "/api/v1/users/me",
        headers={
            "Authorization":
                f"Bearer "
                f"{authenticated_user.access_token}",
        },
        json={
            "nickname":
                "RedNote Pytest User",
            "avatarUrl":
                "https://example.com/avatar.jpg",
            "bio":
                "pytest profile test",
        },
    )

    assert response.status_code == 200

    profile = response.json()

    assert (
        profile["nickname"]
        == "RedNote Pytest User"
    )

    assert (
        profile["bio"]
        == "pytest profile test"
    )


def test_get_public_profile(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    me_response = client.get(
        "/api/v1/users/me",
        headers={
            "Authorization":
                f"Bearer "
                f"{authenticated_user.access_token}",
        },
    )

    assert me_response.status_code == 200

    user_id = me_response.json()["userId"]

    response = client.get(
        f"/api/v1/users/{user_id}"
    )

    assert response.status_code == 200

    profile = response.json()

    assert profile["userId"] == user_id
    assert profile["isFollowing"] is False


def test_get_me_without_token(
    client: httpx.Client,
) -> None:
    response = client.get(
        "/api/v1/users/me"
    )

    assert response.status_code == 401