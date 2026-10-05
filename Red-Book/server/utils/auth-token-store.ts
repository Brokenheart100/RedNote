import type { AuthTokenSet } from '../../shared/types/token'
import type { TokenRepository } from './token-refresher'
import { getAuthRedis } from './auth-redis'

const TTL_SECONDS = 60 * 60 * 24 * 7
const LOCK_MS = 60_000

function tokenKey(sessionId: string): string {
    if (!sessionId) throw new Error('Session ID is required.')
    return `rednote:auth-tokens:session:${sessionId}`
}
function lockKey(sessionId: string): string {
    return `${tokenKey(sessionId)}:refresh-lock`
}

export async function setAuthTokenSet(sessionId: string, tokens: AuthTokenSet): Promise<void> {
    if (!tokens.accessToken || !tokens.tokenType) throw new Error('Invalid token set.')
    await getAuthRedis().set(tokenKey(sessionId), JSON.stringify(tokens), 'EX', TTL_SECONDS)
}

export async function getAuthTokenSet(sessionId: string): Promise<AuthTokenSet | null> {
    const value = await getAuthRedis().get(tokenKey(sessionId))
    return value ? JSON.parse(value) as AuthTokenSet : null
}

export async function removeAuthTokenSet(sessionId: string): Promise<void> {
    await getAuthRedis().del(tokenKey(sessionId))
}

export const authTokenRepository: TokenRepository = {
    get: getAuthTokenSet,
    async acquire(sessionId, owner) {
        return await getAuthRedis().set(lockKey(sessionId), owner, 'PX', LOCK_MS, 'NX') === 'OK'
    },
    async release(sessionId, owner) {
        await getAuthRedis().eval(
            "if redis.call('GET', KEYS[1]) == ARGV[1] then return redis.call('DEL', KEYS[1]) end return 0",
            1, lockKey(sessionId), owner,
        )
    },
    async replace(sessionId, expected, next, owner) {
        const result = await getAuthRedis().eval(
            "if redis.call('GET', KEYS[1]) == ARGV[1] and redis.call('GET', KEYS[2]) == ARGV[2] then redis.call('SET', KEYS[1], ARGV[3], 'EX', ARGV[4]); return 1 end return 0",
            2, tokenKey(sessionId), lockKey(sessionId),
            JSON.stringify(expected), owner, JSON.stringify(next), TTL_SECONDS,
        )
        return result === 1
    },
    async removeIfCurrent(sessionId, expected, owner) {
        await getAuthRedis().eval(
            "if redis.call('GET', KEYS[1]) == ARGV[1] and redis.call('GET', KEYS[2]) == ARGV[2] then return redis.call('DEL', KEYS[1]) end return 0",
            2, tokenKey(sessionId), lockKey(sessionId), JSON.stringify(expected), owner,
        )
    },
}
