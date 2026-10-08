import { createTracedFetch } from '~~/server/utils/traced-fetch'
import { eventLogger } from '~~/server/utils/server-logger'
import {
    appendUpstreamSetCookies,
    getIdentityCsrfContext,
    getUpstreamSetCookies,
} from '~~/server/utils/identity-antiforgery'

import {
    getFetchErrorData,
    getFetchErrorStatusCode,
} from '~~/server/utils/fetch-error'

import { parseLogin } from '../../../shared/schemas/requests'
import { readJsonRequest } from '~~/server/utils/limited-body'

const IDENTITY_COOKIE_PREFIX = 'RedNote.Identity'

export default defineEventHandler(async event => {
    const requestId = event.context.requestId ?? crypto.randomUUID()
    const config = useRuntimeConfig(event)

    if (!config.gatewayBaseUrl) {
        throw createError({
            statusCode: 500,
            statusMessage: 'Internal Gateway base URL is not configured.',
        })
    }

    const body = await readJsonRequest(event, parseLogin)
    const email = body.email

    try {
        /*
         * CSRF 完全在 BFF -> Identity 内部处理。
         * Browser 不需要知道 Antiforgery Cookie/Token。
         */
        const csrf = await getIdentityCsrfContext(
            event,
            config.gatewayBaseUrl,
            requestId,
        )

        const loginResponse = await createTracedFetch(event).raw<void>(
            '/api/v1/auth/session/login',
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
            cookie.startsWith(`${IDENTITY_COOKIE_PREFIX}=`)
            || cookie.startsWith(`${IDENTITY_COOKIE_PREFIX}C`),
        )

        if (!hasIdentityCookie) {
            eventLogger(event).error('❌ [BFF] Identity 登录成功但没有返回 Application Cookie', {
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

        eventLogger(event).log('✅ [BFF] Identity Application Cookie 已建立', {
            requestId,
        })

        return {
            success: true,
        }
    }
    catch (error: unknown) {
        const statusCode = getFetchErrorStatusCode(error, 502)

        if (statusCode >= 500) {
            eventLogger(event).error('❌ [BFF] 登录请求失败', {
                requestId,
                statusCode,
            })
        }
        else {
            eventLogger(event).warn('⚠️ [BFF] 登录请求失败', {
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
