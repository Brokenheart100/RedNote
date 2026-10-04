import type { MediaUploadResponse } from '~~/shared/types/media'

import { requireAuthenticatedToken } from '~~/server/utils/authenticated-token'
import {
    getFetchErrorData,
    getFetchErrorStatusCode,
} from '~~/server/utils/fetch-error'

export default defineEventHandler(async event => {
    const requestId = event.context.requestId ?? crypto.randomUUID()

    const {
        sessionId,
        authorization,
        tokenType,
        hasRefreshToken,
    } = await requireAuthenticatedToken(event, requestId)

    const parts = await readMultipartFormData(event)
    const filePart = parts?.find(part => part.name === 'file')

    if (!filePart?.filename || filePart.data.length === 0) {
        throw createError({
            statusCode: 400,
            statusMessage: 'Image file is required.',
        })
    }

    /*
     * Buffer<ArrayBufferLike> 不能直接作为 BlobPart，
     * 复制为标准 Uint8Array<ArrayBuffer>。
     */
    const bytes = new Uint8Array(filePart.data.byteLength)
    bytes.set(filePart.data)

    const blob = new Blob([bytes], {
        type: filePart.type ?? 'application/octet-stream',
    })

    const formData = new FormData()
    formData.append('file', blob, filePart.filename)

    const config = useRuntimeConfig(event)

    if (!config.gatewayBaseUrl) {
        throw createError({
            statusCode: 500,
            statusMessage: 'Internal Gateway base URL is not configured.',
        })
    }

    const method = 'POST'
    const path = '/api/v1/media/images'
    const startedAt = performance.now()

    console.log(`🌐 [BFF] ${method} ${path}`, {
        requestId,
        sessionId,
        upstream: config.gatewayBaseUrl,
        fileName: filePart.filename,
        contentType: filePart.type ?? 'application/octet-stream',
        size: filePart.data.byteLength,
        tokenType,
        hasRefreshToken,
    })

    try {
        const response = await $fetch<MediaUploadResponse>(path, {
            baseURL: config.gatewayBaseUrl,
            method,
            headers: {
                Authorization: authorization,
                'X-Request-ID': requestId,
            },
            body: formData,
        })

        console.log(`✅ [BFF] ${method} ${path} ${(performance.now() - startedAt).toFixed(1)}ms`, {
            requestId,
            sessionId,
            mediaId: response.id,
        })

        return response
    }
    catch (error: unknown) {
        const statusCode = getFetchErrorStatusCode(error, 502)
        const duration = performance.now() - startedAt

        if (statusCode >= 500) {
            console.error(`❌ [BFF] ${statusCode} ${method} ${path} ${duration.toFixed(1)}ms`, {
                requestId,
                sessionId,
                fileName: filePart.filename,
                error,
            })
        }
        else {
            console.warn(`⚠️ [BFF] ${statusCode} ${method} ${path} ${duration.toFixed(1)}ms`, {
                requestId,
                sessionId,
                fileName: filePart.filename,
            })
        }

        throw createError({
            statusCode,
            statusMessage: statusCode === 401
                ? 'Unauthorized'
                : 'MediaService request failed.',
            data: getFetchErrorData(error),
        })
    }
})