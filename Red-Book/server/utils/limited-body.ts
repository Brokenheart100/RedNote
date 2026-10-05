import type { H3Event } from 'h3'
import { createError } from 'h3'
import { validateRequest } from './request-validation'

export async function readLimitedStream(
    stream: AsyncIterable<Uint8Array | string>,
    maximum: number,
): Promise<Buffer> {
    const chunks: Buffer[] = []
    let size = 0
    for await (const chunk of stream) {
        const buffer = typeof chunk === 'string' ? Buffer.from(chunk) : Buffer.from(chunk)
        size += buffer.length
        if (size > maximum) {
            throw createError({ statusCode: 413, statusMessage: 'Request body is too large.' })
        }
        chunks.push(buffer)
    }
    return Buffer.concat(chunks)
}

export async function readJsonRequest<T>(event: H3Event, parser: (value: unknown) => T): Promise<T> {
    const contentType = event.node.req.headers['content-type'] ?? ''
    if (contentType.split(';')[0]?.trim().toLowerCase() !== 'application/json') {
        throw createError({ statusCode: 415, statusMessage: 'JSON content type is required.' })
    }
    const buffer = await readRequestBuffer(event, 64 * 1024)
    let value: unknown
    try { value = JSON.parse(buffer.toString('utf8')) }
    catch {
        throw createError({ statusCode: 400, statusMessage: 'Invalid JSON body.' })
    }
    return validateRequest(parser, value)
}

export async function readRequestBuffer(event: H3Event, maximum: number): Promise<Buffer> {
    try {
        // Keep the socket alive so a rejected oversized request receives 413.
        return await readLimitedStream(event.node.req.iterator({ destroyOnReturn: false }), maximum)
    }
    catch (error) {
        event.node.req.resume()
        throw error
    }
}
