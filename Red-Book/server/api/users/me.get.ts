import type { CurrentUser } from '~~/shared/types/users'

import { requireAuthenticatedToken } from '~~/server/utils/authenticated-token'
import {
    getFetchErrorData,
    getFetchErrorStatusCode,
} from '~~/server/utils/fetch-error'

export default defineEventHandler(async event => {
    const requestId = event.context.requestId ?? crypto.randomUUID()

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
    const path = '/api/v1/users/me'
    const startedAt = performance.now()

    console.log(`🌐 [BFF] ${method} ${path}`, {
        requestId,
        sessionId,
        upstream: config.gatewayBaseUrl,
        tokenType,
        hasRefreshToken,
    })

    try {
        const result = await $fetch<CurrentUser>(path, {
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
        })

        return result
    }
    catch (error: unknown) {
        const statusCode = getFetchErrorStatusCode(error, 502)
        const duration = performance.now() - startedAt

        if (statusCode >= 500) {
            console.error(`❌ [BFF] ${statusCode} ${method} ${path} ${duration.toFixed(1)}ms`, {
                requestId,
                sessionId,
                error,
            })
        }
        else {
            console.warn(`⚠️ [BFF] ${statusCode} ${method} ${path} ${duration.toFixed(1)}ms`, {
                requestId,
                sessionId,
            })
        }

        throw createError({
            statusCode,
            statusMessage: statusCode === 401
                ? 'Unauthorized'
                : 'UserService request failed.',
            data: getFetchErrorData(error),
        })
    }
})