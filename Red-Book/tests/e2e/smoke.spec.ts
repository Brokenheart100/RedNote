import { expect, test } from '@playwright/test'
import { defaults, seal } from 'iron-webcrypto'

test('anonymous search renders through SSR and the BFF', async ({ page }) => {
    await page.goto('/search?q=Vue')
    await expect(page.getByRole('heading', { name: '架构测试笔记' })).toBeVisible()
    await page.waitForFunction(() => {
        const root = document.querySelector('#__nuxt') as HTMLElement & {
            __vue_app__?: { $nuxt?: { isHydrating: boolean } }
        }
        return root?.__vue_app__?.$nuxt?.isHydrating === false
    })
    await page.getByRole('button', { name: '查看帖子：架构测试笔记' }).click()
    await expect(page.getByText('还没有评论')).toBeVisible()
    await expect(page.getByRole('button', { name: '删除帖子', exact: true })).toHaveCount(0)
})

test('malformed JSON input returns a client error', async ({ request }) => {
    const response = await request.post('/api/posts', {
        data: { title: 123, content: 'test', mediaIds: [], tags: [123] },
    })
    expect(response.status()).toBe(400)
    const body = await response.json()
    expect(body.data.errors.title).toEqual(['title must be a string.'])
    if (process.env.REDNOTE_E2E_PREVIEW === '1') {
        expect(body).not.toHaveProperty('stack')
    }
})

test('private endpoints require a session', async ({ request }) => {
    const response = await request.get('/api/users/me')
    expect(response.status()).toBe(401)
    const id = '11111111-1111-1111-1111-111111111111'
    expect((await request.delete(`/api/posts/${id}`)).status()).toBe(401)
    expect((await request.delete(`/api/posts/${id}/comments/${id}`)).status()).toBe(401)
    expect((await request.delete('/api/posts/not-a-guid')).status()).toBe(400)
})

test('authors can confirm comment, reply and post deletion; failed deletion preserves the post', async ({ page, baseURL }) => {
    const owner = '11111111-1111-1111-1111-111111111111'
    const rootId = '22222222-2222-2222-2222-222222222222'
    const replyId = '33333333-3333-3333-3333-333333333333'
    const strangerId = '44444444-4444-4444-4444-444444444444'
    // Use the same sealed session format as H3 with the isolated test secret.
    // No production login route or token storage is bypassed in application code.
    const cookie = await seal(crypto, { id: crypto.randomUUID(), createdAt: Date.now(),
        data: { user: { authenticated: true, provider: 'oidc' } } },
    'frontend-test-session-password-12345678901234567890', defaults)
    await page.context().addCookies([{ name: 'nuxt-session', value: cookie, url: baseURL!, httpOnly: true }])
    await page.route('**/api/users/me', route => route.fulfill({ json: {
        userId: owner, nickname: '测试作者', avatarUrl: null, bio: null,
        followersCount: 0, followingCount: 0, isFollowing: false,
        createdAtUtc: '2026-10-01T00:00:00Z', updatedAtUtc: '2026-10-01T00:00:00Z',
    } }))
    const makeComment = (id: string, user: string, content: string, parentCommentId: string | null) => ({
        id, postId: owner, authorUserId: user, author: { userId: user, nickname: '作者', avatarUrl: null },
        content, parentCommentId, createdAtUtc: '2026-10-01T00:00:00Z', updatedAtUtc: '2026-10-01T00:00:00Z',
    })
    let roots = [{ ...makeComment(rootId, owner, '我的评论', null), replies: [
        makeComment(replyId, owner, '我的回复', rootId),
        makeComment(strangerId, strangerId, '其他人的回复', rootId),
    ] }]
    const deletes: string[] = []
    await page.route(`**/api/posts/${owner}/comments*`, async route => {
        await route.fulfill({ json: { page: 1, pageSize: 50, totalCount: roots.length, items: roots } })
    })
    await page.route(`**/api/posts/${owner}/comments/*`, async route => {
        const id = new URL(route.request().url()).pathname.split('/').at(-1)!
        deletes.push(id)
        roots = id === rootId ? [] : roots.map(root => ({ ...root, replies: root.replies.filter(reply => reply.id !== id) }))
        await route.fulfill({ status: 204 })
    })
    let failPostDelete = true
    await page.route(`**/api/posts/${owner}`, async route => {
        deletes.push('post')
        await route.fulfill({ status: failPostDelete ? 503 : 204,
            ...(failPostDelete ? { json: { statusCode: 503, message: '请稍后重试。' } } : {}) })
    })
    await page.route('**/api/posts/search*', route => route.fulfill({ json: {
        page: 1, pageSize: 20, totalCount: 1, items: [{ id: owner, authorUserId: owner,
            author: { userId: owner, nickname: '测试作者', avatarUrl: null }, title: '架构测试笔记',
            content: '验证删除', mediaIds: [], media: [], tags: [], likeCount: 0, commentCount: 3,
            isLiked: false, isFavorited: false, createdAtUtc: '2026-10-01T00:00:00Z', updatedAtUtc: '2026-10-01T00:00:00Z' }],
    } }))
    await page.goto('/login')
    await page.waitForFunction(() => {
        const root = document.querySelector('#__nuxt') as HTMLElement & {
            __vue_app__?: { $nuxt?: { isHydrating: boolean } }
        }
        return root?.__vue_app__?.$nuxt?.isHydrating === false
    })
    await page.getByRole('textbox', { name: '搜索 RedNote' }).fill('delete-test')
    await page.getByRole('textbox', { name: '搜索 RedNote' }).press('Enter')
    await page.getByRole('button', { name: '查看帖子：架构测试笔记' }).click()
    await expect(page.getByText('其他人的回复', { exact: true })).toBeVisible()
    await expect(page.getByRole('status', { name: '评论数量' })).toHaveText('3')
    await expect(page.getByRole('button', { name: '删除回复', exact: true })).toHaveCount(1)
    await page.getByRole('button', { name: '删除回复', exact: true }).click()
    await page.getByRole('button', { name: '取消', exact: true }).click()
    expect(deletes).toEqual([])
    await page.getByRole('button', { name: '删除回复', exact: true }).click()
    await page.getByRole('button', { name: '确认删除', exact: true }).click()
    await expect(page.getByText('我的回复', { exact: true })).toHaveCount(0)
    await expect(page.getByText('其他人的回复', { exact: true })).toBeVisible()
    await expect(page.getByText('我的评论', { exact: true })).toBeVisible()
    await expect(page.getByRole('status', { name: '评论数量' })).toHaveText('2')
    await page.getByRole('button', { name: '删除评论', exact: true }).click()
    await expect(page.getByText('删除这条评论及其全部回复？此操作无法撤销。')).toBeVisible()
    await page.getByRole('button', { name: '确认删除', exact: true }).click()
    await expect(page.getByText('还没有评论')).toBeVisible()
    await expect(page.getByRole('status', { name: '评论数量' })).toHaveText('0')
    await page.getByRole('button', { name: '删除帖子', exact: true }).click()
    await page.getByRole('button', { name: '确认删除', exact: true }).click()
    await expect(page.getByText('帖子删除失败', { exact: true })).toBeVisible()
    await page.getByRole('button', { name: '取消', exact: true }).click()
    await expect(page.getByRole('heading', { name: '架构测试笔记', exact: true })).toBeVisible()
    failPostDelete = false
    await page.getByRole('button', { name: '删除帖子', exact: true }).click()
    await page.getByRole('button', { name: '确认删除', exact: true }).click()
    await expect(page.getByRole('dialog')).toHaveCount(0)
    await expect(page.getByRole('button', { name: '查看帖子：架构测试笔记' })).toHaveCount(0)
    expect(deletes).toEqual([replyId, rootId, 'post', 'post'])
    // A stale search response must not resurrect the deleted card.
    await page.getByRole('textbox', { name: '搜索 RedNote' }).fill('Vue')
    await page.getByRole('textbox', { name: '搜索 RedNote' }).press('Enter')
    await expect(page).toHaveURL(/\/search\?q=Vue$/)
    await expect(page.getByRole('button', { name: '查看帖子：架构测试笔记' })).toHaveCount(0)
})

test('oversized and malformed JSON bodies are rejected at the boundary', async ({ request }) => {
    const oversized = await request.post('/api/posts', {
        data: { title: 'test', content: 'x'.repeat(70_000), mediaIds: [], tags: [] },
    })
    expect(oversized.status()).toBe(413)
    const malformed = await request.post('/api/posts', {
        headers: { 'Content-Type': 'application/json' }, data: '{broken',
    })
    expect(malformed.status()).toBe(400)
})
