import {
    expect,
    test as base,
    type Page,
} from '@playwright/test'

interface AuthFixture {
    authenticatedPage: Page
}

function createRandomText(
    length: number,
): string {
    const characters =
        'abcdefghijklmnopqrstuvwxyz0123456789'

    return Array.from(
        {
            length,
        },
        () => {
            const index =
                Math.floor(
                    Math.random()
                    * characters.length,
                )

            return characters[index]
        },
    ).join('')
}

async function waitForNuxtHydration(
    page: Page,
): Promise<void> {
    await page.waitForFunction(() => {
        const root =
            document.querySelector(
                '#__nuxt',
            )

        if (!root) {
            return false
        }

        return (
            '__vue_app__'
            in root
        )
    })
}

async function registerAndLogin(
    page: Page,
): Promise<void> {
    const suffix =
        createRandomText(10)

    const email =
        `playwright_${suffix}@example.com`

    const password =
        `RedNote@${createRandomText(12)}Aa1`

    const displayName =
        `Playwright ${suffix}`

    const familyName =
        'RedNote'

    console.log()
    console.log(
        '🧪 创建 Playwright 测试账号',
    )

    console.log(
        `📧 ${email}`,
    )

    /*
     * =========================================================
     * 1. 注册
     * =========================================================
     */

    await page.goto(
        '/register',
    )

    await waitForNuxtHydration(
        page,
    )

    await expect(
        page.getByRole(
            'heading',
            {
                name:
                    '创建 RedNote 账号',
            },
        ),
    ).toBeVisible()

    await page
        .getByPlaceholder(
            '请输入邮箱',
        )
        .fill(
            email,
        )

    await page
        .getByPlaceholder(
            '请输入显示名称',
        )
        .fill(
            displayName,
        )

    await page
        .getByPlaceholder(
            '请输入姓氏',
        )
        .fill(
            familyName,
        )

    await page
        .getByPlaceholder(
            '请输入密码',
        )
        .fill(
            password,
        )

    await page
        .getByPlaceholder(
            '请再次输入密码',
        )
        .fill(
            password,
        )

    const registerResponsePromise =
        page.waitForResponse(
            response => {
                return (
                    response
                        .url()
                        .includes(
                            '/api/v1/auth/register',
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

    await page
        .getByRole(
            'button',
            {
                name: '注册',
            },
        )
        .click()

    const registerResponse =
        await registerResponsePromise

    console.log(
        '📥 Register:',
        registerResponse.status(),
    )

    expect(
        [
            200,
            201,
            204,
        ],
    ).toContain(
        registerResponse.status(),
    )

    await expect(
        page,
    ).toHaveURL(
        /\/login/,
        {
            timeout:
                15_000,
        },
    )

    await waitForNuxtHydration(
        page,
    )

    /*
     * =========================================================
     * 2. 登录
     * =========================================================
     */

    const loginEmail =
        page.getByPlaceholder(
            '请输入邮箱',
        )

    const loginPassword =
        page.getByPlaceholder(
            '请输入密码',
        )

    /*
     * register.vue 在开发环境会通过 sessionStorage
     * 把刚注册的账号密码传给 login.vue。
     *
     * 如果没有自动恢复，
     * fixture 也主动填一次，
     * 避免测试对开发辅助逻辑形成强耦合。
     */
    if (
        await loginEmail
            .inputValue()
        !== email
    ) {
        await loginEmail.fill(
            email,
        )
    }

    if (
        await loginPassword
            .inputValue()
        !== password
    ) {
        await loginPassword.fill(
            password,
        )
    }

    const loginResponsePromise =
        page.waitForResponse(
            response => {
                return (
                    response
                        .url()
                        .includes(
                            '/api/v1/auth/session/login',
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

    await page
        .getByRole(
            'button',
            {
                name: '登录',
            },
        )
        .click()

    const loginResponse =
        await loginResponsePromise

    console.log(
        '📥 Login:',
        loginResponse.status(),
    )

    expect(
        [
            200,
            204,
        ],
    ).toContain(
        loginResponse.status(),
    )

    /*
     * =========================================================
     * 3. 等待 OIDC Authorization Code Flow 完成
     * =========================================================
     */

    await expect(
        page,
    ).toHaveURL(
        /^http:\/\/localhost:3000\/$/,
        {
            timeout:
                30_000,
        },
    )

    await waitForNuxtHydration(
        page,
    )

    /*
     * =========================================================
     * 4. 验证 Auth Utils Session
     * =========================================================
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
     * =========================================================
     * 5. 验证 BFF
     * =========================================================
     */

    const meResponse =
        await page.request.get(
            '/api/users/me',
        )

    expect(
        meResponse.status(),
    ).toBe(
        200,
    )

    console.log(
        '✅ Playwright 登录 Fixture 已完成',
    )
}

export const test =
    base.extend<AuthFixture>({
        authenticatedPage:
            async (
                {
                    page,
                },
                use,
            ) => {
                await registerAndLogin(
                    page,
                )

                await use(
                    page,
                )
            },
    })

export {
    expect,
}