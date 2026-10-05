import type { H3Event } from 'h3'
import { refreshAuthTokenSet } from './auth-token-refresh'
import { shouldRefreshToken } from './token-refresher'
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
        throw createError({ statusCode: 401, statusMessage: 'Authentication session is unavailable.' })
    }

    let tokens
    try {
        tokens = await getAuthTokenSet(session.id)
    }
    catch {
        throw createError({ statusCode: 503, statusMessage: 'Authentication storage is unavailable.' })
    }
    if (!tokens?.accessToken) {
        await clearUserSession(event)
        throw createError({ statusCode: 401, statusMessage: 'Authentication token is unavailable.' })
    }
    if (shouldRefreshToken(tokens)) {
        try {
            tokens = await refreshAuthTokenSet(event, session.id, requestId)
        }
        catch (error) {
            if (getFetchErrorStatusCode(error) === 401) await clearUserSession(event)
            throw error
        }
    }
    return {
        sessionId: session.id,
        accessToken: tokens.accessToken,
        authorization: `${tokens.tokenType} ${tokens.accessToken}`,
        tokenType: tokens.tokenType,
        hasRefreshToken: Boolean(tokens.refreshToken),
    }
}