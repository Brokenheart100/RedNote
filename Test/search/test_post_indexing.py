from __future__ import annotations

import time

import httpx


def bearer_headers(access_token: str) -> dict[str, str]:
    return {
        "Authorization": f"Bearer {access_token}"
    }


def test_created_post_is_indexed_in_opensearch(
    client: httpx.Client,
    authenticated_user,
) -> None:
    unique_title = (
        f"OpenSearchIndexTest-{time.time_ns()}"
    )

    create_response = client.post(
        "/api/v1/posts",
        headers=bearer_headers(
            authenticated_user.access_token
        ),
        json={
            "title": unique_title,
            "content": "OpenSearch indexing test content",
            "mediaIds": [],
            "tags": [
                "opensearch",
                "wolverine",
            ],
        },
    )

    assert create_response.status_code == 201

    post = create_response.json()
    post_id = post["id"]

    timeout_seconds = 10
    poll_interval_seconds = 0.5

    deadline = (
        time.monotonic()
        + timeout_seconds
    )

    indexed_document = None

    while time.monotonic() < deadline:
        response = httpx.get(
            f"http://localhost:9200/"
            f"posts-v1/_doc/{post_id}",
            timeout=5,
        )

        if response.status_code == 200:
            indexed_document = response.json()
            break

        assert response.status_code == 404

        time.sleep(
            poll_interval_seconds
        )

    assert indexed_document is not None

    source = indexed_document["_source"]

    assert source["id"] == post_id
    assert source["title"] == unique_title
    assert (
        source["content"]
        == "OpenSearch indexing test content"
    )

    assert set(source["tags"]) == {
        "opensearch",
        "wolverine",
    }

    assert source["likeCount"] == 0
    assert source["commentCount"] == 0