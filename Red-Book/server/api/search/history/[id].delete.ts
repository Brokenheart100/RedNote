import { parseId } from '~~/shared/schemas/requests'
import { validateRequest } from '~~/server/utils/request-validation'
import { gatewayFetch } from '~~/server/utils/gateway-fetch'

export default defineEventHandler(async event => {
    const id = validateRequest(parseId, getRouterParam(event, 'id'))
    await gatewayFetch(event, `/api/v1/search/history/${id}`, { method: 'DELETE', auth: 'required' })
    setResponseStatus(event, 204)
    return null
})
