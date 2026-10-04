export interface ApiProblemDetails {
    title?: string
    detail?: string
    errors?: Record<string, string[]>
}

interface FetchErrorLike {
    status?: number
    statusCode?: number
    statusMessage?: string
    message?: string
    data?: ApiProblemDetails
}

function asFetchError(error: unknown): FetchErrorLike | null {
    if (typeof error !== 'object' || error === null || Array.isArray(error)) {
        return null
    }

    return error as FetchErrorLike
}

export function getApiErrorStatus(error: unknown): number | undefined {
    const fetchError = asFetchError(error)
    return fetchError?.statusCode ?? fetchError?.status
}

export function getFirstValidationError(
    errors: Record<string, string[]> | undefined,
): string | null {
    if (!errors) {
        return null
    }

    for (const messages of Object.values(errors)) {
        const message = messages.find(value => value.trim().length > 0)

        if (message) {
            return message
        }
    }

    return null
}

export function getApiProblemDetails(error: unknown): ApiProblemDetails | undefined {
    return asFetchError(error)?.data
}

export function getApiErrorMessage(
    error: unknown,
    fallback: string,
): string {
    const fetchError = asFetchError(error)
    const validationMessage = getFirstValidationError(fetchError?.data?.errors)

    return validationMessage
        ?? fetchError?.data?.detail
        ?? fetchError?.data?.title
        ?? fetchError?.statusMessage
        ?? fetchError?.message
        ?? fallback
}