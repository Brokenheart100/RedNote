interface FetchErrorLike {
    status?: number
    statusCode?: number
    data?: unknown
}

function asFetchError(error: unknown): FetchErrorLike | null {
    if (typeof error !== 'object' || error === null || Array.isArray(error)) {
        return null
    }

    return error as FetchErrorLike
}

export function getFetchErrorStatusCode(error: unknown, fallback = 500): number {
    const fetchError = asFetchError(error)
    return fetchError?.statusCode ?? fetchError?.status ?? fallback
}

export function getFetchErrorData(error: unknown): unknown {
    return asFetchError(error)?.data
}