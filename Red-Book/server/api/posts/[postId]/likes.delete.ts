import { requireAuthenticatedToken } from '~~/server/utils/authenticated-token'
import {
    getFetchErrorData,
    getFetchErrorStatusCode,
} from '~~/server/utils/fetch-error'

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

    const config = useRuntimeConfig(event)

    if (!config.gatewayBaseUrl) {
        throw createError({
            statusCode: 500,
            statusMessage: 'Internal Gateway base URL is not configured.',
        })
    }

    const method = 'DELETE'
    const path = `/api/v1/posts/${encodeURIComponent(postId)}/likes`
    const startedAt = performance.now()

    console.log(`🌐 [BFF] ${method} ${path}`, {
        requestId,
        sessionId,
        upstream: config.gatewayBaseUrl,
        postId,
        tokenType,
        hasRefreshToken,
    })

    try {
        await $fetch<void>(path, {
            baseURL: config.gatewayBaseUrl,
            method,
            headers: {
                Authorization: authorization,
                'X-Request-ID': requestId,
            },
        })

        console.log(`✅ [BFF] ${method} ${path} ${(performance.now() - startedAt).toFixed(1)}ms`, {
            requestId,
            sessionId,
            postId,
        })

        setResponseStatus(event, 204)
        return null
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
                    : 'Unlike request failed.',
            data: getFetchErrorData(error),
        })
    }
})