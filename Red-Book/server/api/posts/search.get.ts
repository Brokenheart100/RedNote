import type { SearchPostsResponse } from '../../../shared/types/search'
import { parseSearch } from '../../../shared/schemas/requests'
import { validateRequest } from '../../utils/request-validation'
import { gatewayFetch } from '../../utils/gateway-fetch'

export default defineEventHandler(event => {
    const query = validateRequest(parseSearch, getQuery(event))
    return gatewayFetch<SearchPostsResponse>(event, '/api/v1/posts/search', { query, auth: 'optional' })
})