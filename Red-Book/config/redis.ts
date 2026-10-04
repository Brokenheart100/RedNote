export interface RedisConnectionOptions {
    host: string
    port: number
    password?: string
    username?: string
    tls?: Record<string, never>
}

export function getRedisConnectionOptions():
    RedisConnectionOptions {
    const connectionString =
        process.env.ConnectionStrings__redis

    if (!connectionString) {
        return {
            host: 'localhost',
            port: 6379,
        }
    }

    const parts = connectionString
        .split(',')
        .map(part => part.trim())
        .filter(Boolean)

    const endpoint = parts.shift()

    if (!endpoint) {
        throw new Error(
            'ConnectionStrings__redis is empty.',
        )
    }

    const separator = endpoint.lastIndexOf(':')

    if (separator <= 0) {
        throw new Error(
            'ConnectionStrings__redis endpoint is invalid.',
        )
    }

    const host = endpoint
        .slice(0, separator)
        .trim()

    const port = Number(
        endpoint
            .slice(separator + 1)
            .trim(),
    )

    if (
        !host
        || !Number.isInteger(port)
        || port < 1
        || port > 65_535
    ) {
        throw new Error(
            'ConnectionStrings__redis endpoint is invalid.',
        )
    }

    const options =
        new Map<string, string>()

    for (const part of parts) {
        const equalsIndex =
            part.indexOf('=')

        if (equalsIndex <= 0) {
            continue
        }

        const key = part
            .slice(0, equalsIndex)
            .trim()
            .toLowerCase()

        const value = part
            .slice(equalsIndex + 1)
            .trim()

        if (key && value) {
            options.set(key, value)
        }
    }

    const password =
        options.get('password')

    const username =
        options.get('user')
        ?? options.get('username')

    const useTls =
        options.get('ssl')?.toLowerCase()
        === 'true'

    return {
        host,
        port,

        ...(username
            ? { username }
            : {}),

        ...(password
            ? { password }
            : {}),

        ...(useTls
            ? { tls: {} }
            : {}),
    }
}