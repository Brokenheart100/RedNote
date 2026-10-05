import type { z } from 'zod'
import type { mediaUploadResponseSchema } from '../schemas/media'

export type MediaUploadResponse = z.output<typeof mediaUploadResponseSchema>
