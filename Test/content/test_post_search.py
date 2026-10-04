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


def create_post(
    client: httpx.Client,
    user: AuthenticatedUser,
    *,
    title: str,
    content: str,
) -> dict:
    response = client.post(
        "/api/v1/posts",
        headers=bearer_headers(user),
        json={
            "title": title,
            "content": content,
            "mediaIds": [],
            "tags": [],
        },
    )

    assert response.status_code == 201

    return response.json()


def search_posts(
    client: httpx.Client,
    *,
    q: str,
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
        "/api/v1/search/posts",
        headers=headers,
        params={
            "q": q,
            "page": page,
            "pageSize": page_size,
        },
    )


def test_search_allows_anonymous_access(
    client: httpx.Client,
) -> None:
    response = search_posts(
        client,
        q="anonymous_search_test",
    )

    assert response.status_code == 200


def test_search_matches_title(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="UniqueSearchTitleAlpha",
        content="Normal content",
    )

    response = search_posts(
        client,
        q="UniqueSearchTitleAlpha",
        page_size=100,
    )

    assert response.status_code == 200

    ids = {
        item["id"]
        for item in response.json()["items"]
    }

    assert post["id"] in ids


def test_search_matches_content(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="Normal Search Title",
        content="UniqueSearchContentBeta",
    )

    response = search_posts(
        client,
        q="UniqueSearchContentBeta",
        page_size=100,
    )

    assert response.status_code == 200

    ids = {
        item["id"]
        for item in response.json()["items"]
    }

    assert post["id"] in ids


def test_search_is_case_insensitive(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="CaseInsensitiveSearchGamma",
        content="Normal content",
    )

    response = search_posts(
        client,
        q="caseinsensitivesearchgamma",
        page_size=100,
    )

    assert response.status_code == 200

    ids = {
        item["id"]
        for item in response.json()["items"]
    }

    assert post["id"] in ids


def test_search_matches_partial_keyword(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="AspireNuxtIntegratedSearch",
        content="Normal content",
    )

    response = search_posts(
        client,
        q="NuxtIntegrated",
        page_size=100,
    )

    assert response.status_code == 200

    ids = {
        item["id"]
        for item in response.json()["items"]
    }

    assert post["id"] in ids


def test_search_does_not_return_unmatched_post(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="CompletelyDifferentTitle",
        content="Completely different content",
    )

    response = search_posts(
        client,
        q="NoMatchKeywordXYZ987654",
        page_size=100,
    )

    assert response.status_code == 200

    ids = {
        item["id"]
        for item in response.json()["items"]
    }

    assert post["id"] not in ids


def test_deleted_post_is_not_searchable(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="DeletedSearchDelta",
        content="Deleted searchable content",
    )

    delete_response = client.delete(
        f"/api/v1/posts/{post['id']}",
        headers=bearer_headers(
            authenticated_user
        ),
    )

    assert delete_response.status_code == 204

    response = search_posts(
        client,
        q="DeletedSearchDelta",
        page_size=100,
    )

    assert response.status_code == 200

    ids = {
        item["id"]
        for item in response.json()["items"]
    }

    assert post["id"] not in ids


def test_search_orders_by_created_at_desc(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    keyword = "SearchOrderOmega"

    first_post = create_post(
        client,
        authenticated_user,
        title=f"{keyword} First",
        content="First",
    )

    time.sleep(0.01)

    second_post = create_post(
        client,
        authenticated_user,
        title=f"{keyword} Second",
        content="Second",
    )

    response = search_posts(
        client,
        q=keyword,
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


def test_search_returns_like_and_comment_count(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="SearchInteractionCount",
        content="Interaction search content",
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
                "Search comment",
            "parentCommentId":
                None,
        },
    )

    assert comment_response.status_code == 201

    response = search_posts(
        client,
        q="SearchInteractionCount",
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


def test_anonymous_search_has_false_user_state(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="AnonymousSearchState",
        content="Anonymous state content",
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

    assert like_response.status_code == 204
    assert favorite_response.status_code == 204

    response = search_posts(
        client,
        q="AnonymousSearchState",
        page_size=100,
    )

    assert response.status_code == 200

    item = next(
        item
        for item in response.json()["items"]
        if item["id"] == post["id"]
    )

    assert item["likeCount"] == 1
    assert item["isLiked"] is False
    assert item["isFavorited"] is False


def test_authenticated_search_returns_user_state(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    post = create_post(
        client,
        authenticated_user,
        title="AuthenticatedSearchState",
        content="Authenticated state content",
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

    assert like_response.status_code == 204
    assert favorite_response.status_code == 204

    response = search_posts(
        client,
        q="AuthenticatedSearchState",
        user=authenticated_user,
        page_size=100,
    )

    assert response.status_code == 200

    item = next(
        item
        for item in response.json()["items"]
        if item["id"] == post["id"]
    )

    assert item["isLiked"] is True
    assert item["isFavorited"] is True


def test_search_returns_tags(
    client: httpx.Client,
    authenticated_user: AuthenticatedUser,
) -> None:
    create_response = client.post(
        "/api/v1/posts",
        headers=bearer_headers(
            authenticated_user
        ),
        json={
            "title":
                "SearchTagIntegration",
            "content":
                "Search tag content",
            "mediaIds":
                [],
            "tags": [
                "search",
                "aspire",
            ],
        },
    )

    assert create_response.status_code == 201

    post = create_response.json()

    response = search_posts(
        client,
        q="SearchTagIntegration",
        page_size=100,
    )

    assert response.status_code == 200

    item = next(
        item
        for item in response.json()["items"]
        if item["id"] == post["id"]
    )

    assert set(
        item["tags"]
    ) == {
        "search",
        "aspire",
    }


def test_search_pagination(
    client: httpx.Client,
    create_user,
) -> None:
    user = create_user(
        prefix="search_paging",
        display_name="Search Paging",
    )

    keyword = "SearchPagingUniqueTheta"

    created_ids: set[str] = set()

    for index in range(3):
        post = create_post(
            client,
            user,
            title=f"{keyword} {index}",
            content="Paging content",
        )

        created_ids.add(
            post["id"]
        )

        time.sleep(0.01)

    first_page = search_posts(
        client,
        q=keyword,
        page=1,
        page_size=2,
    )

    second_page = search_posts(
        client,
        q=keyword,
        page=2,
        page_size=2,
    )

    assert first_page.status_code == 200
    assert second_page.status_code == 200

    first_body = first_page.json()
    second_body = second_page.json()

    assert first_body["totalCount"] == 3
    assert second_body["totalCount"] == 3

    assert len(
        first_body["items"]
    ) == 2

    assert len(
        second_body["items"]
    ) == 1

    returned_ids = {
        item["id"]
        for item in (
            first_body["items"]
            + second_body["items"]
        )
    }

    assert returned_ids == created_ids


def test_search_validates_empty_query(
    client: httpx.Client,
) -> None:
    response = search_posts(
        client,
        q="   ",
    )

    assert response.status_code == 400


def test_search_validates_query_length(
    client: httpx.Client,
) -> None:
    response = search_posts(
        client,
        q="x" * 101,
    )

    assert response.status_code == 400


def test_search_validates_pagination(
    client: httpx.Client,
) -> None:
    invalid_page = search_posts(
        client,
        q="validation",
        page=0,
        page_size=20,
    )

    assert invalid_page.status_code == 400

    invalid_page_size = search_posts(
        client,
        q="validation",
        page=1,
        page_size=101,
    )

    assert invalid_page_size.status_code == 400