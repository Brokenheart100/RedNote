from __future__ import annotations

import sys
import time
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

    return response.json()["userId"]


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


def follow_user(
    client: httpx.Client,
    user: AuthenticatedUser,
    target_user_id: str,
) -> None:
    response = client.post(
        f"/api/v1/users/{target_user_id}/follow",
        headers=bearer_headers(user),
    )

    assert response.status_code in {
        200,
        204,
    }


def unfollow_user(
    client: httpx.Client,
    user: AuthenticatedUser,
    target_user_id: str,
) -> None:
    response = client.delete(
        f"/api/v1/users/{target_user_id}/follow",
        headers=bearer_headers(user),
    )

    assert response.status_code in {
        200,
        204,
    }


def get_following_feed(
    client: httpx.Client,
    *,
    user: AuthenticatedUser | None = None,
    page: int = 1,
    page_size: int = 20,
) -> httpx.Response:
    headers = (
        bearer_headers(user)
        if user is not None
        else None
    )

    return client.get(
        "/api/v1/posts/feed/following",
        headers=headers,
        params={
            "page": page,
            "pageSize": page_size,
        },
    )


def test_following_feed_requires_authentication(
    client: httpx.Client,
) -> None:
    response = get_following_feed(
        client,
    )

    assert response.status_code == 401


def test_following_feed_returns_followed_users_posts(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
    create_user,
) -> None:
    followed_user = create_user(
        prefix="following_feed_followed",
        display_name="Following Feed Followed",
    )

    followed_user_id = get_user_id(
        client,
        followed_user,
    )

    post = create_post(
        client,
        followed_user,
        title="Following Feed Visible",
    )

    follow_user(
        client,
        authenticated_user,
        followed_user_id,
    )

    response = get_following_feed(
        client,
        user=authenticated_user,
        page=1,
        page_size=100,
    )

    assert response.status_code == 200

    ids = {
        item["id"]
        for item in response.json()["items"]
    }

    assert post["id"] in ids


def test_following_feed_excludes_unfollowed_users_posts(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
    create_user,
) -> None:
    followed_user = create_user(
        prefix="following_feed_included",
        display_name="Following Feed Included",
    )

    unrelated_user = create_user(
        prefix="following_feed_unrelated",
        display_name="Following Feed Unrelated",
    )

    followed_user_id = get_user_id(
        client,
        followed_user,
    )

    followed_post = create_post(
        client,
        followed_user,
        title="Following Included",
    )

    unrelated_post = create_post(
        client,
        unrelated_user,
        title="Following Excluded",
    )

    follow_user(
        client,
        authenticated_user,
        followed_user_id,
    )

    response = get_following_feed(
        client,
        user=authenticated_user,
        page=1,
        page_size=100,
    )

    assert response.status_code == 200

    ids = {
        item["id"]
        for item in response.json()["items"]
    }

    assert followed_post["id"] in ids
    assert unrelated_post["id"] not in ids


def test_own_post_is_not_in_following_feed_by_default(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Own Following Feed Post",
    )

    response = get_following_feed(
        client,
        user=authenticated_user,
        page=1,
        page_size=100,
    )

    assert response.status_code == 200

    ids = {
        item["id"]
        for item in response.json()["items"]
    }

    assert post["id"] not in ids


def test_unfollow_removes_posts_from_following_feed(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
    create_user,
) -> None:
    followed_user = create_user(
        prefix="following_feed_unfollow",
        display_name="Following Feed Unfollow",
    )

    followed_user_id = get_user_id(
        client,
        followed_user,
    )

    post = create_post(
        client,
        followed_user,
        title="Following Feed Unfollow Post",
    )

    follow_user(
        client,
        authenticated_user,
        followed_user_id,
    )

    before_response = get_following_feed(
        client,
        user=authenticated_user,
        page=1,
        page_size=100,
    )

    assert before_response.status_code == 200

    before_ids = {
        item["id"]
        for item in before_response.json()["items"]
    }

    assert post["id"] in before_ids

    unfollow_user(
        client,
        authenticated_user,
        followed_user_id,
    )

    after_response = get_following_feed(
        client,
        user=authenticated_user,
        page=1,
        page_size=100,
    )

    assert after_response.status_code == 200

    after_ids = {
        item["id"]
        for item in after_response.json()["items"]
    }

    assert post["id"] not in after_ids


def test_deleted_post_is_not_in_following_feed(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
    create_user,
) -> None:
    followed_user = create_user(
        prefix="following_feed_deleted",
        display_name="Following Feed Deleted",
    )

    followed_user_id = get_user_id(
        client,
        followed_user,
    )

    post = create_post(
        client,
        followed_user,
        title="Following Feed Deleted Post",
    )

    follow_user(
        client,
        authenticated_user,
        followed_user_id,
    )

    delete_response = client.delete(
        f"/api/v1/posts/{post['id']}",
        headers=bearer_headers(
            followed_user
        ),
    )

    assert delete_response.status_code == 204

    response = get_following_feed(
        client,
        user=authenticated_user,
        page=1,
        page_size=100,
    )

    assert response.status_code == 200

    ids = {
        item["id"]
        for item in response.json()["items"]
    }

    assert post["id"] not in ids


def test_following_feed_orders_by_created_at_desc(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
    create_user,
) -> None:
    followed_user = create_user(
        prefix="following_feed_order",
        display_name="Following Feed Order",
    )

    followed_user_id = get_user_id(
        client,
        followed_user,
    )

    follow_user(
        client,
        authenticated_user,
        followed_user_id,
    )

    first_post = create_post(
        client,
        followed_user,
        title="Following Order First",
    )

    time.sleep(0.01)

    second_post = create_post(
        client,
        followed_user,
        title="Following Order Second",
    )

    response = get_following_feed(
        client,
        user=authenticated_user,
        page=1,
        page_size=100,
    )

    assert response.status_code == 200

    items = response.json()["items"]

    indexes = {
        item["id"]: index
        for index, item in enumerate(items)
    }

    assert first_post["id"] in indexes
    assert second_post["id"] in indexes

    assert (
        indexes[second_post["id"]]
        <
        indexes[first_post["id"]]
    )


def test_following_feed_returns_interaction_state(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
    create_user,
) -> None:
    followed_user = create_user(
        prefix="following_feed_state",
        display_name="Following Feed State",
    )

    followed_user_id = get_user_id(
        client,
        followed_user,
    )

    follow_user(
        client,
        authenticated_user,
        followed_user_id,
    )

    post = create_post(
        client,
        followed_user,
        title="Following Feed State Post",
    )

    like_response = client.post(
        f"/api/v1/posts/{post['id']}/likes",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    favorite_response = client.post(
        f"/api/v1/posts/{post['id']}/favorites",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    comment_response = client.post(
        f"/api/v1/posts/{post['id']}/comments",
        headers=bearer_headers(
            authenticated_user
        ),
        json={
            "content":
                "Following feed comment",
            "parentCommentId":
                None,
        },
    )

    assert like_response.status_code == 204
    assert favorite_response.status_code == 204
    assert comment_response.status_code == 201

    response = get_following_feed(
        client,
        user=authenticated_user,
        page=1,
        page_size=100,
    )

    assert response.status_code == 200

    item = next(
        item
        for item in response.json()["items"]
        if item["id"] == post["id"]
    )

    assert item["likeCount"] == 1
    assert item["commentCount"] == 1
    assert item["isLiked"] is True
    assert item["isFavorited"] is True


def test_following_feed_pagination(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
    create_user,
) -> None:
    followed_user = create_user(
        prefix="following_feed_paging",
        display_name="Following Feed Paging",
    )

    followed_user_id = get_user_id(
        client,
        followed_user,
    )

    follow_user(
        client,
        authenticated_user,
        followed_user_id,
    )

    created_ids: set[str] = set()

    for index in range(3):
        post = create_post(
            client,
            followed_user,
            title=f"Following Feed Page {index}",
        )

        created_ids.add(
            post["id"]
        )

        time.sleep(0.01)

    first_page = get_following_feed(
        client,
        user=authenticated_user,
        page=1,
        page_size=2,
    )

    second_page = get_following_feed(
        client,
        user=authenticated_user,
        page=2,
        page_size=2,
    )

    assert first_page.status_code == 200
    assert second_page.status_code == 200

    first_body = first_page.json()
    second_body = second_page.json()

    assert first_body["totalCount"] == 3
    assert second_body["totalCount"] == 3

    assert len(first_body["items"]) == 2
    assert len(second_body["items"]) == 1

    returned_ids = {
        item["id"]
        for item in (
            first_body["items"]
            + second_body["items"]
        )
    }

    assert returned_ids == created_ids


def test_following_feed_validates_pagination(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    invalid_page = get_following_feed(
        client,
        user=authenticated_user,
        page=0,
        page_size=20,
    )

    assert invalid_page.status_code == 400

    invalid_page_size = get_following_feed(
        client,
        user=authenticated_user,
        page=1,
        page_size=101,
    )

    assert invalid_page_size.status_code == 400