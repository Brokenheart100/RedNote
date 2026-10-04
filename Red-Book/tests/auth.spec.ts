import {
    expect,
    test,
} from './fixtures/auth'

test(
    'Access Token 即将过期时可以自动刷新',
    async ({
        authenticatedPage,
    }) => {
        const page =
            authenticatedPage

        console.log()
        console.log(
            '🔄 开始测试 Access Token 自动续期',
        )

        /*
         * Fixture 已经保证：
         *
         * 注册 ✅
         * Identity 登录 ✅
         * OIDC ✅
         * Nuxt Session ✅
         * Redis Token Store ✅
         * BFF ✅
         */

        await expect(
            page,
        ).toHaveURL(
            /^http:\/\/localhost:3000\/$/,
        )

        /*
         * 再次调用 BFF。
         *
         * 如果 auth-token-refresh.ts
         * 临时配置：
         *
         * REFRESH_EARLY_MS = 3_600_000
         *
         * 那么这里会触发自动续期。
         */
        const response =
            await page.request.get(
                '/api/users/me',
            )

        console.log(
            '📥 /api/users/me:',
            response.status(),
        )

        expect(
            response.status(),
        ).toBe(
            200,
        )

        const body =
            await response.json()

        console.log(
            '👤 UserService:',
            body,
        )

        /*
         * Refresh 后 Nuxt Session
         * 不应该受到影响。
         */
        const sessionResponse =
            await page.request.get(
                '/api/_auth/session',
            )

        expect(
            sessionResponse.status(),
        ).toBe(
            200,
        )

        const session =
            await sessionResponse.json()

        expect(
            session.user,
        ).toMatchObject({
            authenticated: true,
            provider: 'oidc',
        })

        /*
         * 页面刷新以后仍然要保持登录。
         */
        await page.reload()

        await expect(
            page,
        ).toHaveURL(
            /^http:\/\/localhost:3000\/$/,
        )

        await expect(
            page.getByText(
                /Logged In:\s*true/i,
            ),
        ).toBeVisible()

        const afterReloadResponse =
            await page.request.get(
                '/api/users/me',
            )

        expect(
            afterReloadResponse.status(),
        ).toBe(
            200,
        )

        console.log()
        console.log(
            '✅ Access Token 自动续期测试通过',
        )
    },
)