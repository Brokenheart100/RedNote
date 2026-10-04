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
            "content":
                f"Content for {title}",
        },
    )

    assert response.status_code == 201

    return response.json()


def test_author_can_delete_post(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    created = create_post(
        client,
        authenticated_user,
        title="Delete Test",
    )

    post_id = created["id"]

    response = client.delete(
        f"/api/v1/posts/{post_id}",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert response.status_code == 204

    get_response = client.get(
        f"/api/v1/posts/{post_id}"
    )

    assert get_response.status_code == 404


def test_other_user_cannot_delete_post(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
    create_user,
) -> None:
    author = authenticated_user

    other_user = create_user(
        prefix="post_delete_other",
        display_name="Post Delete Other",
    )

    created = create_post(
        client,
        author,
        title="Permission Test",
    )

    post_id = created["id"]

    response = client.delete(
        f"/api/v1/posts/{post_id}",
        headers=bearer_headers(
            other_user
        ),
    )

    assert response.status_code == 403

    get_response = client.get(
        f"/api/v1/posts/{post_id}"
    )

    assert get_response.status_code == 200


def test_delete_missing_post_returns_404(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    response = client.delete(
        (
            "/api/v1/posts/"
            "11111111-1111-1111-1111-111111111111"
        ),
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert response.status_code == 404


def test_user_posts_returns_only_published_posts(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    user_id = get_user_id(
        client,
        authenticated_user,
    )

    first = create_post(
        client,
        authenticated_user,
        title="Published Post 1",
    )

    second = create_post(
        client,
        authenticated_user,
        title="Published Post 2",
    )

    delete_response = client.delete(
        f"/api/v1/posts/{second['id']}",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert delete_response.status_code == 204

    response = client.get(
        "/api/v1/posts",
        params={
            "authorUserId": user_id,
            "page": 1,
            "pageSize": 20,
        },
    )

    assert response.status_code == 200

    body = response.json()

    ids = {
        item["id"]
        for item in body["items"]
    }

    assert first["id"] in ids
    assert second["id"] not in ids


def test_user_posts_pagination(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    user_id = get_user_id(
        client,
        authenticated_user,
    )

    create_post(
        client,
        authenticated_user,
        title="Pagination Post 1",
    )

    create_post(
        client,
        authenticated_user,
        title="Pagination Post 2",
    )

    first_page = client.get(
        "/api/v1/posts",
        params={
            "authorUserId": user_id,
            "page": 1,
            "pageSize": 1,
        },
    )

    second_page = client.get(
        "/api/v1/posts",
        params={
            "authorUserId": user_id,
            "page": 2,
            "pageSize": 1,
        },
    )

    assert first_page.status_code == 200
    assert second_page.status_code == 200

    first_body = first_page.json()
    second_body = second_page.json()

    assert len(first_body["items"]) == 1
    assert len(second_body["items"]) == 1

    assert (
        first_body["items"][0]["id"]
        != second_body["items"][0]["id"]
    )


def test_user_posts_invalid_page_returns_400(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    user_id = get_user_id(
        client,
        authenticated_user,
    )

    response = client.get(
        "/api/v1/posts",
        params={
            "authorUserId": user_id,
            "page": 0,
            "pageSize": 20,
        },
    )

    assert response.status_code == 400


def test_user_posts_invalid_page_size_returns_400(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    user_id = get_user_id(
        client,
        authenticated_user,
    )

    response = client.get(
        "/api/v1/posts",
        params={
            "authorUserId": user_id,
            "page": 1,
            "pageSize": 101,
        },
    )

    assert response.status_code == 400