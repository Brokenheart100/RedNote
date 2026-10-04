interface OidcDiscoveryDocument {
    token_endpoint?: string
}

interface RefreshTokenResponse {
    access_token?: string
    refresh_token?: string
    token_type?: string
    expires_in?: number
}

const REFRESH_EARLY_MS =
    60_000

export function shouldRefreshToken(
    tokens: AuthTokenSet,
): boolean {
    if (
        typeof tokens.expiresAt
        !== 'number'
    ) {
        return false
    }

    return (
        Date.now()
        >= tokens.expiresAt
        - REFRESH_EARLY_MS
    )
}

export async function refreshAuthTokenSet(
    event: Parameters<
        typeof useRuntimeConfig
    >[0],
    sessionId: string,
    requestId?: string,
): Promise<AuthTokenSet> {
    const currentTokens =
        await getAuthTokenSet(
            sessionId,
        )

    if (!currentTokens) {
        console.warn(
            '⚠️ [AUTH] 无法刷新 Token：Redis Token Set 不存在',
            {
                requestId,
                sessionId,
            },
        )

        throw createError({
            statusCode: 401,

            statusMessage:
                'Authentication token is unavailable.',
        })
    }

    if (
        !currentTokens.refreshToken
    ) {
        console.warn(
            '⚠️ [AUTH] 无法刷新 Token：Refresh Token 不存在',
            {
                requestId,
                sessionId,
            },
        )

        throw createError({
            statusCode: 401,

            statusMessage:
                'Refresh token is unavailable.',
        })
    }

    const config =
        useRuntimeConfig(
            event,
        )

    const clientId =
        config.oauth.oidc.clientId

    const openidConfigUrl =
        config.oauth.oidc.openidConfig

    if (!clientId) {
        throw new Error(
            'OIDC client ID is unavailable.',
        )
    }

    if (!openidConfigUrl) {
        throw new Error(
            'OIDC discovery URL is unavailable.',
        )
    }

    console.log(
        '🔄 [AUTH] 开始刷新 OIDC Access Token',
        {
            requestId,
            sessionId,

            hasRefreshToken:
                true,
        },
    )

    /*
     * =========================================================
     * 1. 读取 OIDC Discovery Document
     * =========================================================
     */

    let discovery:
        OidcDiscoveryDocument

    try {
        discovery =
            await $fetch<
                OidcDiscoveryDocument
            >(
                openidConfigUrl,
            )
    }
    catch (error: unknown) {
        console.error(
            '❌ [AUTH] 获取 OIDC Discovery Document 失败',
            {
                requestId,
                sessionId,

                error:
                    error instanceof Error
                        ? error.message
                        : String(error),
            },
        )

        throw createError({
            statusCode: 502,

            statusMessage:
                'OIDC discovery request failed.',

            cause:
                error,
        })
    }

    const tokenEndpoint =
        discovery.token_endpoint

    if (!tokenEndpoint) {
        throw new Error(
            'OIDC discovery document does not contain token_endpoint.',
        )
    }

    /*
     * =========================================================
     * 2. Refresh Token Grant
     * =========================================================
     *
     * RedNote Web 是 Public Client。
     *
     * 所以这里：
     *
     * - client_id ✅
     * - client_secret ❌
     * - refresh_token ✅
     */

    const body =
        new URLSearchParams()

    body.set(
        'grant_type',
        'refresh_token',
    )

    body.set(
        'client_id',
        clientId,
    )

    body.set(
        'refresh_token',
        currentTokens.refreshToken,
    )

    let response:
        RefreshTokenResponse

    try {
        response =
            await $fetch<
                RefreshTokenResponse
            >(
                tokenEndpoint,
                {
                    method:
                        'POST',

                    headers: {
                        'Content-Type':
                            'application/x-www-form-urlencoded',
                    },

                    body,
                },
            )
    }
    catch (error: unknown) {
        const statusCode =
            getFetchErrorStatusCode(
                error,
                502,
            )

        if (
            statusCode === 400
            || statusCode === 401
        ) {
            console.warn(
                '⚠️ [AUTH] Refresh Token 已失效或被拒绝',
                {
                    requestId,
                    sessionId,
                    statusCode,
                },
            )

            /*
             * Refresh Token 已不可用。
             *
             * 删除服务端 Token Set，
             * 防止之后每个请求都继续尝试刷新。
             */
            await removeAuthTokenSet(
                sessionId,
            )

            throw createError({
                statusCode: 401,

                statusMessage:
                    'Authentication session has expired.',

                cause:
                    error,
            })
        }

        console.error(
            '❌ [AUTH] OIDC Token Endpoint 请求失败',
            {
                requestId,
                sessionId,
                statusCode,
            },
        )

        throw createError({
            statusCode: 502,

            statusMessage:
                'OIDC token refresh failed.',

            cause:
                error,
        })
    }

    /*
     * =========================================================
     * 3. 验证响应
     * =========================================================
     */

    if (
        !response.access_token
    ) {
        console.error(
            '❌ [AUTH] Refresh Token 响应缺少 Access Token',
            {
                requestId,
                sessionId,
            },
        )

        throw createError({
            statusCode: 502,

            statusMessage:
                'OIDC refresh response is invalid.',
        })
    }

    const tokenType =
        response.token_type
        ?? currentTokens.tokenType
        ?? 'Bearer'

    const expiresAt =
        typeof response.expires_in
            === 'number'
            ? Date.now()
            + response.expires_in
            * 1000
            : undefined

    /*
     * Refresh Token Rotation：
     *
     * 如果 OpenIddict 返回新的 refresh_token，
     * 必须保存新的。
     *
     * 如果没有返回，则继续使用旧的。
     */
    const refreshToken =
        response.refresh_token
        ?? currentTokens.refreshToken

    const updatedTokens:
        AuthTokenSet = {
        accessToken:
            response.access_token,

        refreshToken,

        tokenType,

        expiresAt,
    }

    /*
     * =========================================================
     * 4. 更新 Redis
     * =========================================================
     */

    await setAuthTokenSet(
        sessionId,
        updatedTokens,
    )

    console.log(
        '✅ [AUTH] OIDC Access Token 刷新成功',
        {
            requestId,
            sessionId,

            hasAccessToken:
                true,

            hasRefreshToken:
                Boolean(
                    updatedTokens.refreshToken,
                ),

            tokenType:
                updatedTokens.tokenType,

            expiresAt:
                updatedTokens.expiresAt
                ?? null,
        },
    )

    return updatedTokens
}