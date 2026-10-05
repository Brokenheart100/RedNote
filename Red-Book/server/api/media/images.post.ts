import type { MediaUploadResponse } from '../../../shared/types/media'
import { mediaUploadResponseSchema } from '../../../shared/schemas/media'
import { readRequestBuffer } from '../../utils/limited-body'
import { requireAuthenticatedToken } from '../../utils/authenticated-token'
import { gatewayFetch } from '../../utils/gateway-fetch'

export default defineEventHandler(async event => {
    const requestId = event.context.requestId ?? crypto.randomUUID()
    await requireAuthenticatedToken(event, requestId)
    const contentType = getHeader(event, 'content-type') ?? ''
    if (!contentType.startsWith('multipart/form-data;')) {
        throw createError({ statusCode: 400, statusMessage: 'Multipart form data is required.' })
    }
    // Enforce the limit while reading, including requests without Content-Length.
    const bytes = new Uint8Array(await readRequestBuffer(event, 10 * 1024 * 1024 + 64 * 1024))
    const request = new Request('http://localhost/upload', {
        method: 'POST', headers: { 'Content-Type': contentType }, body: bytes,
    })
    let form: FormData
    try { form = await request.formData() }
    catch {
        throw createError({ statusCode: 400, statusMessage: 'Invalid multipart body.' })
    }
    const file = form.get('file')
    if (!(file instanceof File) || file.size === 0 || file.size > 10 * 1024 * 1024
        || !['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) {
        throw createError({ statusCode: 400, statusMessage: 'A JPEG, PNG or WebP image of at most 10 MB is required.' })
    }
    const body = new FormData()
    body.append('file', file, file.name)
    const media = await gatewayFetch<MediaUploadResponse>(event, '/api/v1/media/images', {
        method: 'POST', body, timeout: 60_000,
    })
    const result = mediaUploadResponseSchema.safeParse(media)
    if (!result.success) {
        throw createError({ statusCode: 502, statusMessage: 'Media upload response is invalid.' })
    }
    setResponseStatus(event, 201)
    return result.data
})
