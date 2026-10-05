import type { PostCommentsResponse } from '../../../../shared/types/posts'
import { parseId, parsePagination } from '../../../../shared/schemas/requests'
import { validateRequest } from '../../../utils/request-validation'
import { gatewayFetch } from '../../../utils/gateway-fetch'

export default defineEventHandler(event => {
    const postId = validateRequest(parseId, getRouterParam(event, 'postId'))
    const query = validateRequest(parsePagination, getQuery(event))
    return gatewayFetch<PostCommentsResponse>(event, `/api/v1/posts/${postId}/comments`, {
        query, auth: 'optional',
    })
})