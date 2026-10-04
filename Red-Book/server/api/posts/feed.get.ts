import type { FeedResponse } from '~~/shared/types/posts'

import { requireAuthenticatedToken } from '~~/server/utils/authenticated-token'
import {
    getFetchErrorData,
    getFetchErrorStatusCode,
} from '~~/server/utils/fetch-error'

export default defineEventHandler(async event => {
    const requestId = event.context.requestId ?? crypto.randomUUID()

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

    const method = 'GET'
    const path = '/api/v1/posts/feed'
    const startedAt = performance.now()

    console.log(`🌐 [BFF] ${method} ${path}`, {
        requestId,
        sessionId,
        upstream: config.gatewayBaseUrl,
        page,
        pageSize,
        tokenType,
        hasRefreshToken,
    })

    try {
        const response = await $fetch<FeedResponse>(path, {
            baseURL: config.gatewayBaseUrl,
            method,
            query: {
                page,
                pageSize,
            },
            headers: {
                Authorization: authorization,
                'X-Request-ID': requestId,
            },
        })

        console.log(`✅ [BFF] ${method} ${path} ${(performance.now() - startedAt).toFixed(1)}ms`, {
            requestId,
            sessionId,
            page,
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
                sessionId,
                error,
            })
        }
        else {
            console.warn(`⚠️ [BFF] ${statusCode} ${method} ${path}`, {
                requestId,
                sessionId,
            })
        }

        throw createError({
            statusCode,
            statusMessage: statusCode === 401
                ? 'Unauthorized'
                : 'Feed request failed.',
            data: getFetchErrorData(error),
        })
    }
})