import type { H3Event } from 'h3'
import { createTracedFetch } from './traced-fetch'
import type { AuthTokenSet } from '../../shared/types/token'
import { authTokenRepository } from './auth-token-store'
import { createTokenRefresher, TokenRefreshError } from './token-refresher'
import { getFetchErrorStatusCode } from './fetch-error'

const refresh = createTokenRefresher(authTokenRepository)

export async function refreshAuthTokenSet(
    event: H3Event,
    sessionId: string,
    _requestId?: string,
): Promise<AuthTokenSet> {
    const config = useRuntimeConfig(event)
    const upstreamFetch = createTracedFetch(event)
    try {
        return await refresh(sessionId, async current => {
            const discovery = await upstreamFetch<{ token_endpoint?: string }>(
                config.oauth.oidc.openidConfig,
                { timeout: 5_000, retry: 0 },
            )
            if (!discovery.token_endpoint) {
                throw new TokenRefreshError(502, 'OIDC discovery response is invalid.')
            }
            const body = new URLSearchParams({
                grant_type: 'refresh_token',
                client_id: config.oauth.oidc.clientId,
                refresh_token: current.refreshToken!,
            })
            let response: {
                access_token?: string
                refresh_token?: string
                token_type?: string
                expires_in?: number
            }
            try {
                response = await upstreamFetch(new URL('/connect/token', config.gatewayBaseUrl).href, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                    body,
                    timeout: 15_000,
                    retry: 0,
                })
            }
            catch (error) {
                const status = getFetchErrorStatusCode(error, 502)
                throw new TokenRefreshError(
                    status === 400 || status === 401 ? 401 : 502,
                    status === 400 || status === 401
                        ? 'Authentication session has expired.'
                        : 'OIDC token refresh failed.',
                )
            }
            if (typeof response.access_token !== 'string' || !response.access_token
                || typeof response.expires_in !== 'number' || response.expires_in <= 0) {
                throw new TokenRefreshError(502, 'OIDC refresh response is invalid.')
            }
            return {
                accessToken: response.access_token,
                refreshToken: response.refresh_token ?? current.refreshToken,
                tokenType: response.token_type ?? current.tokenType,
                expiresAt: Date.now() + response.expires_in * 1000,
            }
        })
    }
    catch (error) {
        throw createError({
            statusCode: error instanceof TokenRefreshError ? error.statusCode : 502,
            statusMessage: error instanceof TokenRefreshError ? error.message : 'OIDC refresh failed.',
        })
    }
}
