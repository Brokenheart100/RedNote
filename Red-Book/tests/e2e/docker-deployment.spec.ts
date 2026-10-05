import { expect, test } from '@playwright/test'

test('shared validation blocks invalid login and registration before HTTP requests', async ({ page }) => {
    const submitted: string[] = []
    page.on('request', request => {
        const path = new URL(request.url()).pathname
        if (request.method() === 'POST' && ['/api/auth/login', '/api/auth/register'].includes(path)) {
            submitted.push(path)
        }
    })
    await page.goto('/login')
    await page.getByPlaceholder('请输入邮箱', { exact: true }).fill('schema@example.com')
    await page.getByPlaceholder('请输入密码', { exact: true }).fill('x'.repeat(1025))
    await page.getByRole('button', { name: '登录', exact: true }).click()
    await expect(page.getByText('密码不能为空，且不能超过 1024 个字符。')).toBeVisible()
    await page.goto('/register')
    await page.getByPlaceholder('请输入邮箱', { exact: true }).fill('schema@example.com')
    await page.getByPlaceholder('请输入显示名称').fill('x'.repeat(65))
    await page.getByPlaceholder('请输入密码', { exact: true }).fill('Schema@Password1')
    await page.getByPlaceholder('请再次输入密码').fill('Schema@Password1')
    await page.getByRole('button', { name: '注册', exact: true }).click()
    await expect(page.getByText('显示名称不能超过 64 个字符。')).toBeVisible()
    expect(submitted).toEqual([])
})

test('Docker: OIDC, Redis session, upload, gRPC, search, deletion and logout', async ({ page }) => {
    const email = `docker_${crypto.randomUUID()}@example.com`
    const password = `Docker@${crypto.randomUUID()}Aa1`
    const register = await page.request.post('/api/auth/register', {
        data: { email, password, displayName: 'Docker smoke', familyName: 'Test' },
    })
    expect(register.status(), await register.text()).toBe(200)
    const login = await page.request.post('/api/auth/login', { data: { email, password } })
    expect(login.status(), await login.text()).toBe(200)
    await page.goto('/auth/rednote')
    await expect(page).toHaveURL('http://localhost:3000/')
    const me = await page.request.get('/api/users/me')
    expect(me.status(), await me.text()).toBe(200)
    const session = await (await page.request.get('/api/_auth/session')).json()
    expect(session.user).toMatchObject({ authenticated: true, provider: 'oidc' })
    expect(session).not.toHaveProperty('accessToken')
    expect(session).not.toHaveProperty('refreshToken')

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
    await expect(page.getByRole('heading', { name: 'Docker avatar', exact: true })).toBeVisible()
    const profileAvatar = page.getByRole('img', { name: 'Docker avatar', exact: true }).first()
    await expect(profileAvatar).toHaveAttribute('src', avatarPath)
    await expect.poll(() => profileAvatar.evaluate(image => (image as HTMLImageElement).naturalWidth)).toBeGreaterThan(0)
    await page.getByRole('button', { name: '编辑资料', exact: true }).click()
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
    expect(new URL(post.media[0].url).origin).toBe('http://localhost:9000')
    const image = await page.request.get(post.media[0].url)
    expect(image.status()).toBe(200)
    expect(await image.body()).toEqual(png)
    const detail = await page.request.get(`http://localhost:8080/api/v1/posts/${post.id}`)
    expect(detail.status()).toBe(200)
    await expect.poll(async () => {
        const response = await page.request.get('http://localhost:8080/api/v1/search/posts', { params: { q: title, page: 1, pageSize: 20 } })
        expect(response.status(), await response.text()).toBe(200)
        const result = await response.json()
        return result.items.some((item: { postId: string }) => item.postId === post.id)
    }, { timeout: 60_000, intervals: [1000, 2000, 5000] }).toBe(true)

    await page.goto('/search?q=docker')
    const search = page.getByRole('textbox', { name: '搜索 RedNote' })
    await expect(search).toHaveValue('docker')
    const queries: string[] = []
    page.on('request', request => {
        const url = new URL(request.url())
        if (url.pathname === '/api/posts/search') queries.push(url.searchParams.get('q') ?? '')
    })
    await search.fill('discarded-query')
    await search.fill(title)
    await expect(page).toHaveURL(new RegExp(`/search\\?q=${title}$`))
    await expect.poll(() => queries).toContain(title)
    expect(queries).not.toContain('discarded-query')
    const cover = page.getByRole('img', { name: title, exact: true })
    await expect(cover).toBeVisible()
    const coverUrl = new URL((await cover.getAttribute('src'))!)
    expect(coverUrl.origin).toBe('http://localhost:9000')
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
    await search.fill('enterquery')
    await search.press('Enter')
    await expect(page).toHaveURL(/\/search\?q=enterquery$/)
    await page.waitForTimeout(500)
    expect(queries.filter(query => query === 'enterquery')).toHaveLength(1)
    await search.fill('cancelled-query')
    await page.getByRole('link', { name: 'RedNote', exact: true }).click()
    await expect(page).toHaveURL('http://localhost:3000/')
    await page.waitForTimeout(500)
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
    expect((await page.request.get(`http://localhost:8080/api/v1/posts/${post.id}`)).status()).toBe(404)
    await expect.poll(async () => {
        const response = await page.request.get('http://localhost:8080/api/v1/search/posts', { params: { q: title, page: 1, pageSize: 20 } })
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
    await expect(page).toHaveURL('http://localhost:3000/')
    await page.goto('/me')
    await expect(page.getByRole('button', { name: '编辑资料', exact: true })).toBeVisible()
    await page.waitForFunction(() => {
        const root = document.querySelector('#__nuxt')
        return root && '__vue_app__' in root
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
        await expect(page).toHaveURL('http://localhost:3000/login')
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
