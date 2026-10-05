import { getAuthTokenSet } from '~~/server/utils/auth-token-store'

interface JwtPayload {
    sub?: unknown
    iss?: unknown
    aud?: unknown
    exp?: unknown
    iat?: unknown
}

function decodeJwtPayload(accessToken: string): JwtPayload | null {
    const parts = accessToken.split('.')

    if (parts.length !== 3 || !parts[1]) {
        return null
    }

    try {
        const json = Buffer
            .from(parts[1], 'base64url')
            .toString('utf8')

        const value: unknown = JSON.parse(json)

        if (
            typeof value !== 'object'
            || value === null
            || Array.isArray(value)
        ) {
            return null
        }

        return value as JwtPayload
    }
    catch {
        return null
    }
}

function getStringClaim(value: unknown): string | null {
    return typeof value === 'string'
        ? value
        : null
}

function getAudience(value: unknown): string | string[] | null {
    if (typeof value === 'string') {
        return value
    }

    if (
        Array.isArray(value)
        && value.every(item => typeof item === 'string')
    ) {
        return value
    }

    return null
}

function maskSessionId(sessionId: string): string {
    if (sessionId.length <= 8) {
        return '***'
    }

    return `${sessionId.slice(0, 4)}...${sessionId.slice(-4)}`
}

export default defineEventHandler(async event => {
    if (!import.meta.dev) {
        return
    }

    const path = event.path

    const shouldTrace =
        path.startsWith('/auth/')
        || path.startsWith('/api/users/')
        || path.startsWith('/api/auth/')

    if (!shouldTrace) {
        return
    }

    const requestId =
        event.context.requestId
        ?? crypto.randomUUID()

    const session = await getUserSession(event)

    if (!session.id || !session.user) {
        console.log('🔍 [AUTH DEBUG] 请求没有 Nuxt Session', {
            requestId,
            method: event.method,
            path,
            authenticated: false,
        })

        return
    }

    const tokens = await getAuthTokenSet(session.id)

    if (!tokens?.accessToken) {
        console.log('🔍 [AUTH DEBUG] Session 存在，但 Redis Token Set 不存在', {
            requestId,
            method: event.method,
            path,
            sessionId: maskSessionId(session.id),
            authenticated: Boolean(session.user),
        })

        return
    }

    const payload = decodeJwtPayload(tokens.accessToken)

    const expiresAt =
        typeof tokens.expiresAt === 'number'
            ? new Date(tokens.expiresAt).toISOString()
            : null

    const jwtExpiresAt =
        payload
            && typeof payload.exp === 'number'
            ? new Date(payload.exp * 1000).toISOString()
            : null

    console.log('🔍 [AUTH DEBUG] 当前认证上下文', {
        requestId,
        method: event.method,
        path,

        sessionId: maskSessionId(session.id),

        session: {
            authenticated: Boolean(session.user),
            user: session.user ?? null,
        },

        tokenStore: {
            found: true,
            tokenType: tokens.tokenType,
            hasAccessToken: true,
            hasRefreshToken: Boolean(tokens.refreshToken),
            expiresAt,
        },

        jwt: {
            sub: payload
                ? getStringClaim(payload.sub)
                : null,

            iss: payload
                ? getStringClaim(payload.iss)
                : null,

            aud: payload
                ? getAudience(payload.aud)
                : null,

            issuedAt: payload
                && typeof payload.iat === 'number'
                ? new Date(payload.iat * 1000).toISOString()
                : null,

            expiresAt: jwtExpiresAt,
        },
    })
})
