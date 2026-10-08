import { gatewayFetch } from '~~/server/utils/gateway-fetch'

export default defineEventHandler(async event => {
    await gatewayFetch(event, '/api/v1/search/history', { method: 'DELETE', auth: 'required' })
    setResponseStatus(event, 204)
    return null
})
