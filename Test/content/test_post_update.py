from __future__ import annotations

import sys
from datetime import datetime
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
    title: str = "Original Title",
    content: str = "Original Content",
) -> dict:
    response = client.post(
        "/api/v1/posts",
        headers=bearer_headers(user),
        json={
            "title": title,
            "content": content,
        },
    )

    assert response.status_code == 201

    return response.json()


def test_author_can_update_post(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    created = create_post(
        client,
        authenticated_user,
    )

    post_id = created["id"]

    response = client.patch(
        f"/api/v1/posts/{post_id}",
        headers=bearer_headers(
            authenticated_user
        ),
        json={
            "title": "Updated Title",
            "content": "Updated Content",
        },
    )

    assert response.status_code == 200

    updated = response.json()

    assert updated["id"] == post_id
    assert updated["title"] == "Updated Title"
    assert updated["content"] == "Updated Content"
    assert (
        updated["authorUserId"]
        == created["authorUserId"]
    )

    get_response = client.get(
        f"/api/v1/posts/{post_id}"
    )

    assert get_response.status_code == 200

    persisted = get_response.json()

    assert persisted["title"] == "Updated Title"
    assert persisted["content"] == "Updated Content"


def test_update_changes_updated_at(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    created = create_post(
        client,
        authenticated_user,
    )

    post_id = created["id"]

    created_at = datetime.fromisoformat(
        created["createdAtUtc"]
    )

    original_updated_at = datetime.fromisoformat(
        created["updatedAtUtc"]
    )

    response = client.patch(
        f"/api/v1/posts/{post_id}",
        headers=bearer_headers(
            authenticated_user
        ),
        json={
            "title": "Updated Timestamp",
            "content": "Timestamp Test",
        },
    )

    assert response.status_code == 200

    updated = response.json()

    updated_at = datetime.fromisoformat(
        updated["updatedAtUtc"]
    )

    assert original_updated_at == created_at
    assert updated_at > original_updated_at


def test_other_user_cannot_update_post(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
    create_user,
) -> None:
    author = authenticated_user

    other_user = create_user(
        prefix="post_update_other",
        display_name="Post Update Other",
    )

    created = create_post(
        client,
        author,
    )

    post_id = created["id"]

    response = client.patch(
        f"/api/v1/posts/{post_id}",
        headers=bearer_headers(
            other_user
        ),
        json={
            "title": "Unauthorized Update",
            "content": "Should not update.",
        },
    )

    assert response.status_code == 403

    get_response = client.get(
        f"/api/v1/posts/{post_id}"
    )

    assert get_response.status_code == 200

    post = get_response.json()

    assert post["title"] == "Original Title"
    assert post["content"] == "Original Content"


def test_update_missing_post_returns_404(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    response = client.patch(
        (
            "/api/v1/posts/"
            "11111111-1111-1111-1111-111111111111"
        ),
        headers=bearer_headers(
            authenticated_user
        ),
        json={
            "title": "Missing Post",
            "content": "Missing Post",
        },
    )

    assert response.status_code == 404


def test_deleted_post_cannot_be_updated(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    created = create_post(
        client,
        authenticated_user,
    )

    post_id = created["id"]

    delete_response = client.delete(
        f"/api/v1/posts/{post_id}",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert delete_response.status_code == 204

    update_response = client.patch(
        f"/api/v1/posts/{post_id}",
        headers=bearer_headers(
            authenticated_user
        ),
        json={
            "title": "Updated Deleted Post",
            "content": "Should not update.",
        },
    )

    assert update_response.status_code == 404


def test_update_without_token_returns_401(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    created = create_post(
        client,
        authenticated_user,
    )

    response = client.patch(
        f"/api/v1/posts/{created['id']}",
        json={
            "title": "Unauthorized",
            "content": "Unauthorized",
        },
    )

    assert response.status_code == 401


def test_update_rejects_empty_title(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    created = create_post(
        client,
        authenticated_user,
    )

    response = client.patch(
        f"/api/v1/posts/{created['id']}",
        headers=bearer_headers(
            authenticated_user
        ),
        json={
            "title": "",
            "content": "Valid Content",
        },
    )

    assert response.status_code == 400


def test_update_rejects_title_over_100_characters(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    created = create_post(
        client,
        authenticated_user,
    )

    response = client.patch(
        f"/api/v1/posts/{created['id']}",
        headers=bearer_headers(
            authenticated_user
        ),
        json={
            "title": "A" * 101,
            "content": "Valid Content",
        },
    )

    assert response.status_code == 400


def test_update_rejects_empty_content(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    created = create_post(
        client,
        authenticated_user,
    )

    response = client.patch(
        f"/api/v1/posts/{created['id']}",
        headers=bearer_headers(
            authenticated_user
        ),
        json={
            "title": "Valid Title",
            "content": "",
        },
    )

    assert response.status_code == 400


def test_update_rejects_content_over_5000_characters(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    created = create_post(
        client,
        authenticated_user,
    )

    response = client.patch(
        f"/api/v1/posts/{created['id']}",
        headers=bearer_headers(
            authenticated_user
        ),
        json={
            "title": "Valid Title",
            "content": "A" * 5001,
        },
    )

    assert response.status_code == 400