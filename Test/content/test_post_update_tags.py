from __future__ import annotations

import sys
from pathlib import Path

import httpx


PROJECT_ROOT = Path(__file__).resolve().parents[2]

if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))


from Test.support import AuthenticatedUser


class _UnsetType:
    pass


_UNSET = _UnsetType()


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
    content: str,
    tags: list[str] | None,
) -> dict:
    response = client.post(
        "/api/v1/posts",
        headers=bearer_headers(user),
        json={
            "title": title,
            "content": content,
            "mediaIds": [],
            "tags": tags,
        },
    )

    assert response.status_code == 201

    return response.json()


def update_post(
    client: httpx.Client,
    user: AuthenticatedUser,
    *,
    post_id: str,
    title: str,
    content: str,
    tags: list[str] | None | _UnsetType = _UNSET,
) -> httpx.Response:
    body: dict[str, object] = {
        "title": title,
        "content": content,
    }

    if not isinstance(
        tags,
        _UnsetType,
    ):
        body["tags"] = tags

    return client.patch(
        f"/api/v1/posts/{post_id}",
        headers=bearer_headers(user),
        json=body,
    )


def get_post(
    client: httpx.Client,
    post_id: str,
) -> dict:
    response = client.get(
        f"/api/v1/posts/{post_id}"
    )

    assert response.status_code == 200

    return response.json()


def test_update_without_tags_preserves_existing_tags(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Preserve Tags",
        content="Original content",
        tags=[
            "aspire",
            "nuxt",
        ],
    )

    response = update_post(
        client,
        authenticated_user,
        post_id=post["id"],
        title="Preserve Tags Updated",
        content="Updated content",
    )

    assert response.status_code == 200

    body = response.json()

    assert set(body["tags"]) == {
        "aspire",
        "nuxt",
    }

    persisted = get_post(
        client,
        post["id"],
    )

    assert set(persisted["tags"]) == {
        "aspire",
        "nuxt",
    }


def test_update_with_null_tags_preserves_existing_tags(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Null Tags",
        content="Original",
        tags=[
            "dotnet",
            "postgres",
        ],
    )

    response = update_post(
        client,
        authenticated_user,
        post_id=post["id"],
        title="Null Tags Updated",
        content="Updated",
        tags=None,
    )

    assert response.status_code == 200

    body = response.json()

    assert set(body["tags"]) == {
        "dotnet",
        "postgres",
    }

    persisted = get_post(
        client,
        post["id"],
    )

    assert set(persisted["tags"]) == {
        "dotnet",
        "postgres",
    }


def test_update_replaces_tags(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Replace Tags",
        content="Original",
        tags=[
            "old-one",
            "old-two",
        ],
    )

    response = update_post(
        client,
        authenticated_user,
        post_id=post["id"],
        title="Replace Tags Updated",
        content="Updated",
        tags=[
            "new-one",
            "new-two",
        ],
    )

    assert response.status_code == 200

    body = response.json()

    assert set(body["tags"]) == {
        "new-one",
        "new-two",
    }

    persisted = get_post(
        client,
        post["id"],
    )

    assert set(persisted["tags"]) == {
        "new-one",
        "new-two",
    }


def test_update_can_clear_tags(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Clear Tags",
        content="Original",
        tags=[
            "aspire",
            "nuxt",
        ],
    )

    response = update_post(
        client,
        authenticated_user,
        post_id=post["id"],
        title="Clear Tags Updated",
        content="Updated",
        tags=[],
    )

    assert response.status_code == 200

    assert response.json()["tags"] == []

    persisted = get_post(
        client,
        post["id"],
    )

    assert persisted["tags"] == []


def test_update_tags_are_trimmed(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Trim Update Tags",
        content="Original",
        tags=[],
    )

    response = update_post(
        client,
        authenticated_user,
        post_id=post["id"],
        title="Trim Update Tags Updated",
        content="Updated",
        tags=[
            "  aspire  ",
            " nuxt ",
        ],
    )

    assert response.status_code == 200

    assert set(
        response.json()["tags"]
    ) == {
        "aspire",
        "nuxt",
    }

    persisted = get_post(
        client,
        post["id"],
    )

    assert set(
        persisted["tags"]
    ) == {
        "aspire",
        "nuxt",
    }


def test_update_tags_are_deduplicated_case_insensitively(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Deduplicate Update Tags",
        content="Original",
        tags=[],
    )

    response = update_post(
        client,
        authenticated_user,
        post_id=post["id"],
        title="Deduplicate Update Tags Updated",
        content="Updated",
        tags=[
            "Nuxt",
            "nuxt",
            " NUXT ",
            "Aspire",
        ],
    )

    assert response.status_code == 200

    tags = response.json()["tags"]

    assert len(tags) == 2

    assert {
        tag.lower()
        for tag in tags
    } == {
        "nuxt",
        "aspire",
    }


def test_update_rejects_empty_tag(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Empty Update Tag",
        content="Original",
        tags=[
            "original",
        ],
    )

    response = update_post(
        client,
        authenticated_user,
        post_id=post["id"],
        title="Empty Update Tag Updated",
        content="Updated",
        tags=[
            "aspire",
            "   ",
        ],
    )

    assert response.status_code == 400

    persisted = get_post(
        client,
        post["id"],
    )

    assert persisted["title"] == \
        "Empty Update Tag"

    assert persisted["content"] == \
        "Original"

    assert persisted["tags"] == [
        "original"
    ]


def test_update_rejects_tag_longer_than_30_characters(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Long Update Tag",
        content="Original",
        tags=[],
    )

    response = update_post(
        client,
        authenticated_user,
        post_id=post["id"],
        title="Long Update Tag Updated",
        content="Updated",
        tags=[
            "x" * 31,
        ],
    )

    assert response.status_code == 400

    persisted = get_post(
        client,
        post["id"],
    )

    assert persisted["tags"] == []


def test_update_allows_30_character_tag(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Max Update Tag",
        content="Original",
        tags=[],
    )

    tag = "x" * 30

    response = update_post(
        client,
        authenticated_user,
        post_id=post["id"],
        title="Max Update Tag Updated",
        content="Updated",
        tags=[
            tag,
        ],
    )

    assert response.status_code == 200

    assert response.json()["tags"] == [
        tag
    ]

    persisted = get_post(
        client,
        post["id"],
    )

    assert persisted["tags"] == [
        tag
    ]


def test_update_rejects_more_than_10_unique_tags(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Too Many Update Tags",
        content="Original",
        tags=[],
    )

    tags = [
        f"tag-{index}"
        for index in range(11)
    ]

    response = update_post(
        client,
        authenticated_user,
        post_id=post["id"],
        title="Too Many Update Tags Updated",
        content="Updated",
        tags=tags,
    )

    assert response.status_code == 400

    persisted = get_post(
        client,
        post["id"],
    )

    assert persisted["tags"] == []


def test_duplicate_tags_do_not_count_toward_update_limit(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Duplicate Update Limit",
        content="Original",
        tags=[],
    )

    tags = [
        "TagOne",
        "tagone",
        " TAGONE ",
    ]

    tags.extend(
        f"tag-{index}"
        for index in range(9)
    )

    response = update_post(
        client,
        authenticated_user,
        post_id=post["id"],
        title="Duplicate Update Limit Updated",
        content="Updated",
        tags=tags,
    )

    assert response.status_code == 200

    assert len(
        response.json()["tags"]
    ) == 10


def test_update_tags_preserves_like_and_comment_count(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Interaction Preserve Tags",
        content="Original",
        tags=[
            "original",
        ],
    )

    like_response = client.post(
        f"/api/v1/posts/{post['id']}/likes",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert like_response.status_code == 204

    comment_response = client.post(
        f"/api/v1/posts/{post['id']}/comments",
        headers=bearer_headers(
            authenticated_user
        ),
        json={
            "content":
                "Existing comment",
            "parentCommentId":
                None,
        },
    )

    assert comment_response.status_code == 201

    response = update_post(
        client,
        authenticated_user,
        post_id=post["id"],
        title="Interaction Preserve Tags Updated",
        content="Updated",
        tags=[
            "updated",
        ],
    )

    assert response.status_code == 200

    body = response.json()

    assert body["likeCount"] == 1
    assert body["commentCount"] == 1
    assert body["isLiked"] is True


def test_update_tags_preserves_favorite_state(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Favorite Preserve Tags",
        content="Original",
        tags=[
            "original",
        ],
    )

    favorite_response = client.post(
        f"/api/v1/posts/{post['id']}/favorites",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert favorite_response.status_code == 204

    response = update_post(
        client,
        authenticated_user,
        post_id=post["id"],
        title="Favorite Preserve Tags Updated",
        content="Updated",
        tags=[
            "updated",
        ],
    )

    assert response.status_code == 200

    assert response.json()["isFavorited"] is True


def test_other_user_cannot_update_tags(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
    create_user,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Protected Tags",
        content="Original",
        tags=[
            "original",
        ],
    )

    other_user = create_user(
        prefix="update_tag_other",
        display_name="Update Tag Other",
    )

    response = update_post(
        client,
        other_user,
        post_id=post["id"],
        title="Hacked Title",
        content="Hacked Content",
        tags=[
            "hacked",
        ],
    )

    assert response.status_code == 403

    persisted = get_post(
        client,
        post["id"],
    )

    assert persisted["title"] == \
        "Protected Tags"

    assert persisted["content"] == \
        "Original"

    assert persisted["tags"] == [
        "original"
    ]


def test_update_tags_requires_authentication(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Auth Required Tags",
        content="Original",
        tags=[
            "original",
        ],
    )

    response = client.patch(
        f"/api/v1/posts/{post['id']}",
        json={
            "title":
                "Unauthorized Update",
            "content":
                "Unauthorized content",
            "tags": [
                "unauthorized"
            ],
        },
    )

    assert response.status_code == 401


def test_update_missing_post_tags_returns_not_found(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    missing_post_id = (
        "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
    )

    response = update_post(
        client,
        authenticated_user,
        post_id=missing_post_id,
        title="Missing Post",
        content="Missing content",
        tags=[
            "missing",
        ],
    )

    assert response.status_code == 404


def test_update_deleted_post_tags_returns_not_found(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Deleted Update Tags",
        content="Original",
        tags=[
            "original",
        ],
    )

    delete_response = client.delete(
        f"/api/v1/posts/{post['id']}",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert delete_response.status_code == 204

    response = update_post(
        client,
        authenticated_user,
        post_id=post["id"],
        title="Deleted Update",
        content="Updated",
        tags=[
            "new-tag",
        ],
    )

    assert response.status_code == 404