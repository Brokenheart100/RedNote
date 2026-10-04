import {
    expect,
    test,
} from './fixtures/auth'

test(
    '用户可以完整退出登录',
    async ({
        authenticatedPage,
    }) => {
        const page =
            authenticatedPage

        console.log()
        console.log(
            '🚪 开始测试完整退出登录流程',
        )

        /*
         * =====================================================
         * 1. 确认测试开始前已经登录
         * =====================================================
         */

        const sessionBeforeResponse =
            await page.request.get(
                '/api/_auth/session',
            )

        expect(
            sessionBeforeResponse.status(),
        ).toBe(
            200,
        )

        const sessionBefore =
            await sessionBeforeResponse.json()

        console.log(
            '🔐 Logout 前 Session:',
            sessionBefore,
        )

        expect(
            sessionBefore.user,
        ).toMatchObject({
            authenticated: true,
            provider: 'oidc',
        })

        /*
         * =====================================================
         * 2. 确认 BFF 当前可访问
         * =====================================================
         */

        const meBeforeResponse =
            await page.request.get(
                '/api/users/me',
            )

        console.log(
            '👤 Logout 前 /api/users/me:',
            meBeforeResponse.status(),
        )

        expect(
            meBeforeResponse.status(),
        ).toBe(
            200,
        )

        /*
         * =====================================================
         * 3. 检查登录前 Cookie
         * =====================================================
         */

        const cookiesBefore =
            await page.context()
                .cookies()

        const cookieNamesBefore =
            cookiesBefore.map(
                cookie =>
                    cookie.name,
            )

        console.log(
            '🍪 Logout 前 Cookies:',
            cookieNamesBefore,
        )

        expect(
            cookieNamesBefore,
        ).toContain(
            'nuxt-session',
        )

        expect(
            cookieNamesBefore,
        ).toContain(
            '__Host-RedNote.Identity',
        )

        /*
         * =====================================================
         * 4. 打开用户菜单
         * =====================================================
         *
         * 这里不依赖用户名。
         *
         * 找右上角包含头像的按钮即可。
         */

        const userMenuButton =
            page
                .locator('header')
                .getByRole(
                    'button',
                )
                .filter({
                    has:
                        page.locator(
                            '[data-slot="avatar"], img',
                        ),
                })
                .last()

        /*
         * Nuxt UI 的实际 DOM 如果没有 data-slot，
         * 上面的选择器可能匹配不到。
         *
         * 所以优先通过 chevron 图标所在按钮查找。
         */
        const menuTrigger =
            page.locator(
                'header button',
            ).filter({
                has:
                    page.locator(
                        '.i-lucide-chevron-down',
                    ),
            })

        if (
            await menuTrigger.count()
            > 0
        ) {
            await menuTrigger
                .first()
                .click()
        }
        else {
            await userMenuButton.click()
        }

        /*
         * =====================================================
         * 5. 点击退出登录
         * =====================================================
         */

        const logoutButton =
            page.getByText(
                '退出登录',
                {
                    exact: true,
                },
            )

        await expect(
            logoutButton,
        ).toBeVisible()

        const logoutResponsePromise =
            page.waitForResponse(
                response => {
                    return (
                        response
                            .url()
                            .includes(
                                '/api/auth/logout',
                            )
                        &&
                        response
                            .request()
                            .method()
                        === 'POST'
                    )
                },
                {
                    timeout:
                        15_000,
                },
            )

        await logoutButton.click()

        const logoutResponse =
            await logoutResponsePromise

        console.log(
            '📤 POST /api/auth/logout:',
            logoutResponse.status(),
        )

        expect(
            logoutResponse.status(),
        ).toBe(
            200,
        )

        /*
         * =====================================================
         * 6. 应跳转到 /login
         * =====================================================
         */

        await expect(
            page,
        ).toHaveURL(
            /\/login(?:\?.*)?$/,
            {
                timeout:
                    15_000,
            },
        )

        console.log(
            '✅ 已跳转到 /login',
        )

        /*
         * =====================================================
         * 7. 检查 nuxt-auth-utils Session
         * =====================================================
         */

        const sessionAfterResponse =
            await page.request.get(
                '/api/_auth/session',
            )

        expect(
            sessionAfterResponse.status(),
        ).toBe(
            200,
        )

        const sessionAfter =
            await sessionAfterResponse.json()

        console.log(
            '🔓 Logout 后 Session:',
            sessionAfter,
        )

        /*
         * 不对整个 JSON 做完全相等判断，
         * 因为 auth-utils 以后可能增加字段。
         *
         * 这里只验证已经没有已认证用户。
         */
        expect(
            sessionAfter.user
                ?.authenticated,
        ).not.toBe(
            true,
        )

        /*
         * =====================================================
         * 8. BFF 必须拒绝访问
         * =====================================================
         */

        const meAfterResponse =
            await page.request.get(
                '/api/users/me',
            )

        console.log(
            '⛔ Logout 后 /api/users/me:',
            meAfterResponse.status(),
        )

        expect(
            meAfterResponse.status(),
        ).toBe(
            401,
        )

        /*
         * =====================================================
         * 9. 验证认证 Cookie 已清除
         * =====================================================
         */

        const cookiesAfter =
            await page.context()
                .cookies()

        const cookieNamesAfter =
            cookiesAfter.map(
                cookie =>
                    cookie.name,
            )

        console.log(
            '🍪 Logout 后 Cookies:',
            cookieNamesAfter,
        )

        expect(
            cookieNamesAfter,
        ).not.toContain(
            'nuxt-session',
        )

        expect(
            cookieNamesAfter,
        ).not.toContain(
            '__Host-RedNote.Identity',
        )

        /*
         * 注意：
         *
         * __Host-RedNote.Antiforgery
         *
         * 可以继续存在。
         *
         * Antiforgery Cookie 不代表用户处于登录状态，
         * 所以这里不检查它必须消失。
         */

        /*
         * =====================================================
         * 10. 受保护首页应无法重新进入
         * =====================================================
         */

        await page.goto(
            '/',
        )

        await expect(
            page,
        ).toHaveURL(
            /\/login(?:\?.*)?$/,
            {
                timeout:
                    15_000,
            },
        )

        console.log(
            '🔒 Logout 后访问 / 已被 auth middleware 拦截',
        )

        console.log()
        console.log(
            '🎉 完整退出登录 E2E 测试通过',
        )

        console.log(
            '✅ Nuxt Session cleared',
        )

        console.log(
            '✅ Redis Token unavailable',
        )

        console.log(
            '✅ Identity Cookie cleared',
        )

        console.log(
            '✅ BFF returns 401',
        )

        console.log(
            '✅ Protected route redirects to login',
        )
    },
)