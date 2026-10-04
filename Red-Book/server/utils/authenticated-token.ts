import type { H3Event } from 'h3'

import {
    refreshAuthTokenSet,
    shouldRefreshToken,
} from './auth-token-refresh'

import { getAuthTokenSet } from './auth-token-store'
import { getFetchErrorStatusCode } from './fetch-error'

export interface AuthenticatedTokenContext {
    sessionId: string
    accessToken: string
    authorization: string
    tokenType: string
    hasRefreshToken: boolean
}

export async function requireAuthenticatedToken(
    event: H3Event,
    requestId: string,
): Promise<AuthenticatedTokenContext> {
    const session = await requireUserSession(event)

    if (!session.id) {
        console.warn('⚠️ [BFF] Session ID 不存在', {
            requestId,
        })

        throw createError({
            statusCode: 401,
            statusMessage: 'Authentication session is unavailable.',
        })
    }

    const sessionId = session.id
    let tokens = await getAuthTokenSet(sessionId)

    if (!tokens?.accessToken) {
        console.warn('⚠️ [BFF] Access Token 不存在', {
            requestId,
            sessionId,
        })

        throw createError({
            statusCode: 401,
            statusMessage: 'Authentication token is unavailable.',
        })
    }

    if (shouldRefreshToken(tokens)) {
        console.log('🔄 [BFF] Access Token 即将过期，开始自动刷新', {
            requestId,
            sessionId,
            expiresAt: tokens.expiresAt ?? null,
        })

        try {
            tokens = await refreshAuthTokenSet(event, sessionId, requestId)
        }
        catch (error: unknown) {
            if (getFetchErrorStatusCode(error) === 401) {
                console.warn('⚠️ [BFF] 认证 Session 已过期，清理 Nuxt Session', {
                    requestId,
                    sessionId,
                })

                await clearUserSession(event)
            }

            throw error
        }
    }

    return {
        sessionId,
        accessToken: tokens.accessToken,
        authorization: `${tokens.tokenType} ${tokens.accessToken}`,
        tokenType: tokens.tokenType,
        hasRefreshToken: Boolean(tokens.refreshToken),
    }
}