import { gatewayFetch } from '~~/server/utils/gateway-fetch'

export default defineEventHandler(event => gatewayFetch(event, '/api/v1/search/history', { auth: 'required' }))
