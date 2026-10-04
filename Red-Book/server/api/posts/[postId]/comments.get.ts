import { requireAuthenticatedToken } from '~~/server/utils/authenticated-token'
import {
    getFetchErrorData,
    getFetchErrorStatusCode,
} from '~~/server/utils/fetch-error'

interface PostCommentResponse {
    id: string
    postId: string
    authorUserId: string
    content: string
    parentCommentId: string | null
    createdAtUtc: string
    updatedAtUtc: string
}

interface PostCommentItem extends PostCommentResponse {
    replies: PostCommentResponse[]
}

interface PostCommentsResponse {
    page: number
    pageSize: number
    totalCount: number
    items: PostCommentItem[]
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

    const query = getQuery(event)

    const page = typeof query.page === 'string'
        ? Number.parseInt(query.page, 10)
        : 1

    const pageSize = typeof query.pageSize === 'string'
        ? Number.parseInt(query.pageSize, 10)
        : 20

    if (!Number.isInteger(page) || page < 1) {
        throw createError({
            statusCode: 400,
            statusMessage: 'Page must be greater than or equal to 1.',
        })
    }

    if (!Number.isInteger(pageSize) || pageSize < 1 || pageSize > 100) {
        throw createError({
            statusCode: 400,
            statusMessage: 'PageSize must be between 1 and 100.',
        })
    }

    const config = useRuntimeConfig(event)

    if (!config.gatewayBaseUrl) {
        throw createError({
            statusCode: 500,
            statusMessage: 'Internal Gateway base URL is not configured.',
        })
    }

    let authorization: string | undefined

    const session = await getUserSession(event)

    if (session.loggedIn) {
        const token = await requireAuthenticatedToken(
            event,
            requestId,
        )

        authorization = token.authorization
    }

    const method = 'GET'
    const path = `/api/v1/posts/${encodeURIComponent(postId)}/comments`
    const startedAt = performance.now()

    console.log(`🌐 [BFF] ${method} ${path}`, {
        requestId,
        upstream: config.gatewayBaseUrl,
        postId,
        page,
        pageSize,
        authenticated: Boolean(authorization),
    })

    try {
        const response = await $fetch<PostCommentsResponse>(path, {
            baseURL: config.gatewayBaseUrl,
            method,
            query: {
                page,
                pageSize,
            },
            headers: {
                'X-Request-ID': requestId,

                ...(authorization
                    ? {
                        Authorization: authorization,
                    }
                    : {}),
            },
        })

        console.log(`✅ [BFF] ${method} ${path} ${(performance.now() - startedAt).toFixed(1)}ms`, {
            requestId,
            postId,
            returnedCount: response.items.length,
            totalCount: response.totalCount,
        })

        return response
    }
    catch (error: unknown) {
        const statusCode = getFetchErrorStatusCode(error, 502)

        if (statusCode >= 500) {
            console.error(`❌ [BFF] ${statusCode} ${method} ${path}`, {
                requestId,
                postId,
                error,
            })
        }
        else {
            console.warn(`⚠️ [BFF] ${statusCode} ${method} ${path}`, {
                requestId,
                postId,
            })
        }

        throw createError({
            statusCode,
            statusMessage: statusCode === 404
                ? 'Post not found.'
                : 'Comments request failed.',
            data: getFetchErrorData(error),
        })
    }
})