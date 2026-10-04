from __future__ import annotations

import time

import httpx


OPENSEARCH_BASE_URL = "http://localhost:9200"
INDEX_NAME = "posts-v1"


def bearer_headers(access_token: str) -> dict[str, str]:
    return {
        "Authorization": f"Bearer {access_token}",
    }


def get_indexed_document(
    post_id: str,
) -> dict | None:
    response = httpx.get(
        f"{OPENSEARCH_BASE_URL}/{INDEX_NAME}/_doc/{post_id}",
        timeout=5,
    )

    if response.status_code == 404:
        return None

    response.raise_for_status()

    return response.json()


def wait_for_document(
    post_id: str,
    *,
    timeout_seconds: float = 10,
    poll_interval_seconds: float = 0.25,
) -> dict:
    deadline = time.monotonic() + timeout_seconds

    while time.monotonic() < deadline:
        document = get_indexed_document(post_id)

        if document is not None:
            return document

        time.sleep(poll_interval_seconds)

    raise AssertionError(
        f"Post '{post_id}' was not indexed "
        f"within {timeout_seconds} seconds."
    )


def wait_for_document_matching(
    post_id: str,
    predicate,
    *,
    timeout_seconds: float = 10,
    poll_interval_seconds: float = 0.25,
) -> dict:
    deadline = time.monotonic() + timeout_seconds

    while time.monotonic() < deadline:
        document = get_indexed_document(post_id)

        if document is not None:
            source = document["_source"]

            if predicate(source):
                return document

        time.sleep(poll_interval_seconds)

    raise AssertionError(
        f"Post '{post_id}' did not reach the expected "
        f"OpenSearch state within {timeout_seconds} seconds."
    )


def wait_for_document_deleted(
    post_id: str,
    *,
    timeout_seconds: float = 10,
    poll_interval_seconds: float = 0.25,
) -> None:
    deadline = time.monotonic() + timeout_seconds

    while time.monotonic() < deadline:
        if get_indexed_document(post_id) is None:
            return

        time.sleep(poll_interval_seconds)

    raise AssertionError(
        f"Post '{post_id}' still exists in OpenSearch "
        f"after {timeout_seconds} seconds."
    )


def test_post_index_create_update_delete(
    client: httpx.Client,
    authenticated_user,
) -> None:
    headers = bearer_headers(
        authenticated_user.access_token
    )

    unique_suffix = str(time.time_ns())

    original_title = (
        f"OpenSearch Sync Create {unique_suffix}"
    )

    original_content = (
        "Original OpenSearch synchronization content."
    )

    create_response = client.post(
        "/api/v1/posts",
        headers=headers,
        json={
            "title": original_title,
            "content": original_content,
            "mediaIds": [],
            "tags": [
                "opensearch",
                "wolverine",
            ],
        },
    )

    assert create_response.status_code == 201

    created_post = create_response.json()

    post_id = created_post["id"]

    #
    # CREATE → PostPublished → OpenSearch
    #

    created_document = wait_for_document(
        post_id
    )

    created_source = created_document["_source"]

    assert created_document["found"] is True

    assert created_source["id"] == post_id

    assert (
        created_source["authorUserId"]
        == created_post["authorUserId"]
    )

    assert created_source["title"] == original_title

    assert (
        created_source["content"]
        == original_content
    )

    assert set(created_source["tags"]) == {
        "opensearch",
        "wolverine",
    }

    assert created_source["likeCount"] == 0
    assert created_source["commentCount"] == 0

    #
    # UPDATE → PostUpdated → OpenSearch
    #

    updated_title = (
        f"OpenSearch Sync Updated {unique_suffix}"
    )

    updated_content = (
        "Updated OpenSearch synchronization content."
    )

    update_response = client.patch(
        f"/api/v1/posts/{post_id}",
        headers=headers,
        json={
            "title": updated_title,
            "content": updated_content,
            "tags": [
                "search",
                "updated",
            ],
        },
    )

    assert update_response.status_code == 200

    updated_document = wait_for_document_matching(
        post_id,
        lambda source: (
            source["title"] == updated_title
            and
            source["content"] == updated_content
            and
            set(source["tags"])
            == {
                "search",
                "updated",
            }
        ),
    )

    updated_source = updated_document["_source"]

    assert updated_source["id"] == post_id
    assert updated_source["title"] == updated_title

    assert (
        updated_source["content"]
        == updated_content
    )

    assert set(updated_source["tags"]) == {
        "search",
        "updated",
    }

    assert updated_source["likeCount"] == 0
    assert updated_source["commentCount"] == 0

    #
    # DELETE → PostDeleted → OpenSearch
    #

    delete_response = client.delete(
        f"/api/v1/posts/{post_id}",
        headers=headers,
    )

    assert delete_response.status_code == 204

    wait_for_document_deleted(
        post_id
    )