import { requireAuthenticatedToken } from '~~/server/utils/authenticated-token'
import {
    getFetchErrorData,
    getFetchErrorStatusCode,
} from '~~/server/utils/fetch-error'

interface CreatePostCommentRequest {
    content: string
    parentCommentId?: string | null
}

interface PostCommentResponse {
    id: string
    postId: string
    authorUserId: string
    content: string
    parentCommentId: string | null
    createdAtUtc: string
    updatedAtUtc: string
}

export default defineEventHandler(async event => {
    const requestId = event.context.requestId ?? crypto.randomUUID()

    const postId = getRouterParam(event, 'postId')?.trim()

    if (!postId) {
        throw createError({
            statusCode: 400,
            statusMessage: 'Post ID is required.',
        })
    }

    const {
        sessionId,
        authorization,
        tokenType,
        hasRefreshToken,
    } = await requireAuthenticatedToken(event, requestId)

    const body = await readBody<CreatePostCommentRequest>(event)

    const content = body.content?.trim()

    if (!content) {
        throw createError({
            statusCode: 400,
            statusMessage: 'Comment content is required.',
        })
    }

    if (content.length > 1000) {
        throw createError({
            statusCode: 400,
            statusMessage: 'Comment content cannot exceed 1000 characters.',
        })
    }

    const parentCommentId = body.parentCommentId?.trim() || null

    const config = useRuntimeConfig(event)

    if (!config.gatewayBaseUrl) {
        throw createError({
            statusCode: 500,
            statusMessage: 'Internal Gateway base URL is not configured.',
        })
    }

    const method = 'POST'
    const path = `/api/v1/posts/${encodeURIComponent(postId)}/comments`
    const startedAt = performance.now()

    console.log(`🌐 [BFF] ${method} ${path}`, {
        requestId,
        sessionId,
        upstream: config.gatewayBaseUrl,
        postId,
        parentCommentId,
        contentLength: content.length,
        tokenType,
        hasRefreshToken,
    })

    try {
        const response = await $fetch<PostCommentResponse>(path, {
            baseURL: config.gatewayBaseUrl,
            method,

            headers: {
                Authorization: authorization,
                'X-Request-ID': requestId,
            },

            body: {
                content,
                parentCommentId,
            },
        })

        console.log(`✅ [BFF] ${method} ${path} ${(performance.now() - startedAt).toFixed(1)}ms`, {
            requestId,
            sessionId,
            postId,
            commentId: response.id,
        })

        return response
    }
    catch (error: unknown) {
        const statusCode = getFetchErrorStatusCode(error, 502)

        if (statusCode >= 500) {
            console.error(`❌ [BFF] ${statusCode} ${method} ${path}`, {
                requestId,
                sessionId,
                postId,
                error,
            })
        }
        else {
            console.warn(`⚠️ [BFF] ${statusCode} ${method} ${path}`, {
                requestId,
                sessionId,
                postId,
            })
        }

        throw createError({
            statusCode,
            statusMessage: statusCode === 401
                ? 'Unauthorized'
                : statusCode === 404
                    ? 'Post not found.'
                    : 'Comment creation failed.',
            data: getFetchErrorData(error),
        })
    }
})