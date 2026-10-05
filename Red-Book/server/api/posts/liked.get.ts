import type { LikedPostsResponse } from '../../../shared/types/posts'
import { parsePagination } from '../../../shared/schemas/requests'
import { validateRequest } from '../../utils/request-validation'
import { gatewayFetch } from '../../utils/gateway-fetch'

export default defineEventHandler(event => {
    const query = validateRequest(parsePagination, getQuery(event))
    return gatewayFetch<LikedPostsResponse>(event, '/api/v1/posts/liked', { query })
})