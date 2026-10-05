import type { RegisterResponse } from '~~/shared/types/auth'

import {
    getIdentityCsrfContext,
} from '~~/server/utils/identity-antiforgery'

import {
    getFetchErrorData,
    getFetchErrorStatusCode,
} from '~~/server/utils/fetch-error'

import { parseRegister } from '../../../shared/schemas/requests'
import { readJsonRequest } from '~~/server/utils/limited-body'

export default defineEventHandler(async event => {
    const requestId = event.context.requestId ?? crypto.randomUUID()
    const config = useRuntimeConfig(event)

    if (!config.gatewayBaseUrl) {
        throw createError({
            statusCode: 500,
            statusMessage: 'Internal Gateway base URL is not configured.',
        })
    }

    const body = await readJsonRequest(event, parseRegister)
    const email = body.email

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
                timeout: 15_000,
                retry: 0,

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
