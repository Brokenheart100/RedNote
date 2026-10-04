from __future__ import annotations

import sys
from collections.abc import Callable
from pathlib import Path

import httpx


PROJECT_ROOT = Path(__file__).resolve().parents[2]

if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))


from Test.support import AuthenticatedUser


UserFactory = Callable[..., AuthenticatedUser]


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


def follow(
    client: httpx.Client,
    follower: AuthenticatedUser,
    target_user_id: str,
) -> None:
    response = client.post(
        f"/api/v1/users/{target_user_id}/follow",
        headers=bearer_headers(
            follower
        ),
    )

    assert response.status_code == 204


def create_follow_graph(
    client: httpx.Client,
    create_user: UserFactory,
) -> tuple[
    AuthenticatedUser,
    AuthenticatedUser,
    AuthenticatedUser,
    str,
    str,
    str,
]:
    user_a = create_user(
        prefix="follow_list_a",
        display_name="Follow List A",
    )

    user_b = create_user(
        prefix="follow_list_b",
        display_name="Follow List B",
    )

    user_c = create_user(
        prefix="follow_list_c",
        display_name="Follow List C",
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

    follow(
        client,
        user_a,
        user_b_id,
    )

    follow(
        client,
        user_c,
        user_b_id,
    )

    return (
        user_a,
        user_b,
        user_c,
        user_a_id,
        user_b_id,
        user_c_id,
    )


def test_followers(
    client: httpx.Client,
    create_user: UserFactory,
) -> None:
    (
        _,
        _,
        _,
        user_a_id,
        user_b_id,
        user_c_id,
    ) = create_follow_graph(
        client,
        create_user,
    )

    response = client.get(
        f"/api/v1/users/{user_b_id}/followers",
        params={
            "page": 1,
            "pageSize": 20,
        },
    )

    assert response.status_code == 200

    body = response.json()

    assert body["page"] == 1
    assert body["pageSize"] == 20
    assert body["totalCount"] == 2

    actual_ids = {
        item["userId"]
        for item in body["items"]
    }

    assert actual_ids == {
        user_a_id,
        user_c_id,
    }


def test_following(
    client: httpx.Client,
    create_user: UserFactory,
) -> None:
    (
        _,
        _,
        _,
        user_a_id,
        user_b_id,
        _,
    ) = create_follow_graph(
        client,
        create_user,
    )

    response = client.get(
        f"/api/v1/users/{user_a_id}/following",
        params={
            "page": 1,
            "pageSize": 20,
        },
    )

    assert response.status_code == 200

    body = response.json()

    assert body["totalCount"] == 1

    assert {
        item["userId"]
        for item in body["items"]
    } == {
        user_b_id
    }


def test_empty_following(
    client: httpx.Client,
    create_user: UserFactory,
) -> None:
    user = create_user(
        prefix="empty_following",
        display_name="Empty Following",
    )

    user_id = get_user_id(
        client,
        user,
    )

    response = client.get(
        f"/api/v1/users/{user_id}/following",
        params={
            "page": 1,
            "pageSize": 20,
        },
    )

    assert response.status_code == 200

    body = response.json()

    assert body["totalCount"] == 0
    assert body["items"] == []


def test_followers_pagination(
    client: httpx.Client,
    create_user: UserFactory,
) -> None:
    (
        _,
        _,
        _,
        _,
        user_b_id,
        _,
    ) = create_follow_graph(
        client,
        create_user,
    )

    first = client.get(
        f"/api/v1/users/{user_b_id}/followers",
        params={
            "page": 1,
            "pageSize": 1,
        },
    )

    second = client.get(
        f"/api/v1/users/{user_b_id}/followers",
        params={
            "page": 2,
            "pageSize": 1,
        },
    )

    assert first.status_code == 200
    assert second.status_code == 200

    first_body = first.json()
    second_body = second.json()

    assert first_body["totalCount"] == 2
    assert second_body["totalCount"] == 2

    assert len(first_body["items"]) == 1
    assert len(second_body["items"]) == 1

    assert (
        first_body["items"][0]["userId"]
        !=
        second_body["items"][0]["userId"]
    )


def test_invalid_page(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    user_id = get_user_id(
        client,
        authenticated_user,
    )

    response = client.get(
        f"/api/v1/users/{user_id}/followers",
        params={
            "page": 0,
            "pageSize": 20,
        },
    )

    assert response.status_code == 400


def test_invalid_page_size(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    user_id = get_user_id(
        client,
        authenticated_user,
    )

    response = client.get(
        f"/api/v1/users/{user_id}/followers",
        params={
            "page": 1,
            "pageSize": 101,
        },
    )

    assert response.status_code == 400


def test_missing_user_returns_404(
    client: httpx.Client,
) -> None:
    response = client.get(
        (
            "/api/v1/users/"
            "11111111-1111-1111-1111-111111111111"
            "/followers"
        ),
        params={
            "page": 1,
            "pageSize": 20,
        },
    )

    assert response.status_code == 404


if __name__ == "__main__":
    import pytest

    raise SystemExit(
        pytest.main(
            [
                __file__,
                "-v",
            ]
        )
    )