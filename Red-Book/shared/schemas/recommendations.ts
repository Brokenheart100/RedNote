import { z } from 'zod'
import { parseRequest } from './requests'

const feedQuerySchema = z.object({
    pageSize: z.coerce.number().int().min(1).max(50).default(20),
    cursor: z.string().min(1).max(80).optional(),
})
const feedbackSchema = z.object({
    requestId: z.uuid(),
    items: z.array(z.object({ postId: z.uuid(), type: z.enum(['read', 'click']) })).min(1).max(50),
})
export const parseRecommendationQuery = (value: unknown) => parseRequest(feedQuerySchema, value)
export const parseRecommendationFeedback = (value: unknown) => parseRequest(feedbackSchema, value)
export type RecommendationFeedback = z.output<typeof feedbackSchema>
