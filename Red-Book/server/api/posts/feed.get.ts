import type { FeedResponse } from '../../../shared/types/posts'
import { parsePagination } from '../../../shared/schemas/requests'
import { validateRequest } from '../../utils/request-validation'
import { gatewayFetch } from '../../utils/gateway-fetch'

export default defineEventHandler(event => {
    const query = validateRequest(parsePagination, getQuery(event))
    return gatewayFetch<FeedResponse>(event, '/api/v1/posts/feed', { query, auth: 'optional' })
})