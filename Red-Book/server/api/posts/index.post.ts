import type { PostResponse } from '../../../shared/types/posts'
import { parsePost } from '../../../shared/schemas/requests'
import { readJsonRequest } from '~~/server/utils/limited-body'
import { gatewayFetch } from '../../utils/gateway-fetch'

export default defineEventHandler(async event => {
    const body = await readJsonRequest(event, parsePost)
    const post = await gatewayFetch<PostResponse>(event, '/api/v1/posts', { method: 'POST', body })
    setResponseStatus(event, 201)
    return post
})