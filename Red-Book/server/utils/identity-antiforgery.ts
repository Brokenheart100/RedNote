import type { H3Event } from 'h3'
import { appendResponseHeader } from 'h3'

interface CsrfResponse {
    token: string
    headerName: string
}

export interface IdentityCsrfContext {
    token: string
    headerName: string
    cookieHeader: string
    setCookies: string[]
}

export function getUpstreamSetCookies(headers: Headers): string[] {
    const getSetCookie = (
        headers as Headers & {
            getSetCookie?: () => string[]
        }
    ).getSetCookie

    if (getSetCookie) {
        return getSetCookie.call(headers)
    }

    const value = headers.get('set-cookie')

    return value
        ? [value]
        : []
}

export function createUpstreamCookieHeader(setCookies: string[]): string {
    return setCookies
        .map(setCookie => setCookie.split(';', 1)[0]?.trim())
        .filter((cookie): cookie is string => Boolean(cookie))
        .join('; ')
}

export function appendUpstreamSetCookies(
    event: H3Event,
    setCookies: string[],
): void {
    for (const setCookie of setCookies) {
        appendResponseHeader(
            event,
            'set-cookie',
            setCookie,
        )
    }
}

export async function getIdentityCsrfContext(
    gatewayBaseUrl: string,
    requestId: string,
): Promise<IdentityCsrfContext> {
    const response = await $fetch.raw<CsrfResponse>(
        '/api/v1/auth/csrf',
        {
            baseURL: gatewayBaseUrl,
            method: 'GET',

            headers: {
                'X-Request-ID': requestId,
            },
        },
    )

    const csrf = response._data

    if (!csrf?.token || !csrf.headerName) {
        throw createError({
            statusCode: 502,
            statusMessage: 'IdentityService returned an invalid CSRF response.',
        })
    }

    const setCookies = getUpstreamSetCookies(response.headers)
    const cookieHeader = createUpstreamCookieHeader(setCookies)

    if (!cookieHeader) {
        throw createError({
            statusCode: 502,
            statusMessage: 'IdentityService did not return an antiforgery cookie.',
        })
    }

    return {
        token: csrf.token,
        headerName: csrf.headerName,
        cookieHeader,
        setCookies,
    }
}