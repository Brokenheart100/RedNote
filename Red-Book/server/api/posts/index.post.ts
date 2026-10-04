import type {
    CreatePostRequest,
    PostResponse,
} from '~~/shared/types/posts'

import { requireAuthenticatedToken } from '~~/server/utils/authenticated-token'
import {
    getFetchErrorData,
    getFetchErrorStatusCode,
} from '~~/server/utils/fetch-error'

function validateRequest(body: CreatePostRequest): void {
    const title = body.title?.trim()
    const content = body.content?.trim()

    if (!title) {
        throw createError({
            statusCode: 400,
            statusMessage: 'Title is required.',
        })
    }

    if (title.length > 100) {
        throw createError({
            statusCode: 400,
            statusMessage: 'Title cannot exceed 100 characters.',
        })
    }

    if (!content) {
        throw createError({
            statusCode: 400,
            statusMessage: 'Content is required.',
        })
    }

    if (content.length > 5000) {
        throw createError({
            statusCode: 400,
            statusMessage: 'Content cannot exceed 5000 characters.',
        })
    }

    if (!Array.isArray(body.mediaIds)) {
        throw createError({
            statusCode: 400,
            statusMessage: 'MediaIds must be an array.',
        })
    }

    if (body.mediaIds.length > 9) {
        throw createError({
            statusCode: 400,
            statusMessage: 'A post can contain at most 9 media items.',
        })
    }

    if (!Array.isArray(body.tags)) {
        throw createError({
            statusCode: 400,
            statusMessage: 'Tags must be an array.',
        })
    }

    if (body.tags.length > 10) {
        throw createError({
            statusCode: 400,
            statusMessage: 'A post can contain at most 10 tags.',
        })
    }
}

export default defineEventHandler(async event => {
    const requestId = event.context.requestId ?? crypto.randomUUID()

    const {
        sessionId,
        authorization,
        tokenType,
        hasRefreshToken,
    } = await requireAuthenticatedToken(event, requestId)

    const body = await readBody<CreatePostRequest>(event)

    validateRequest(body)

    const request: CreatePostRequest = {
        title: body.title.trim(),
        content: body.content.trim(),

        mediaIds: [
            ...new Set(body.mediaIds),
        ],

        tags: [
            ...new Set(
                body.tags
                    .map(tag => tag.trim())
                    .filter(tag => tag.length > 0),
            ),
        ],
    }

    const config = useRuntimeConfig(event)

    if (!config.gatewayBaseUrl) {
        throw createError({
            statusCode: 500,
            statusMessage: 'Internal Gateway base URL is not configured.',
        })
    }

    const method = 'POST'
    const path = '/api/v1/posts'
    const startedAt = performance.now()

    console.log(`🌐 [BFF] ${method} ${path}`, {
        requestId,
        sessionId,
        upstream: config.gatewayBaseUrl,
        mediaCount: request.mediaIds.length,
        tagCount: request.tags.length,
        tokenType,
        hasRefreshToken,
    })

    try {
        const post = await $fetch<PostResponse>(path, {
            baseURL: config.gatewayBaseUrl,
            method,
            headers: {
                Authorization: authorization,
                'X-Request-ID': requestId,
            },
            body: request,
        })

        console.log(`✅ [BFF] ${method} ${path} ${(performance.now() - startedAt).toFixed(1)}ms`, {
            requestId,
            sessionId,
            postId: post.id,
        })

        return post
    }
    catch (error: unknown) {
        const statusCode = getFetchErrorStatusCode(error, 502)

        if (statusCode >= 500) {
            console.error(`❌ [BFF] ${statusCode} ${method} ${path}`, {
                requestId,
                sessionId,
                error,
            })
        }
        else {
            console.warn(`⚠️ [BFF] ${statusCode} ${method} ${path}`, {
                requestId,
                sessionId,
            })
        }

        throw createError({
            statusCode,
            statusMessage: statusCode === 401
                ? 'Unauthorized'
                : 'Post creation failed.',
            data: getFetchErrorData(error),
        })
    }
})