import { parseId } from '../../../shared/schemas/requests'
import { validateRequest } from '../../utils/request-validation'
import { gatewayFetch } from '../../utils/gateway-fetch'

export default defineEventHandler(async event => {
    const postId = validateRequest(parseId, getRouterParam(event, 'postId'))
    await gatewayFetch<void>(event, `/api/v1/posts/${postId}`, { method: 'DELETE' })
    setResponseStatus(event, 204)
    return null
})
