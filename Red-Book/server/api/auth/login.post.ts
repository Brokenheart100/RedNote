import {
    appendUpstreamSetCookies,
    getIdentityCsrfContext,
    getUpstreamSetCookies,
} from '~~/server/utils/identity-antiforgery'

import {
    getFetchErrorData,
    getFetchErrorStatusCode,
} from '~~/server/utils/fetch-error'

interface LoginRequest {
    email: string
    password: string
}

const IDENTITY_COOKIE_PREFIX = '__Host-RedNote.Identity'

export default defineEventHandler(async event => {
    const requestId = event.context.requestId ?? crypto.randomUUID()
    const config = useRuntimeConfig(event)

    if (!config.gatewayBaseUrl) {
        throw createError({
            statusCode: 500,
            statusMessage: 'Internal Gateway base URL is not configured.',
        })
    }

    const body = await readBody<LoginRequest>(event)
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
        /*
         * CSRF 完全在 BFF -> Identity 内部处理。
         * Browser 不需要知道 Antiforgery Cookie/Token。
         */
        const csrf = await getIdentityCsrfContext(
            config.gatewayBaseUrl,
            requestId,
        )

        const loginResponse = await $fetch.raw<void>(
            '/api/v1/auth/session/login',
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
                },
            },
        )

        /*
         * 登录成功必须由 IdentityService 返回 Application Cookie。
         *
         * 如果没有 Cookie，直接认为认证链路异常。
         * 不允许页面继续进入 OIDC 后再产生难排查的登录循环。
         */
        const identitySetCookies = getUpstreamSetCookies(
            loginResponse.headers,
        )

        const hasIdentityCookie = identitySetCookies.some(cookie =>
            cookie.startsWith(IDENTITY_COOKIE_PREFIX),
        )

        if (!hasIdentityCookie) {
            console.error('❌ [BFF] Identity 登录成功但没有返回 Application Cookie', {
                requestId,
            })

            throw createError({
                statusCode: 502,
                statusMessage: 'Identity authentication cookie was not issued.',
            })
        }

        appendUpstreamSetCookies(
            event,
            identitySetCookies,
        )

        console.log('✅ [BFF] Identity Application Cookie 已建立', {
            requestId,
        })

        return {
            success: true,
        }
    }
    catch (error: unknown) {
        const statusCode = getFetchErrorStatusCode(error, 502)

        if (statusCode >= 500) {
            console.error('❌ [BFF] 登录请求失败', {
                requestId,
                statusCode,
                error,
            })
        }
        else {
            console.warn('⚠️ [BFF] 登录请求失败', {
                requestId,
                statusCode,
            })
        }

        throw createError({
            statusCode,
            statusMessage: statusCode === 401
                ? 'Unauthorized'
                : 'Login request failed.',
            data: getFetchErrorData(error),
        })
    }
})