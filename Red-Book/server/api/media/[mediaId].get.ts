import { parseId } from '../../../shared/schemas/requests'
import { validateRequest } from '../../utils/request-validation'
import { gatewayFetch } from '../../utils/gateway-fetch'

export default defineEventHandler(async event => {
    const mediaId = validateRequest(parseId, getRouterParam(event, 'mediaId'))
    const media = await gatewayFetch<{ url: string }>(event, `/api/v1/media/${mediaId}`, {
        auth: 'optional',
    })
    if (!media.url) {
        throw createError({ statusCode: 502, statusMessage: 'Media URL is unavailable.' })
    }
    return sendRedirect(event, media.url, 302)
})