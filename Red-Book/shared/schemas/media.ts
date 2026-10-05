import { z } from 'zod'

export const mediaUploadResponseSchema = z.object({
    id: z.guid(),
    fileName: z.string().min(1),
    contentType: z.string().min(1),
    size: z.number().int().nonnegative(),
    objectKey: z.string().min(1),
    createdAtUtc: z.string().min(1),
})
