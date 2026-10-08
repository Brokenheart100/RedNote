import { createTracedFetch } from '~~/server/utils/traced-fetch'
import { eventLogger } from '~~/server/utils/server-logger'
import {
    getIdentityCsrfContext,
    getUpstreamSetCookies,
} from '~~/server/utils/identity-antiforgery'

import {
    getFetchErrorStatusCode,
} from '~~/server/utils/fetch-error'

export default defineEventHandler(async event => {
    const requestId = event.context.requestId ?? crypto.randomUUID()

    const session = await getUserSession(event)
    const sessionId = session.id

    const config = useRuntimeConfig(event)

    if (!config.gatewayBaseUrl) {
        throw createError({
            statusCode: 500,
            statusMessage: 'Internal Gateway base URL is not configured.',
        })
    }

    const browserCookie = getHeader(event, 'cookie')

    let identityLogoutError: unknown = null

    try {
        const csrf = await getIdentityCsrfContext(
            event,
            config.gatewayBaseUrl,
            requestId,
            browserCookie,
        )

        /*
         * 浏览器可能已经持有 Identity Application Cookie。
         * CSRF 请求又会返回新的 antiforgery cookie。
         *
         * 两者需要合并后再发给 IdentityService。
         */
        const cookies = [
            browserCookie,
            csrf.cookieHeader,
        ]
            .filter((value): value is string => Boolean(value))
            .join('; ')

        const logoutResponse = await createTracedFetch(event).raw<void>(
            '/api/v1/auth/session/logout',
            {
                baseURL: config.gatewayBaseUrl,
                method: 'POST',
                timeout: 15_000,
                retry: 0,

                headers: {
                    Cookie: cookies,
                    [csrf.headerName]: csrf.token,
                    'X-Request-ID': requestId,
                },
            },
        )

        /*
         * IdentityService logout 返回的 Set-Cookie 一般包含
         * 过期/删除 Identity Cookie。
         *
         * 这里必须转发给浏览器，否则浏览器仍然持有旧 Cookie。
         */
        const setCookies = getUpstreamSetCookies(
            logoutResponse.headers,
        )

        for (const setCookie of setCookies) {
            appendResponseHeader(
                event,
                'set-cookie',
                setCookie,
            )
        }
    }
    catch (error: unknown) {
        identityLogoutError = error

        const statusCode = getFetchErrorStatusCode(
            error, 502,
        )

        eventLogger(event).error('❌ [BFF] Identity logout failed', {
            requestId,
            statusCode,
        })
    }

    /*
     * 即使 Identity logout 失败，
     * 本地 Nuxt session / token store 也必须清理，
     * 避免用户留在半登录状态。
     */
    if (sessionId) {
        await removeAuthTokenSet(sessionId)
    }

    await clearUserSession(event)

    if (identityLogoutError) {
        throw createError({
            statusCode: 502,
            statusMessage: 'Identity logout failed.',
            data: { localSessionCleared: true },
        })
    }

    return {
        success: true,
    }
})
