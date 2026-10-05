import { getAuthTokenSet, setAuthTokenSet, removeAuthTokenSet } from '../../utils/auth-token-store'

export default defineEventHandler(event => {
    const runtime = useRuntimeConfig(event)
    return defineOAuthOidcEventHandler({
    config: {
        scope: ['openid', 'profile', 'email', 'offline_access', 'rednote-api'],
        openidConfig: {
            authorization_endpoint: new URL('/connect/authorize', runtime.public.apiBaseUrl).href,
            token_endpoint: new URL('/connect/token', runtime.gatewayBaseUrl).href,
        },
    },
    async onSuccess(event, { tokens }) {
        if (!tokens.access_token) {
            throw createError({ statusCode: 502, statusMessage: 'OIDC access token is missing.' })
        }
        const previous = await getUserSession(event)
        await setUserSession(event, {
            user: { authenticated: true, provider: 'oidc' },
            loggedInAt: Date.now(),
        })
        const session = await getUserSession(event)
        if (!session.id) {
            throw createError({ statusCode: 502, statusMessage: 'Session could not be created.' })
        }
        try {
            await setAuthTokenSet(session.id, {
                accessToken: tokens.access_token,
                refreshToken: tokens.refresh_token,
                tokenType: tokens.token_type ?? 'Bearer',
                expiresAt: typeof tokens.expires_in === 'number'
                    ? Date.now() + tokens.expires_in * 1000 : undefined,
            })
            if (!await getAuthTokenSet(session.id)) {
                throw new Error('Token storage unavailable.')
            }
            if (previous.id && previous.id !== session.id) {
                await removeAuthTokenSet(previous.id)
            }
        }
        catch {
            await clearUserSession(event)
            throw createError({ statusCode: 502, statusMessage: 'Token storage unavailable.' })
        }
        return sendRedirect(event, '/', 302)
    },
    onError(event, _error) {
        console.warn('[OIDC] Authentication failed.')
        return sendRedirect(event, '/login?oidcError=1', 302)
    },
    })(event)
})
