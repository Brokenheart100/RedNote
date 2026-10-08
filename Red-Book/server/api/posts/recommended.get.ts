import type { RecommendationFeedResponse } from '../../../shared/types/posts'
import { parseRecommendationQuery } from '../../../shared/schemas/recommendations'
import { validateRequest } from '../../utils/request-validation'
import { gatewayFetch } from '../../utils/gateway-fetch'

export default defineEventHandler(event => {
    const query = validateRequest(parseRecommendationQuery, getQuery(event))
    return gatewayFetch<RecommendationFeedResponse>(event, '/api/v1/posts/recommended', { query, auth: 'optional' })
})
