interface FetchErrorLike {
    status?: number
    statusCode?: number
    data?: unknown
}

function asFetchError(error: unknown): FetchErrorLike | null {
    return typeof error === 'object' && error !== null && !Array.isArray(error)
        ? error as FetchErrorLike : null
}

export function getFetchErrorStatusCode(error: unknown, fallback = 502): number {
    const value = asFetchError(error)?.statusCode ?? asFetchError(error)?.status
    return typeof value === 'number' && value >= 400 && value <= 599 ? value : fallback
}

// Preserve client validation messages; internal exception details never cross
// the BFF boundary.
export function getFetchErrorData(error: unknown): { errors: Record<string, string[]> } | undefined {
    if (getFetchErrorStatusCode(error) >= 500) return undefined
    const data = asFetchError(error)?.data
    if (!data || typeof data !== 'object' || !('errors' in data)) return undefined
    const raw = data.errors
    if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return undefined
    const errors: Record<string, string[]> = {}
    for (const [field, messages] of Object.entries(raw)) {
        if (Array.isArray(messages) && messages.every(item => typeof item === 'string')) {
            errors[field] = messages.slice(0, 5).map(message => message.slice(0, 500))
        }
    }
    return { errors }
}