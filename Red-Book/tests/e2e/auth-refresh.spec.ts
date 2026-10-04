import {
    expect,
    test,
} from '@playwright/test'

test(
    'Access Token 即将过期时可以自动刷新',
    async ({ page }) => {
        /*
         * 这个测试的前提：
         *
         * auth-token-refresh.ts 中临时设置：
         *
         * const REFRESH_EARLY_MS = 3_600_000
         *
         * 这样刚登录拿到的约 1 小时 Access Token
         * 会立即进入刷新逻辑。
         */

        console.log()
        console.log(
            '🧪 开始测试 Access Token 自动续期',
        )

        /*
         * ------------------------------------------------------
         * 1. 先完成一次正常登录
         * ------------------------------------------------------
         *
         * 这里暂时直接复用开发环境已经存在的登录页。
         *
         * 如果你当前浏览器 storageState 已经有登录态，
         * 可以直接进入首页。
         *
         * 如果没有，则这个测试后面我们再抽公共登录 fixture。
         */

        await page.goto('/')

        /*
         * 如果当前还没有登录状态，
         * 会被 auth middleware 重定向到 /login。
         */
        if (
            page.url()
                .includes('/login')
        ) {
            throw new Error(
                '当前测试没有登录状态。'
                + '下一步需要给 Playwright 增加共享登录 fixture。',
            )
        }

        await expect(
            page,
        ).toHaveURL(
            /^http:\/\/localhost:3000\/$/,
        )

        /*
         * ------------------------------------------------------
         * 2. 确认当前 Session 已登录
         * ------------------------------------------------------
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

        console.log(
            '🔐 Current Session:',
            session,
        )

        expect(
            session.user,
        ).toMatchObject({
            authenticated: true,
            provider: 'oidc',
        })

        /*
         * ------------------------------------------------------
         * 3. 请求 BFF
         * ------------------------------------------------------
         *
         * 因为 REFRESH_EARLY_MS 被临时设置成 1 小时，
         * 这里应该触发：
         *
         * Access Token
         * → Refresh Token
         * → /connect/token
         * → Redis 更新
         * → Gateway
         * → UserService
         */

        console.log()
        console.log(
            '🔄 请求 /api/users/me，触发 Token 自动刷新...',
        )

        const meResponse =
            await page.request.get(
                '/api/users/me',
            )

        console.log(
            '📥 HTTP:',
            meResponse.status(),
        )

        const meBody =
            await meResponse.text()

        console.log(
            '📦 Response:',
            meBody || '(empty)',
        )

        expect(
            meResponse.status(),
        ).toBe(
            200,
        )

        /*
         * ------------------------------------------------------
         * 4. Refresh 后 Session 仍然有效
         * ------------------------------------------------------
         */

        const sessionAfterRefreshResponse =
            await page.request.get(
                '/api/_auth/session',
            )

        expect(
            sessionAfterRefreshResponse.status(),
        ).toBe(
            200,
        )

        const sessionAfterRefresh =
            await sessionAfterRefreshResponse
                .json()

        expect(
            sessionAfterRefresh.user,
        ).toMatchObject({
            authenticated: true,
            provider: 'oidc',
        })

        console.log()
        console.log(
            '✅ Access Token 自动续期测试通过',
        )
    },
)