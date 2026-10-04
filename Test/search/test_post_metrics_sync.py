from __future__ import annotations

import time

import httpx


OPENSEARCH_BASE_URL = "http://localhost:9200"
INDEX_NAME = "posts-v1"


def bearer_headers(
    access_token: str,
) -> dict[str, str]:
    return {
        "Authorization":
            f"Bearer {access_token}"
    }


def get_document(
    post_id: str,
) -> dict | None:
    response = httpx.get(
        f"{OPENSEARCH_BASE_URL}/"
        f"{INDEX_NAME}/_doc/{post_id}",
        timeout=5,
    )

    if response.status_code == 404:
        return None

    response.raise_for_status()

    return response.json()


def wait_for_metrics(
    post_id: str,
    *,
    expected_like_count: int,
    expected_comment_count: int,
    timeout_seconds: float = 10,
    poll_interval_seconds: float = 0.25,
) -> dict:
    deadline = (
        time.monotonic()
        + timeout_seconds
    )

    last_source: dict | None = None

    while time.monotonic() < deadline:
        document = get_document(
            post_id
        )

        if document is not None:
            source = document["_source"]

            last_source = source

            if (
                source["likeCount"]
                == expected_like_count
                and
                source["commentCount"]
                == expected_comment_count
            ):
                return source

        time.sleep(
            poll_interval_seconds
        )

    raise AssertionError(
        f"Post '{post_id}' did not reach "
        f"expected metrics "
        f"likeCount={expected_like_count}, "
        f"commentCount={expected_comment_count} "
        f"within {timeout_seconds} seconds. "
        f"Last source: {last_source}"
    )


def test_post_metrics_are_synchronized_to_opensearch(
    client: httpx.Client,
    authenticated_user,
) -> None:
    headers = bearer_headers(
        authenticated_user.access_token
    )

    unique_suffix = str(
        time.time_ns()
    )

    #
    # CREATE POST
    #

    create_response = client.post(
        "/api/v1/posts",
        headers=headers,
        json={
            "title":
                f"Metrics Sync {unique_suffix}",
            "content":
                "OpenSearch metrics synchronization test.",
            "mediaIds":
                [],
            "tags":
                [
                    "metrics",
                    "opensearch",
                ],
        },
    )

    assert create_response.status_code == 201

    post = create_response.json()

    post_id = post["id"]

    #
    # 初始指标
    #
    # PostPublished:
    #
    # likeCount = 0
    # commentCount = 0
    #

    initial_source = wait_for_metrics(
        post_id,
        expected_like_count=0,
        expected_comment_count=0,
    )

    assert initial_source["id"] == post_id

    #
    # LIKE
    #

    like_response = client.post(
        f"/api/v1/posts/{post_id}/likes",
        headers=headers,
    )

    assert like_response.status_code == 204

    liked_source = wait_for_metrics(
        post_id,
        expected_like_count=1,
        expected_comment_count=0,
    )

    assert liked_source["likeCount"] == 1
    assert liked_source["commentCount"] == 0

    #
    # CREATE COMMENT
    #

    create_comment_response = client.post(
        f"/api/v1/posts/{post_id}/comments",
        headers=headers,
        json={
            "content":
                "OpenSearch metrics comment.",
            "parentCommentId":
                None,
        },
    )

    assert create_comment_response.status_code == 201

    comment = create_comment_response.json()

    comment_id = comment["id"]

    commented_source = wait_for_metrics(
        post_id,
        expected_like_count=1,
        expected_comment_count=1,
    )

    assert commented_source["likeCount"] == 1
    assert commented_source["commentCount"] == 1

    #
    # DELETE COMMENT
    #

    delete_comment_response = client.delete(
        f"/api/v1/posts/"
        f"{post_id}/comments/{comment_id}",
        headers=headers,
    )

    assert delete_comment_response.status_code == 204

    comment_deleted_source = wait_for_metrics(
        post_id,
        expected_like_count=1,
        expected_comment_count=0,
    )

    assert (
        comment_deleted_source["likeCount"]
        == 1
    )

    assert (
        comment_deleted_source["commentCount"]
        == 0
    )

    #
    # UNLIKE
    #

    unlike_response = client.delete(
        f"/api/v1/posts/{post_id}/likes",
        headers=headers,
    )

    assert unlike_response.status_code == 204

    final_source = wait_for_metrics(
        post_id,
        expected_like_count=0,
        expected_comment_count=0,
    )

    assert final_source["likeCount"] == 0
    assert final_source["commentCount"] == 0