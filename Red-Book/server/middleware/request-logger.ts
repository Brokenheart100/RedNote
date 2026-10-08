import { eventLogger } from '~~/server/utils/server-logger'
export default defineEventHandler(
    event => {
        if (event.context.telemetrySpan) return
        if (!import.meta.dev) {
            return
        }

        const requestId =
            event.context.requestId ?? crypto.randomUUID()

        const startedAt =
            performance.now()

        const requestUrl =
            getRequestURL(event)

        const method =
            event.method

        const path =
            requestUrl.pathname

        event.context.requestId =
            requestId

        const hasCookie =
            Boolean(
                getHeader(
                    event,
                    'cookie',
                ),
            )

        const hasAuthorization =
            Boolean(
                getHeader(
                    event,
                    'authorization',
                ),
            )

        const userAgent =
            getHeader(
                event,
                'user-agent',
            )

        eventLogger(event).log(
            `🌐 [HTTP] ${method} ${path}`,
            {
                requestId,
                method,
                path,

                hasCookie,
                hasAuthorization,

                userAgent:
                    userAgent
                    ?? undefined,
            },
        )

        event.node.res.once(
            'finish',
            () => {
                const duration =
                    performance.now()
                    - startedAt

                const statusCode =
                    event.node.res.statusCode

                const summary =
                    `${statusCode} `
                    + `${method} `
                    + `${path} `
                    + `${duration.toFixed(1)}ms`

                if (statusCode >= 500) {
                    eventLogger(event).error(
                        `❌ [HTTP] ${summary}`,
                        {
                            requestId,
                        },
                    )

                    return
                }

                if (statusCode >= 400) {
                    eventLogger(event).warn(
                        `⚠️ [HTTP] ${summary}`,
                        {
                            requestId,
                        },
                    )

                    return
                }

                eventLogger(event).log(
                    `✅ [HTTP] ${summary}`,
                    {
                        requestId,
                    },
                )
            },
        )
    },
)
