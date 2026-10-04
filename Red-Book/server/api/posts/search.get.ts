import type { PostResponse } from '~~/shared/types/posts'

import { requireAuthenticatedToken } from '~~/server/utils/authenticated-token'
import {
    getFetchErrorData,
    getFetchErrorStatusCode,
} from '~~/server/utils/fetch-error'

interface SearchPostsResponse {
    page: number
    pageSize: number
    totalCount: number
    items: PostResponse[]
}

export default defineEventHandler(async event => {
    const requestId = event.context.requestId ?? crypto.randomUUID()
    const query = getQuery(event)

    const keyword = typeof query.q === 'string' ? query.q.trim() : ''
    const page = typeof query.page === 'string' ? Number.parseInt(query.page, 10) : 1
    const pageSize = typeof query.pageSize === 'string' ? Number.parseInt(query.pageSize, 10) : 20

    if (!keyword) {
        throw createError({
            statusCode: 400,
            statusMessage: 'Search query is required.',
        })
    }

    if (keyword.length > 100) {
        throw createError({
            statusCode: 400,
            statusMessage: 'Search query cannot exceed 100 characters.',
        })
    }

    if (!Number.isInteger(page) || page < 1) {
        throw createError({
            statusCode: 400,
            statusMessage: 'Page must be greater than or equal to 1.',
        })
    }

    if (!Number.isInteger(pageSize) || pageSize < 1 || pageSize > 100) {
        throw createError({
            statusCode: 400,
            statusMessage: 'PageSize must be between 1 and 100.',
        })
    }

    const config = useRuntimeConfig(event)

    if (!config.gatewayBaseUrl) {
        throw createError({
            statusCode: 500,
            statusMessage: 'Internal Gateway base URL is not configured.',
        })
    }

    /*
     * 搜索允许匿名。
     * 已登录时携带 Access Token，
     * 让后端能够返回当前用户对应的点赞/收藏状态。
     */
    let authorization: string | undefined

    const session = await getUserSession(event)

    if (session.loggedIn) {
        const token = await requireAuthenticatedToken(event, requestId)
        authorization = token.authorization
    }

    const method = 'GET'
    const path = '/api/v1/posts/search'
    const startedAt = performance.now()

    console.log(`🔎 [BFF] ${method} ${path}`, {
        requestId,
        keyword,
        page,
        pageSize,
        authenticated: Boolean(authorization),
    })

    try {
        const response = await $fetch<SearchPostsResponse>(path, {
            baseURL: config.gatewayBaseUrl,
            method,
            query: {
                q: keyword,
                page,
                pageSize,
            },
            headers: {
                'X-Request-ID': requestId,
                ...(authorization ? { Authorization: authorization } : {}),
            },
        })

        console.log(`✅ [BFF] 搜索完成 ${(performance.now() - startedAt).toFixed(1)}ms`, {
            requestId,
            keyword,
            page,
            totalCount: response.totalCount,
            returnedCount: response.items.length,
        })

        return response
    }
    catch (error: unknown) {
        const statusCode = getFetchErrorStatusCode(error)

        console.error(`❌ [BFF] ${statusCode} ${method} ${path} ${(performance.now() - startedAt).toFixed(1)}ms`, {
            requestId,
            keyword,
            page,
            error,
        })

        throw createError({
            statusCode,
            statusMessage: statusCode === 400
                ? 'Invalid search request.'
                : 'Search failed.',
            data: getFetchErrorData(error),
        })
    }
})