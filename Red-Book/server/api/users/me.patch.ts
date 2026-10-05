import type { UpdateMeResponse } from '../../../shared/types/users'
import { parseProfile } from '../../../shared/schemas/requests'
import { readJsonRequest } from '~~/server/utils/limited-body'
import { gatewayFetch } from '../../utils/gateway-fetch'

export default defineEventHandler(async event => {
    const body = await readJsonRequest(event, parseProfile)
    return gatewayFetch<UpdateMeResponse>(event, '/api/v1/users/me', { method: 'PATCH', body })
})