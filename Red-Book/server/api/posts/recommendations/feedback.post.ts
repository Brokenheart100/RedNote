import { parseRecommendationFeedback } from '../../../../shared/schemas/recommendations'
import { validateRequest } from '../../../utils/request-validation'
import { gatewayFetch } from '../../../utils/gateway-fetch'

export default defineEventHandler(async event => {
    const body = validateRequest(parseRecommendationFeedback, await readBody(event))
    return gatewayFetch(event, '/api/v1/posts/recommendations/feedback', { method: 'POST', body })
})
