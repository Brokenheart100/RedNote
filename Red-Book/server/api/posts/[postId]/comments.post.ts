import type { PostCommentResponse } from '../../../../shared/types/posts'
import { parseId, parseComment } from '../../../../shared/schemas/requests'
import { readJsonRequest } from '~~/server/utils/limited-body'
import { gatewayFetch } from '../../../utils/gateway-fetch'

export default defineEventHandler(async event => {
    const postId = validateRequest(parseId, getRouterParam(event, 'postId'))
    const body = await readJsonRequest(event, parseComment)
    const comment = await gatewayFetch<PostCommentResponse>(
        event, `/api/v1/posts/${postId}/comments`, { method: 'POST', body },
    )
    setResponseStatus(event, 201)
    return comment
})