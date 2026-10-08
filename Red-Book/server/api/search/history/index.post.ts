import { z } from 'zod'
import { readJsonRequest } from '~~/server/utils/limited-body'
import { gatewayFetch } from '~~/server/utils/gateway-fetch'
import { parseRequest } from '~~/shared/schemas/requests'

const schema = z.object({ keyword: z.string().trim().min(1).max(100) })

export default defineEventHandler(async event => {
    const body = await readJsonRequest(event, value => parseRequest(schema, value))
    await gatewayFetch(event, '/api/v1/search/history', { method: 'POST', body, auth: 'required' })
    setResponseStatus(event, 204)
    return null
})
