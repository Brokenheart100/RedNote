export interface AuthTokenSet {
    accessToken: string

    refreshToken?: string

    tokenType: string

    expiresAt?: number
}

const TOKEN_STORAGE_NAME =
    'authTokens'

const TOKEN_KEY_PREFIX =
    'session'

const TOKEN_TTL_SECONDS =
    60 * 60 * 24 * 7

function createTokenKey(
    sessionId: string,
): string {
    if (!sessionId) {
        throw new Error(
            'Session ID is required.',
        )
    }

    return (
        `${TOKEN_KEY_PREFIX}:${sessionId}`
    )
}

export async function setAuthTokenSet(
    sessionId: string,
    tokens: AuthTokenSet,
): Promise<void> {
    if (!tokens.accessToken) {
        throw new Error(
            'Access token is required.',
        )
    }

    if (!tokens.tokenType) {
        throw new Error(
            'Token type is required.',
        )
    }

    const storage =
        useStorage(
            TOKEN_STORAGE_NAME,
        )

    const key =
        createTokenKey(
            sessionId,
        )

    await storage.setItem(
        key,
        tokens,
        {
            ttl:
                TOKEN_TTL_SECONDS,
        },
    )
}

export async function getAuthTokenSet(
    sessionId: string,
): Promise<AuthTokenSet | null> {
    const storage =
        useStorage(
            TOKEN_STORAGE_NAME,
        )

    const key =
        createTokenKey(
            sessionId,
        )

    const tokens =
        await storage.getItem<AuthTokenSet>(
            key,
        )

    return tokens
        ?? null
}

export async function removeAuthTokenSet(
    sessionId: string,
): Promise<void> {
    const storage =
        useStorage(
            TOKEN_STORAGE_NAME,
        )

    const key =
        createTokenKey(
            sessionId,
        )

    await storage.removeItem(
        key,
    )
}