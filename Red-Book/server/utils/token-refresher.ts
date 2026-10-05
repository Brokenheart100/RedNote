import type { AuthTokenSet } from '../../shared/types/token'

export interface TokenRepository {
    get(sessionId: string): Promise<AuthTokenSet | null>
    acquire(sessionId: string, owner: string): Promise<boolean>
    release(sessionId: string, owner: string): Promise<void>
    replace(sessionId: string, expected: AuthTokenSet, next: AuthTokenSet, owner: string): Promise<boolean>
    removeIfCurrent(sessionId: string, expected: AuthTokenSet, owner: string): Promise<void>
}

export class TokenRefreshError extends Error {
    readonly statusCode: number

    constructor(statusCode: number, message: string) {
        super(message)
        this.statusCode = statusCode
    }
}

export function shouldRefreshToken(tokens: AuthTokenSet, now = Date.now()): boolean {
    return typeof tokens.expiresAt === 'number' && now >= tokens.expiresAt - 60_000
}

// The repository lock coordinates separate Node processes. Conditional writes
// prevent expired lock holders and requests started before logout from writing.
export function createTokenRefresher(
    repository: TokenRepository,
    options: { waitMs?: number; pollMs?: number } = {},
) {
    return async function refresh(
        sessionId: string,
        exchange: (tokens: AuthTokenSet) => Promise<AuthTokenSet>,
    ): Promise<AuthTokenSet> {
        const owner = crypto.randomUUID()
        const deadline = Date.now() + (options.waitMs ?? 25_000)

        while (true) {
            const tokens = await repository.get(sessionId)
            if (!tokens) {
                throw new TokenRefreshError(401, 'Authentication session has expired.')
            }
            if (!shouldRefreshToken(tokens)) {
                return tokens
            }
            if (await repository.acquire(sessionId, owner)) {
                break
            }
            if (Date.now() >= deadline) {
                throw new TokenRefreshError(503, 'Token refresh is busy. Please retry.')
            }
            await new Promise(resolve => setTimeout(resolve, options.pollMs ?? 50))
        }

        try {
            const current = await repository.get(sessionId)
            if (!current) {
                throw new TokenRefreshError(401, 'Authentication session has expired.')
            }
            if (!shouldRefreshToken(current)) {
                return current
            }
            if (!current.refreshToken) {
                throw new TokenRefreshError(401, 'Refresh token is unavailable.')
            }

            let next: AuthTokenSet
            try {
                next = await exchange(current)
            }
            catch (error) {
                if (error instanceof TokenRefreshError && error.statusCode === 401) {
                    await repository.removeIfCurrent(sessionId, current, owner)
                }
                throw error
            }

            if (await repository.replace(sessionId, current, next, owner)) {
                return next
            }

            const latest = await repository.get(sessionId)
            if (!latest) {
                throw new TokenRefreshError(401, 'Authentication session has expired.')
            }
            if (!shouldRefreshToken(latest)) {
                return latest
            }
            throw new TokenRefreshError(503, 'Token refresh lease expired. Please retry.')
        }
        finally {
            await repository.release(sessionId, owner)
        }
    }
}
