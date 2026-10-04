interface ApiDebugContext {
    startedAt: number
    requestId: string
}

const SENSITIVE_KEYS = new Set([
    'password',
    'confirmpassword',
    'accesstoken',
    'access_token',
    'refreshtoken',
    'refresh_token',
    'idtoken',
    'id_token',
    'token',
    'authorization',
    'cookie',
    'set-cookie',
    'clientsecret',
    'client_secret',
])

function sanitize(value: unknown): unknown {
    if (Array.isArray(value)) {
        return value.map(item => sanitize(item))
    }

    if (
        typeof value === 'object'
        && value !== null
    ) {
        const result: Record<string, unknown> = {}

        for (const [key, item] of Object.entries(value)) {
            result[key] =
                SENSITIVE_KEYS.has(key.toLowerCase())
                    ? '***'
                    : sanitize(item)
        }

        return result
    }

    return value
}

function shouldLog(path: string): boolean {
    return (
        path.startsWith('/api/')
        || path.startsWith('/auth/')
    )
}

export default defineNitroPlugin(nitroApp => {
    nitroApp.hooks.hook('request', event => {
        if (!import.meta.dev) {
            return
        }

        const url = getRequestURL(event)

        if (!shouldLog(url.pathname)) {
            return
        }

        const requestId =
            event.context.requestId
            ?? crypto.randomUUID()

        event.context.requestId = requestId

        event.context.apiDebug = {
            startedAt: performance.now(),
            requestId,
        } satisfies ApiDebugContext

        console.log('')
        console.log('━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━')
        console.log(`➡️ [API REQUEST] ${event.method} ${url.pathname}`, {
            requestId,
            url: url.toString(),
            query: sanitize(getQuery(event)),
        })
    })

    nitroApp.hooks.hook(
        'beforeResponse',
        (event, { body }) => {
            if (!import.meta.dev) {
                return
            }

            const url = getRequestURL(event)

            if (!shouldLog(url.pathname)) {
                return
            }

            const context =
                event.context.apiDebug as ApiDebugContext | undefined

            const duration =
                context
                    ? performance.now() - context.startedAt
                    : null

            console.log(`⬅️ [API RESPONSE] ${event.method} ${url.pathname}`, {
                requestId:
                    context?.requestId
                    ?? event.context.requestId
                    ?? null,

                statusCode:
                    event.node.res.statusCode,

                durationMs:
                    duration === null
                        ? null
                        : Number(duration.toFixed(1)),

                body:
                    sanitize(body),
            })

            console.log('━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━')
            console.log('')
        },
    )

    nitroApp.hooks.hook(
        'error',
        (error, { event }) => {
            if (
                !import.meta.dev
                || !event
            ) {
                return
            }

            const url = getRequestURL(event)

            if (!shouldLog(url.pathname)) {
                return
            }

            const context =
                event.context.apiDebug as ApiDebugContext | undefined

            const duration =
                context
                    ? performance.now() - context.startedAt
                    : null

            console.error(`❌ [API ERROR] ${event.method} ${url.pathname}`, {
                requestId:
                    context?.requestId
                    ?? event.context.requestId
                    ?? null,

                statusCode:
                    event.node.res.statusCode,

                durationMs:
                    duration === null
                        ? null
                        : Number(duration.toFixed(1)),

                error:
                    error instanceof Error
                        ? {
                            name: error.name,
                            message: error.message,
                        }
                        : String(error),
            })
        },
    )
})