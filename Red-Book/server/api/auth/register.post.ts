import type { RegisterResponse } from '~~/shared/types/auth'

import {
    getIdentityCsrfContext,
} from '~~/server/utils/identity-antiforgery'

import {
    getFetchErrorData,
    getFetchErrorStatusCode,
} from '~~/server/utils/fetch-error'

interface RegisterRequest {
    email: string
    password: string
    displayName?: string | null
    familyName?: string | null
}

export default defineEventHandler(async event => {
    const requestId = event.context.requestId ?? crypto.randomUUID()
    const config = useRuntimeConfig(event)

    if (!config.gatewayBaseUrl) {
        throw createError({
            statusCode: 500,
            statusMessage: 'Internal Gateway base URL is not configured.',
        })
    }

    const body = await readBody<RegisterRequest>(event)

    const email = body.email?.trim()

    if (!email) {
        throw createError({
            statusCode: 400,
            statusMessage: 'Email is required.',
        })
    }

    if (!body.password) {
        throw createError({
            statusCode: 400,
            statusMessage: 'Password is required.',
        })
    }

    try {
        const csrf = await getIdentityCsrfContext(
            config.gatewayBaseUrl,
            requestId,
        )

        return await $fetch<RegisterResponse>(
            '/api/v1/auth/register',
            {
                baseURL: config.gatewayBaseUrl,
                method: 'POST',

                headers: {
                    Cookie: csrf.cookieHeader,
                    [csrf.headerName]: csrf.token,
                    'X-Request-ID': requestId,
                },

                body: {
                    email,
                    password: body.password,
                    displayName: body.displayName?.trim() || null,
                    familyName: body.familyName?.trim() || null,
                },
            },
        )
    }
    catch (error: unknown) {
        const statusCode = getFetchErrorStatusCode(error, 502)

        if (statusCode >= 500) {
            console.error('❌ [BFF] 注册请求失败', {
                requestId,
                statusCode,
                error,
            })
        }
        else {
            console.warn('⚠️ [BFF] 注册请求失败', {
                requestId,
                statusCode,
            })
        }

        throw createError({
            statusCode,
            statusMessage: 'Registration request failed.',
            data: getFetchErrorData(error),
        })
    }
})