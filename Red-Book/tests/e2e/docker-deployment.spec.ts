import { expect, test, type APIRequestContext } from '@playwright/test'

const frontendUrl = process.env.REDNOTE_FRONTEND_URL ?? 'http://localhost:3000'
const gatewayUrl = process.env.REDNOTE_GATEWAY_URL ?? 'http://localhost:8080'
const mediaUrl = process.env.REDNOTE_MEDIA_URL ?? 'http://localhost:9000'

test('BFF forwards scoped idempotency keys and commits post interactions', async ({ page }) => {
    const email = `dedup_${crypto.randomUUID()}@example.com`
    const password = `Dedup@${crypto.randomUUID()}Aa1`
    const registration = await page.request.post('/api/auth/register', { data: { email, password } })
    expect(registration.status(), await registration.text()).toBe(200)
    const login = await page.request.post('/api/auth/login', { data: { email, password } })
    expect(login.status(), await login.text()).toBe(200)
    await page.goto('/auth/rednote')
    await expect(page).toHaveURL(`${frontendUrl}/`)
    const headers = { 'Idempotency-Key': crypto.randomUUID() }
    const data = { title: 'Idempotency test', content: 'Created once', mediaIds: [], tags: [] }
    const first = await page.request.post('/api/posts', { data, headers })
    expect(first.status(), await first.text()).toBe(201)
    const post = await first.json()
    try {
        const replay = await page.request.post('/api/posts', { data, headers })
        expect(replay.status(), await replay.text()).toBe(201)
        expect(await replay.json()).toEqual(post)
        expect((await page.request.post('/api/posts', { data: { ...data, title: 'Changed body' }, headers })).status()).toBe(422)
        const path = `/api/posts/${post.id}/comments`
        const comment = await page.request.post(path, { data: { content: 'Created once' }, headers })
        expect(comment.status(), await comment.text()).toBe(201)
        const repeatedComment = await page.request.post(path, { data: { content: 'Created once' }, headers })
        expect(repeatedComment.status(), await repeatedComment.text()).toBe(201)
        expect(await repeatedComment.json()).toEqual(await comment.json())
        const comments = await page.request.get(`${path}?page=1&pageSize=20`)
        expect((await comments.json()).totalCount).toBe(1)

        const postPath = `/api/posts/${post.id}`
        for (let attempt = 0; attempt < 2; attempt++) {
            expect((await page.request.post(`${postPath}/likes`)).status()).toBe(204)
        }
        const detailPath = `${gatewayUrl}/api/v1/posts/${post.id}`
        const liked = await page.request.get(detailPath)
        expect(liked.status(), await liked.text()).toBe(200)
        expect((await liked.json()).likeCount).toBe(1)
        expect((await page.request.delete(`${postPath}/likes`)).status()).toBe(204)
        const unliked = await page.request.get(detailPath)
        expect(unliked.status(), await unliked.text()).toBe(200)
        expect((await unliked.json()).likeCount).toBe(0)
    }
    finally {
        expect((await page.request.delete(`/api/posts/${post.id}`)).status()).toBe(204)
    }
})

test('Docker: search history persists, deduplicates, limits concurrent writes and isolates users', async ({ playwright, baseURL }) => {
    const options = { baseURL, ignoreHTTPSErrors: test.info().project.use.ignoreHTTPSErrors }
    const owner = await playwright.request.newContext(options)
    const other = await playwright.request.newContext(options)
    const path = '/api/search/history'
    try {
        expect((await owner.get(path)).status()).toBe(401)
        expect((await owner.post(path, { data: { keyword: 'anonymous' } })).status()).toBe(401)
        expect((await owner.delete(path)).status()).toBe(401)
        expect((await owner.get(`${gatewayUrl}/api/v1/search/history`)).status()).toBe(401)
        for (const client of [owner, other]) {
            const email = `history_${crypto.randomUUID()}@example.com`
            const password = `History@${crypto.randomUUID()}Aa1`
            const register = await client.post('/api/auth/register', {
                data: { email, password, displayName: 'History test', familyName: 'Test' },
            })
            expect(register.status(), await register.text()).toBe(200)
            const login = await client.post('/api/auth/login', { data: { email, password } })
            expect(login.status(), await login.text()).toBe(200)
            const authorization = await client.get('/auth/rednote')
            expect(authorization.status()).toBe(200)
            expect(authorization.url()).toBe(`${frontendUrl}/`)
            const session = await (await client.get('/api/_auth/session')).json()
            expect(session.user.authenticated).toBe(true)
        }
        const read = async (client: APIRequestContext) => {
            const response = await client.get(path)
            expect(response.status(), await response.text()).toBe(200)
            return (await response.json()).items as { id: string, keyword: string, lastSearchedAtUtc: string }[]
        }
        for (const keyword of ['', '   ', 'x'.repeat(101)]) {
            expect((await owner.post(path, { data: { keyword } })).status()).toBe(400)
        }
        for (const keyword of [' Nuxt ', 'nuxt']) {
            const response = await owner.post(path, { data: { keyword } })
            expect(response.status(), await response.text()).toBe(204)
        }
        const first = await read(owner)
        expect(first).toHaveLength(1)
        expect(first[0]!.keyword).toBe('nuxt')
        expect(await read(other)).toEqual([])
        expect((await other.delete(`${path}/${first[0]!.id}`)).status()).toBe(204)
        expect(await read(owner)).toEqual(first)
        expect((await other.post(path, { data: { keyword: 'other-user-history' } })).status()).toBe(204)

        await Promise.all(Array.from({ length: 25 }, async (_, index) => {
            const response = await owner.post(path, { data: { keyword: `concurrent-${index}` } })
            expect(response.status(), await response.text()).toBe(204)
        }))
        const recent = await read(owner)
        expect(recent).toHaveLength(20)
        expect(new Set(recent.map(item => item.keyword)).size).toBe(20)
        expect((await owner.post(path, { data: { keyword: 'latest-search' } })).status()).toBe(204)
        const latest = await read(owner)
        expect(latest).toHaveLength(20)
        expect(latest[0]!.keyword).toBe('latest-search')
        // A fresh client retains only the session cookies, so history must come from the server.
        const resumed = await playwright.request.newContext({ ...options, storageState: await owner.storageState() })
        try { expect(await read(resumed)).toEqual(latest) }
        finally { await resumed.dispose() }
        expect((await owner.delete(`${path}/${latest[0]!.id}`)).status()).toBe(204)
        expect(await read(owner)).toHaveLength(19)
        expect((await owner.delete(path)).status()).toBe(204)
        expect(await read(owner)).toEqual([])
        expect((await read(other)).map(item => item.keyword)).toEqual(['other-user-history'])
        expect((await other.delete(path)).status()).toBe(204)
    }
    finally {
        await owner.dispose()
        await other.dispose()
    }
})

test('shared validation blocks invalid login and registration before HTTP requests', async ({ page }) => {
    const submitted: string[] = []
    page.on('request', request => {
        const path = new URL(request.url()).pathname
        if (request.method() === 'POST' && ['/api/auth/login', '/api/auth/register'].includes(path)) {
            submitted.push(path)
        }
    })
    await page.goto('/login')
    await page.waitForFunction(() => {
        const root = document.querySelector('#__nuxt') as HTMLElement & { __vue_app__?: { $nuxt?: { isHydrating: boolean } } } | null
        return root?.__vue_app__?.$nuxt?.isHydrating === false
    })
    await page.getByPlaceholder('请输入邮箱', { exact: true }).fill('schema@example.com')
    await page.getByPlaceholder('请输入密码', { exact: true }).fill('x'.repeat(1025))
    await page.getByRole('button', { name: '登录', exact: true }).click()
    await expect(page.getByText('密码不能为空，且不能超过 1024 个字符。')).toBeVisible()
    await page.goto('/register')
    await page.waitForFunction(() => {
        const root = document.querySelector('#__nuxt') as HTMLElement & { __vue_app__?: { $nuxt?: { isHydrating: boolean } } } | null
        return root?.__vue_app__?.$nuxt?.isHydrating === false
    })
    await page.getByPlaceholder('请输入邮箱', { exact: true }).fill('schema@example.com')
    await page.getByPlaceholder('请输入显示名称').fill('x'.repeat(65))
    await page.getByPlaceholder('请输入密码', { exact: true }).fill('Schema@Password1')
    await page.getByPlaceholder('请再次输入密码').fill('Schema@Password1')
    await page.getByRole('button', { name: '注册', exact: true }).click()
    await expect(page.getByText('显示名称不能超过 64 个字符。')).toBeVisible()
    expect(submitted).toEqual([])
})

test('Docker: OIDC, Redis session, upload, gRPC, search, deletion and logout', async ({ page }) => {
    await page.clock.install()
    const email = `docker_${crypto.randomUUID()}@example.com`
    const password = `Docker@${crypto.randomUUID()}Aa1`
    const register = await page.request.post('/api/auth/register', {
        data: { email, password, displayName: 'Docker smoke', familyName: 'Test' },
    })
    expect(register.status(), await register.text()).toBe(200)
    const login = await page.request.post('/api/auth/login', { data: { email, password } })
    expect(login.status(), await login.text()).toBe(200)
    await page.goto('/auth/rednote')
    await expect(page).toHaveURL(`${frontendUrl}/`)
    const me = await page.request.get('/api/users/me')
    expect(me.status(), await me.text()).toBe(200)
    const session = await (await page.request.get('/api/_auth/session')).json()
    expect(session.user).toMatchObject({ authenticated: true, provider: 'oidc' })
    expect(session).not.toHaveProperty('accessToken')
    expect(session).not.toHaveProperty('refreshToken')
    if (frontendUrl.startsWith('https://')) {
        const identityCookies = (await page.context().cookies()).filter(cookie => cookie.name.startsWith('RedNote.Identity'))
        expect(identityCookies.length).toBeGreaterThan(0)
        expect(identityCookies.every(cookie => cookie.secure)).toBe(true)
        const discovery = await page.request.get(`${gatewayUrl}/.well-known/openid-configuration`)
        expect(discovery.status()).toBe(200)
        const configuration = await discovery.json()
        expect(configuration.issuer).toBe(`${gatewayUrl}/`)
        expect(new URL(configuration.authorization_endpoint).origin).toBe(new URL(gatewayUrl).origin)
    }

    const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAXSURBVBhXY/jPwPCfoYHhPwMDw38wAABD1Al4TlSdlQAAAABJRU5ErkJggg==', 'base64')
    const uploaded = await page.request.post('/api/media/images', {
        multipart: { file: { name: 'docker-smoke.png', mimeType: 'image/png', buffer: png } },
    })
    expect(uploaded.status(), await uploaded.text()).toBe(201)
    const media = await uploaded.json()
    const avatarPath = `/api/media/${media.id}`
    const profile = await page.request.patch('/api/users/me', {
        data: { nickname: 'Docker avatar', avatarUrl: avatarPath, bio: 'Avatar regression' },
    })
    expect(profile.status(), await profile.text()).toBe(200)
    const savedUser = await (await page.request.get('/api/users/me')).json()
    expect(savedUser.avatarUrl).toBe(avatarPath)
    const invalidProfile = await page.request.patch('/api/users/me', {
        data: { avatarUrl: '/api/users/me' },
    })
    expect(invalidProfile.status()).toBe(400)
    expect((await invalidProfile.json()).data.errors.avatarUrl).toEqual(['Avatar URL is invalid.'])
    const avatar = await page.request.get(savedUser.avatarUrl)
    expect(avatar.status()).toBe(200)
    expect(await avatar.body()).toEqual(png)
    // Exercise the shared current-user data and the Uppy response validator in UI.
    await page.goto('/me')
    await page.waitForFunction(() => {
        const root = document.querySelector('#__nuxt') as HTMLElement & { __vue_app__?: { $nuxt?: { isHydrating: boolean } } } | null
        return root?.__vue_app__?.$nuxt?.isHydrating === false
    })
    await expect(page.getByRole('heading', { name: 'Docker avatar', exact: true })).toBeVisible()
    const profileAvatar = page.getByRole('img', { name: 'Docker avatar', exact: true }).first()
    await expect(profileAvatar).toHaveAttribute('src', avatarPath)
    await expect.poll(() => profileAvatar.evaluate(image => (image as HTMLImageElement).naturalWidth)).toBeGreaterThan(0)
    await page.getByRole('button', { name: '编辑资料', exact: true }).click()
    await expect(page.getByRole('dialog', { name: '编辑资料' })).toBeVisible()
    const avatarUploaded = page.waitForResponse(response =>
        new URL(response.url()).pathname === '/api/media/images' && response.request().method() === 'POST')
    await page.locator('input[type="file"]').setInputFiles({ name: 'avatar-ui.png', mimeType: 'image/png', buffer: png })
    expect((await avatarUploaded).status()).toBe(201)
    await page.getByPlaceholder('请输入昵称').fill('Docker profile updated')
    const save = page.getByRole('button', { name: '保存', exact: true })
    await expect(save).toBeEnabled()
    await save.click()
    await expect(page.getByRole('heading', { name: 'Docker profile updated', exact: true })).toBeVisible()
    const title = `dockersmoke${Date.now()}`
    const created = await page.request.post('/api/posts', {
        data: { title, content: 'Docker deployment verification', mediaIds: [media.id], tags: ['docker'] },
    })
    expect(created.status(), await created.text()).toBe(201)
    const post = await created.json()
    expect(post.media[0].id).toBe(media.id)
    expect(new URL(post.media[0].url).origin).toBe(new URL(mediaUrl).origin)
    const image = await page.request.get(post.media[0].url)
    expect(image.status()).toBe(200)
    expect(await image.body()).toEqual(png)
    const detail = await page.request.get(`${gatewayUrl}/api/v1/posts/${post.id}`)
    expect(detail.status()).toBe(200)
    await expect.poll(async () => {
        const response = await page.request.get(`${gatewayUrl}/api/v1/search/posts`, { params: { q: title, page: 1, pageSize: 20 } })
        expect(response.status(), await response.text()).toBe(200)
        const result = await response.json()
        return result.items.some((item: { postId: string }) => item.postId === post.id)
    }, { timeout: 60_000, intervals: [1000, 2000, 5000] }).toBe(true)

    await page.goto('/search?q=docker')
    await page.waitForFunction(() => {
        const root = document.querySelector('#__nuxt') as HTMLElement & { __vue_app__?: { $nuxt?: { isHydrating: boolean } } } | null
        return root?.__vue_app__?.$nuxt?.isHydrating === false
    })
    const search = page.getByRole('textbox', { name: '搜索 RedNote' })
    await expect(search).toHaveValue('docker')
    const queries: string[] = []
    page.on('request', request => {
        const url = new URL(request.url())
        if (url.pathname === '/api/posts/search') queries.push(url.searchParams.get('q') ?? '')
    })
    await page.clock.pauseAt(new Date(await page.evaluate(() => Date.now()) + 1000))
    await search.fill('discarded-query')
    await search.fill(title)
    await page.clock.runFor(400)
    await expect(page).toHaveURL(new RegExp(`/search\\?q=${title}$`))
    await expect.poll(() => queries).toContain(title)
    expect(queries).not.toContain('discarded-query')
    await page.clock.resume()
    const cover = page.getByRole('img', { name: title, exact: true })
    await expect(cover).toBeVisible()
    const coverUrl = new URL((await cover.getAttribute('src'))!)
    expect(coverUrl.origin).toBe(new URL(mediaUrl).origin)
    expect(coverUrl.searchParams.has('X-Amz-Signature')).toBe(true)
    await expect.poll(() => cover.evaluate(image => (image as HTMLImageElement).naturalWidth)).toBeGreaterThan(0)
    await page.getByRole('button', { name: `查看帖子：${title}`, exact: true }).click()
    const detailImage = page.getByRole('dialog').getByRole('img', { name: title, exact: true })
    await expect(detailImage).toBeVisible()
    await expect(detailImage).toHaveAttribute('srcset', /\s1x$/)
    await expect.poll(() => detailImage.evaluate(image => (image as HTMLImageElement).naturalWidth)).toBeGreaterThan(0)
    await page.keyboard.press('Escape')
    await expect(page.getByRole('dialog')).toHaveCount(0)
    // Enter bypasses the delay and cancels the timer, so only one request runs.
    await page.clock.pauseAt(new Date(await page.evaluate(() => Date.now()) + 1000))
    await search.fill('enterquery')
    await search.press('Enter')
    // Allow UI scheduling, while staying below the 350 ms search delay.
    await page.clock.runFor(50)
    await expect(page).toHaveURL(/\/search\?q=enterquery$/)
    await page.clock.runFor(500)
    expect(queries.filter(query => query === 'enterquery')).toHaveLength(1)
    await page.clock.resume()
    await page.clock.pauseAt(new Date(await page.evaluate(() => Date.now()) + 1000))
    await search.fill('cancelled-query')
    await page.getByRole('link', { name: 'RedNote', exact: true }).click()
    // Navigation may load asynchronous page data; its guard must cancel the timer first.
    await page.clock.resume()
    await expect(page).toHaveURL(`${frontendUrl}/`)
    await page.clock.fastForward(500)
    expect(queries).not.toContain('cancelled-query')

    const rootResponse = await page.request.post(`/api/posts/${post.id}/comments`, {
        data: { content: 'Docker root comment' },
    })
    expect(rootResponse.status(), await rootResponse.text()).toBe(201)
    const root = await rootResponse.json()
    for (const content of ['Docker reply to delete', 'Docker reply to cascade']) {
        const reply = await page.request.post(`/api/posts/${post.id}/comments`, {
            data: { content, parentCommentId: root.id },
        })
        expect(reply.status(), await reply.text()).toBe(201)
    }
    await page.goto(`/search?q=${title}`)
    await page.waitForFunction(() => {
        const root = document.querySelector('#__nuxt') as HTMLElement & { __vue_app__?: { $nuxt?: { isHydrating: boolean } } } | null
        return root?.__vue_app__?.$nuxt?.isHydrating === false
    })
    await page.getByRole('button', { name: `查看帖子：${title}`, exact: true }).click()
    await expect(page.getByRole('status', { name: '评论数量' })).toHaveText('3')
    await page.getByRole('button', { name: '删除回复', exact: true }).first().click()
    await page.getByRole('button', { name: '确认删除', exact: true }).click()
    await expect(page.getByRole('status', { name: '评论数量' })).toHaveText('2')
    await page.getByRole('button', { name: '删除评论', exact: true }).click()
    await page.getByRole('button', { name: '确认删除', exact: true }).click()
    await expect(page.getByText('还没有评论')).toBeVisible()
    await expect(page.getByRole('status', { name: '评论数量' })).toHaveText('0')
    const comments = await page.request.get(`/api/posts/${post.id}/comments`)
    expect(comments.status()).toBe(200)
    expect((await comments.json()).items).toEqual([])
    await page.getByRole('button', { name: '删除帖子', exact: true }).click()
    await page.getByRole('button', { name: '确认删除', exact: true }).click()
    await expect(page.getByRole('dialog')).toHaveCount(0)
    await expect(page.getByRole('button', { name: `查看帖子：${title}`, exact: true })).toHaveCount(0)
    expect((await page.request.get(`${gatewayUrl}/api/v1/posts/${post.id}`)).status()).toBe(404)
    await expect.poll(async () => {
        const response = await page.request.get(`${gatewayUrl}/api/v1/search/posts`, { params: { q: title, page: 1, pageSize: 20 } })
        expect(response.status()).toBe(200)
        return (await response.json()).items.some((item: { postId: string }) => item.postId === post.id)
    }, { timeout: 60_000, intervals: [1000, 2000, 5000] }).toBe(false)

    const logout = await page.request.post('/api/auth/logout')
    expect(logout.status(), await logout.text()).toBe(200)
    expect((await page.request.get('/api/users/me')).status()).toBe(401)
    const cookieNames = (await page.context().cookies()).map(cookie => cookie.name)
    expect(cookieNames).not.toContain('RedNote.Identity')
    // Reading the anonymous session may create a new empty session cookie.
    const anonymousSession = await (await page.request.get('/api/_auth/session')).json()
    expect(anonymousSession.user ?? null).toBeNull()
})

test('logout clears current-user state while a profile refresh is in flight', async ({ page }) => {
    const email = `race_${crypto.randomUUID()}@example.com`
    const password = `Race@${crypto.randomUUID()}Aa1`
    const register = await page.request.post('/api/auth/register', {
        data: { email, password, displayName: 'Race user', familyName: 'Test' },
    })
    expect(register.status(), await register.text()).toBe(200)
    const login = await page.request.post('/api/auth/login', { data: { email, password } })
    expect(login.status(), await login.text()).toBe(200)
    await page.goto('/auth/rednote')
    await expect(page).toHaveURL(`${frontendUrl}/`)
    await page.goto('/me')
    await expect(page.getByRole('button', { name: '编辑资料', exact: true })).toBeVisible()
    await page.waitForFunction(() => {
        const root = document.querySelector('#__nuxt') as HTMLElement & { __vue_app__?: { $nuxt?: { isHydrating: boolean } } } | null
        return root?.__vue_app__?.$nuxt?.isHydrating === false
    })
    const refresh = page.getByRole('button', { name: '刷新', exact: true })
    await expect(refresh).toBeEnabled()
    await expect(page.getByText('内容加载失败', { exact: true })).toHaveCount(0)

    const staleUser = await (await page.request.get('/api/users/me')).json()
    let refreshStarted = false
    let release!: () => void
    const delayed = new Promise<void>(resolve => { release = resolve })
    await page.route('**/api/users/me', async route => {
        refreshStarted = true
        await delayed
        // The browser may already have cancelled the request after logout.
        await route.fulfill({ status: 200, json: staleUser }).catch(() => {})
    })
    try {
        await refresh.click({ timeout: 5_000 })
        await expect.poll(() => refreshStarted, { timeout: 5_000 }).toBe(true)
        await page.locator('header').getByRole('button').last().click()
        await page.getByRole('menuitem', { name: '退出登录' }).click()
        await expect(page).toHaveURL(`${frontendUrl}/login`)
    }
    finally {
        release()
    }
    await page.waitForTimeout(300)
    await expect(page.locator('header').getByRole('link', { name: '登录', exact: true })).toBeVisible()
    await expect(page.locator('header').getByRole('button')).toHaveCount(0)
    const session = await (await page.request.get('/api/_auth/session')).json()
    expect(session.user ?? null).toBeNull()
})
