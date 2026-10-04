export default defineEventHandler(
    event => {
        if (!import.meta.dev) {
            return
        }

        const requestId =
            crypto.randomUUID()

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

        console.log(
            `🌐 [HTTP] ${method} ${path}`,
            {
                requestId,
                method,
                path,

                query:
                    requestUrl.search
                    || undefined,

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
                    console.error(
                        `❌ [HTTP] ${summary}`,
                        {
                            requestId,
                        },
                    )

                    return
                }

                if (statusCode >= 400) {
                    console.warn(
                        `⚠️ [HTTP] ${summary}`,
                        {
                            requestId,
                        },
                    )

                    return
                }

                console.log(
                    `✅ [HTTP] ${summary}`,
                    {
                        requestId,
                    },
                )
            },
        )
    },
)