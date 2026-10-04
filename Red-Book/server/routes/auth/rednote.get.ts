// Red-Book/server/routes/auth/rednote.get.ts

import { getAuthTokenSet, setAuthTokenSet } from "~~/server/utils/auth-token-store"

export default defineOAuthOidcEventHandler({
    config: {
        scope: [
            'openid',
            'profile',
            'email',
            'offline_access',
            'rednote-api',
        ],
    },

    async onSuccess(
        event,
        {
            tokens,
        },
    ) {
        console.log()
        console.log(
            '============================================================',
        )

        console.log(
            '🔐✅ [OIDC] RedNote OIDC 回调成功',
        )

        console.log(
            '🔑 [OIDC] Access Token:',
            tokens.access_token
                ? '✅ 已获取'
                : '❌ 未获取',
        )

        console.log(
            '🔄 [OIDC] Refresh Token:',
            tokens.refresh_token
                ? '✅ 已获取'
                : '⚠️ 未获取',
        )

        console.log(
            '🎫 [OIDC] Token Type:',
            tokens.token_type
            ?? '(none)',
        )

        console.log(
            '⏱️ [OIDC] Expires In:',
            tokens.expires_in
            ?? '(none)',
        )

        if (!tokens.access_token) {
            throw new Error(
                'OIDC access token is missing.',
            )
        }

        const tokenType =
            tokens.token_type
            ?? 'Bearer'

        const expiresAt =
            typeof tokens.expires_in
                === 'number'
                ? Date.now()
                + tokens.expires_in * 1000
                : undefined

        /*
         * 第一步：
         * 建立一个非常小的 nuxt-auth-utils Session。
         *
         * 不再把 access token / refresh token
         * 塞进 Cookie。
         */
        console.log(
            '💾 [OIDC] 正在建立最小浏览器 Session...',
        )

        await setUserSession(
            event,
            {
                user: {
                    authenticated: true,
                    provider: 'oidc',
                },

                loggedInAt:
                    Date.now(),
            },
        )

        /*
         * 第二步：
         * 取得 nuxt-auth-utils 生成的 Session ID。
         *
         * Redis Token Store 使用这个 ID
         * 关联真正的 OAuth Token。
         */
        const session =
            await getUserSession(
                event,
            )

        if (!session.id) {
            throw new Error(
                'nuxt-auth-utils session ID is unavailable.',
            )
        }

        console.log(
            '🆔 [OIDC] Session ID:',
            session.id,
        )

        /*
         * 第三步：
         * Token 只进入 Redis。
         */
        console.log(
            '🗄️ [OIDC] 正在写入 Redis Token Store...',
        )

        await setAuthTokenSet(
            session.id,
            {
                accessToken:
                    tokens.access_token,

                refreshToken:
                    tokens.refresh_token,

                tokenType,

                expiresAt,
            },
        )

        console.log(
            '✅ [OIDC] Redis Token Store 写入完成',
        )

        /*
         * 为了调试，仅检查 Redis 是否可读。
         * 不输出真实 Token。
         */
        const storedTokens =
            await getAuthTokenSet(
                session.id,
            )

        console.log(
            '🔎 [OIDC] Redis Token 检查:',
            {
                found:
                    storedTokens !== null,

                hasAccessToken:
                    Boolean(
                        storedTokens?.accessToken,
                    ),

                hasRefreshToken:
                    Boolean(
                        storedTokens?.refreshToken,
                    ),

                tokenType:
                    storedTokens?.tokenType
                    ?? null,

                expiresAt:
                    storedTokens?.expiresAt
                    ?? null,
            },
        )

        console.log(
            '📦 [OIDC] Browser Session:',
            {
                id:
                    session.id,

                user:
                    session.user,

                loggedInAt:
                    session.loggedInAt,
            },
        )

        console.log(
            '🏠 [OIDC] 准备返回 302 → /',
        )

        console.log(
            '============================================================',
        )

        return sendRedirect(
            event,
            '/',
            302,
        )
    },

    onError(
        event,
        error,
    ) {
        console.error()
        console.error(
            '============================================================',
        )

        console.error(
            '🔐❌ [OIDC] RedNote OIDC 登录失败',
        )

        if (
            error instanceof Error
        ) {
            console.error(
                '❌ [OIDC] Name:',
                error.name,
            )

            console.error(
                '❌ [OIDC] Message:',
                error.message,
            )

            console.error(
                '❌ [OIDC] Stack:',
                error.stack,
            )
        }
        else {
            console.error(
                '❌ [OIDC] Error:',
                error,
            )
        }

        console.error(
            '↩️ [OIDC] 返回 /login?oidcError=1',
        )

        console.error(
            '============================================================',
        )

        return sendRedirect(
            event,
            '/login?oidcError=1',
            302,
        )
    },
})