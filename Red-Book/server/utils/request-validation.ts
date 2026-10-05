import { createError } from 'h3'
import { RequestValidationError } from '../../shared/schemas/requests'

export function validateRequest<T>(parser: (value: unknown) => T, value: unknown): T {
    try {
        return parser(value)
    }
    catch (error) {
        if (!(error instanceof RequestValidationError)) throw error
        throw createError({
            statusCode: 400,
            statusMessage: 'Invalid request.',
            data: { errors: { [error.field]: [error.message] } },
        })
    }
}
