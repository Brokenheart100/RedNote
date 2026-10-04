from __future__ import annotations

import httpx

from Test.support import AuthenticatedUser


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

    user_id = response.json()["userId"]

    assert isinstance(user_id, str)

    return user_id


def test_profile_social(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
    create_user,
) -> None:
    user_a = authenticated_user

    user_b = create_user(
        prefix="profile_social_b",
        display_name="Profile Social B",
    )

    user_c = create_user(
        prefix="profile_social_c",
        display_name="Profile Social C",
    )

    user_a_id = get_user_id(
        client,
        user_a,
    )

    user_b_id = get_user_id(
        client,
        user_b,
    )

    user_c_id = get_user_id(
        client,
        user_c,
    )

    # A -> B
    response = client.post(
        f"/api/v1/users/{user_b_id}/follow",
        headers=bearer_headers(user_a),
    )

    assert response.status_code == 204

    # C -> B
    response = client.post(
        f"/api/v1/users/{user_b_id}/follow",
        headers=bearer_headers(user_c),
    )

    assert response.status_code == 204

    # A -> C
    response = client.post(
        f"/api/v1/users/{user_c_id}/follow",
        headers=bearer_headers(user_a),
    )

    assert response.status_code == 204

    # A 查看 B
    response = client.get(
        f"/api/v1/users/{user_b_id}",
        headers=bearer_headers(user_a),
    )

    assert response.status_code == 200

    profile_b = response.json()

    assert profile_b["followersCount"] == 2
    assert profile_b["followingCount"] == 0
    assert profile_b["isFollowing"] is True

    # A /me
    response = client.get(
        "/api/v1/users/me",
        headers=bearer_headers(user_a),
    )

    assert response.status_code == 200

    profile_a = response.json()

    assert profile_a["followingCount"] == 2
    assert profile_a["isFollowing"] is False

    # C 查看 A
    response = client.get(
        f"/api/v1/users/{user_a_id}",
        headers=bearer_headers(user_c),
    )

    assert response.status_code == 200
    assert response.json()["isFollowing"] is False

    # 匿名查看 B
    response = client.get(
        f"/api/v1/users/{user_b_id}"
    )

    assert response.status_code == 200
    assert response.json()["isFollowing"] is False

    # A unfollow B
    response = client.delete(
        f"/api/v1/users/{user_b_id}/follow",
        headers=bearer_headers(user_a),
    )

    assert response.status_code == 204

    response = client.get(
        f"/api/v1/users/{user_b_id}",
        headers=bearer_headers(user_a),
    )

    assert response.status_code == 200

    profile_b = response.json()

    assert profile_b["followersCount"] == 1
    assert profile_b["isFollowing"] is False