import { Redis } from 'ioredis'
import { getRedisConnectionOptions } from '../../config/redis'

let client: Redis | undefined

export function getAuthRedis(): Redis {
    if (!client) {
        const options = {
            lazyConnect: true,
            connectTimeout: 5_000,
            maxRetriesPerRequest: 1,
        }
        client = process.env.REDIS_URI
            ? new Redis(process.env.REDIS_URI, options)
            : new Redis({ ...getRedisConnectionOptions(), ...options })
        client.on('error', () => {
            console.error('[AUTH REDIS] Connection unavailable.')
        })
    }
    return client
}

export function closeAuthRedis(): void {
    client?.disconnect()
    client = undefined
}
