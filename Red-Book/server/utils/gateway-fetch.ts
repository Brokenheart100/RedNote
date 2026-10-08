import { eventLogger } from '~~/server/utils/server-logger'
import type { H3Event } from 'h3'
import type { FetchOptions } from 'ofetch'
import { createTracedFetch } from './traced-fetch'
import { requireAuthenticatedToken } from './authenticated-token'
import { getFetchErrorData, getFetchErrorStatusCode } from './fetch-error'

type GatewayOptions = Pick<FetchOptions, 'method' | 'body' | 'query' | 'headers'> & {
    auth?: 'required' | 'optional' | 'none'
    timeout?: number
}

export async function gatewayFetch<T>(
    event: H3Event,
    path: string,
    options: GatewayOptions = {},
): Promise<T> {
    if (!path.startsWith('/api/v1/')) throw new Error('Invalid Gateway API path.')
    const config = useRuntimeConfig(event)
    if (!config.gatewayBaseUrl) {
        throw createError({ statusCode: 500, statusMessage: 'Gateway is not configured.' })
    }
    const requestId = event.context.requestId ?? crypto.randomUUID()
    event.context.requestId = requestId
    const headers = new Headers(options.headers)
    if (options.method === 'POST' && (path === '/api/v1/posts' || /^\/api\/v1\/posts\/[^/]+\/comments$/.test(path))) {
        const idempotencyKey = getHeader(event, 'Idempotency-Key')
        if (idempotencyKey !== undefined) {
            if (!idempotencyKey.trim() || idempotencyKey.length > 128) {
                throw createError({ statusCode: 400, statusMessage: 'Invalid Idempotency-Key.' })
            }
            headers.set('Idempotency-Key', idempotencyKey)
        }
    }
    headers.set('X-Request-ID', requestId)
    const auth = options.auth ?? 'required'
    if (auth === 'required' || (auth === 'optional' && (await getUserSession(event)).user)) {
        const token = await requireAuthenticatedToken(event, requestId)
        headers.set('Authorization', token.authorization)
    }
    const started = performance.now()
    const upstreamFetch = createTracedFetch(event)
    try {
        return await upstreamFetch<T>(path, {
            baseURL: config.gatewayBaseUrl,
            method: options.method,
            body: options.body,
            query: options.query,
            headers,
            timeout: options.timeout ?? 15_000,
            // Mutations must not be replayed after an ambiguous network failure.
            retry: 0,
        })
    }
    catch (error) {
        const statusCode = getFetchErrorStatusCode(error, 502)
        eventLogger(event).warn('[GATEWAY] Request failed', { requestId, path, statusCode })
        throw createError({
            statusCode,
            statusMessage: statusCode === 401 ? 'Unauthorized' : 'Upstream request failed.',
            data: getFetchErrorData(error),
        })
    }
    finally {
        eventLogger(event).info('[GATEWAY]', {
            requestId, path, method: options.method ?? 'GET',
            durationMs: Math.round(performance.now() - started),
        })
    }
}
