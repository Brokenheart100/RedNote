import { expect, test, type Page } from '@playwright/test'
import { createHmac, createHash } from 'node:crypto'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { execFileSync } from 'node:child_process'
import { Redis } from 'ioredis'

interface Account { email: string; password: string; id: string; roles: string[]; authenticatorUri: string }
const accounts: Account[] = JSON.parse(readFileSync(process.env.REDNOTE_ADMIN_TEST_ACCOUNTS ?? resolve('../artifacts/admin-test-accounts.json'), 'utf8'))
const origin = process.env.REDNOTE_GATEWAY_URL ?? 'https://localhost:8443'
const frontend = process.env.REDNOTE_FRONTEND_URL ?? origin
function totp(uri: string) {
  const secret = new URL(uri).searchParams.get('secret')!
  const bits = [...secret.toUpperCase()].map(char => 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'.indexOf(char).toString(2).padStart(5, '0')).join('')
  const key = Buffer.from(bits.match(/.{8}/g)!.map(byte => parseInt(byte, 2)))
  const counter = Buffer.alloc(8); counter.writeBigUInt64BE(BigInt(Math.floor(Date.now() / 30_000)))
  const digest = createHmac('sha1', key).update(counter).digest(); const offset = digest[19]! & 15
  return ((digest.readUInt32BE(offset) & 0x7fffffff) % 1_000_000).toString().padStart(6, '0')
}
async function adminLogin(page: Page, account: Account) {
  const login = await page.request.post('/admin/api/login', { headers: { origin }, data: { email: account.email, password: account.password, code: totp(account.authenticatorUri) } })
  expect(login.status(), 'Admin password + TOTP login').toBe(200)
  const start = await page.request.get('/admin/auth/rednote', { maxRedirects: 0 })
  expect(start.status()).toBe(302)
  const authorization = new URL(start.headers().location!)
  const cookies = await page.context().cookies()
  const verifier = cookies.find(cookie => cookie.name === 'rednote-admin-nuxt-auth-pkce')
  expect(verifier, 'Admin PKCE cookie has an independent name').toBeTruthy()
  expect(verifier!.path).toBe('/admin/')
  expect(createHash('sha256').update(verifier!.value).digest('base64url') === authorization.searchParams.get('code_challenge'), 'PKCE cookie matches its authorization challenge').toBe(true)
  await page.goto(authorization.href)
  expect(new URL(page.url()).pathname, 'Admin OIDC returns to its own dashboard').toBe('/admin/')
  await expect(page.getByRole('heading', { name: '工作台', exact: true })).toBeVisible({ timeout: 120_000 })
  const response = await page.request.get('/admin/api/manage/session')
  expect(response.status()).toBe(200)
  return await response.json() as { permissions: string[]; csrfToken: string }
}
async function moderation(page: Page, csrf: string, path: string, data: object, key = crypto.randomUUID()) {
  return page.request.post(`/admin/api/manage/${path}`, { headers: { origin, 'x-admin-csrf': csrf, 'Idempotency-Key': key }, data })
}

test('Admin: MFA, permission isolation, CSRF, moderation, search, restrictions, audit and logout', async ({ browser }) => {
  const contexts = await Promise.all(Array.from({ length: 4 }, () => browser.newContext({ baseURL: origin })))
  const [admin, moderator, usersAdmin, ordinary] = await Promise.all(contexts.map(context => context.newPage())) as [Page, Page, Page, Page]
  try {
    expect((await ordinary.request.get('/admin/api/manage/session')).status()).toBe(401)
    expect((await ordinary.request.get('/api/v1/admin/posts')).status()).toBe(401)
    expect((await ordinary.request.get('/internal/admin/posts')).status()).toBe(404)
    const invalid = await admin.request.post('/admin/api/login', { headers: { origin }, data: { email: accounts[0]!.email, password: accounts[0]!.password, code: '000000' } })
    expect(invalid.status()).toBe(401)
    const session = await adminLogin(admin, accounts[0]!)
    expect(session.permissions.sort()).toEqual(['audit.read', 'content.moderate', 'users.restrict'])
    if (process.env.REDNOTE_ADMIN_TEST_REDIS_CONTAINER) {
      const cookieSession = await (await admin.request.get('/admin/api/_auth/session')).json()
      const script = "local d=cjson.decode(redis.call('GET',KEYS[1]));d.expiresAt=0;redis.call('SET',KEYS[1],cjson.encode(d),'KEEPTTL');return 1"
      execFileSync(process.env.REDNOTE_TEST_DOCKER ?? 'docker', ['exec', process.env.REDNOTE_ADMIN_TEST_REDIS_CONTAINER, 'sh', '-c',
        'REDISCLI_AUTH="$REDIS_PASSWORD" exec redis-cli --raw EVAL "$1" 1 "$2"', 'test-admin', script, `rednote:admin:session:${cookieSession.id}`], { stdio: 'pipe' })
      const refreshed = await Promise.all(Array.from({ length: 8 }, () => admin.request.get('/admin/api/manage/session')))
      expect(refreshed.map(response => response.status())).toEqual(Array(8).fill(200))
    }
    await adminLogin(moderator, accounts[1]!)
    expect((await moderator.request.get('/admin/api/manage/users')).status()).toBe(403)
    expect((await moderator.request.get('/admin/api/manage/content-audit')).status()).toBe(403)
    await adminLogin(usersAdmin, accounts[2]!)
    expect((await usersAdmin.request.get('/admin/api/manage/posts')).status()).toBe(403)
    if ((process.env.REDNOTE_ADMIN_TEST_REDIS || process.env.REDNOTE_ADMIN_TEST_REDIS_CONTAINER) && process.env.REDNOTE_ADMIN_TEST_POSTGRES_CONTAINER) {
      const docker = process.env.REDNOTE_TEST_DOCKER ?? 'docker'
      const redis = process.env.REDNOTE_ADMIN_TEST_REDIS_CONTAINER ? null : new Redis(process.env.REDNOTE_ADMIN_TEST_REDIS!, { lazyConnect: true, maxRetriesPerRequest: 1 })
      try {
        const adminCookieSession = await (await moderator.request.get('/admin/api/_auth/session')).json()
        const sessionKey = `rednote:admin:session:${adminCookieSession.id}`
        const stored = redis ? await redis.get(sessionKey) : execFileSync(docker, ['exec', process.env.REDNOTE_ADMIN_TEST_REDIS_CONTAINER!, 'sh', '-c',
          'REDISCLI_AUTH="$REDIS_PASSWORD" exec redis-cli --raw GET "$1"', 'test-admin', sessionKey], { encoding: 'utf8', stdio: 'pipe' }).trim()
        expect(Boolean(stored), 'Admin tokens stored only in server Redis').toBe(true)
        const { accessToken } = JSON.parse(stored!)
        expect((await moderator.request.get('/api/v1/admin/users', { headers: { Authorization: `Bearer ${accessToken}` } })).status()).toBe(403)
        expect((await moderator.request.get('/api/v1/admin/posts', { headers: { Authorization: `Bearer ${accessToken}` } })).status()).toBe(200)
        const account = accounts[1]!
        if (!account.email.startsWith('admin_test_') || !/^[0-9a-f-]{36}$/.test(account.id)) throw new Error('Role revocation only targets generated test accounts.')
        const execute = (sql: string) => execFileSync(docker, ['exec', process.env.REDNOTE_ADMIN_TEST_POSTGRES_CONTAINER!, 'sh', '-c',
          'PGPASSWORD="$POSTGRES_PASSWORD" exec psql -U "$POSTGRES_USER" -d identitydb -v ON_ERROR_STOP=1 -c "$1"', 'test-admin', sql], { stdio: 'pipe' })
        try {
          execute(`DELETE FROM "AspNetUserRoles" WHERE "UserId"='${account.id}'`)
          expect((await moderator.request.get('/admin/api/manage/session')).status()).toBe(401)
          expect((await moderator.request.get('/api/v1/admin/posts', { headers: { Authorization: `Bearer ${accessToken}` } })).status()).toBe(403)
        } finally {
          execute(`INSERT INTO "AspNetUserRoles" ("UserId","RoleId") SELECT '${account.id}',"Id" FROM "AspNetRoles" WHERE "Name"='ContentModerator' ON CONFLICT DO NOTHING`)
        }
      } finally { redis?.disconnect() }
    }
    expect((await admin.request.post('/admin/api/manage/logout', { headers: { origin } })).status()).toBe(403)
    expect((await admin.request.post('/admin/api/manage/logout', { headers: { origin: 'https://evil.example', 'x-admin-csrf': session.csrfToken } })).status()).toBe(403)

    const email = `admin_subject_${crypto.randomUUID()}@example.com`; const password = `TestUser@${crypto.randomUUID()}Aa1`
    expect((await ordinary.request.post(`${frontend}/api/auth/register`, { data: { email, password, displayName: 'Admin subject', familyName: 'Test' } })).status()).toBe(200)
    expect((await ordinary.request.post(`${frontend}/api/auth/login`, { data: { email, password } })).status()).toBe(200)
    await ordinary.goto(`${frontend}/auth/rednote`)
    await expect.poll(() => new URL(ordinary.url()).pathname, { timeout: 120_000 }).toBe('/')
    expect((await ordinary.request.get('/api/v1/admin/posts')).status()).toBe(401)
    expect((await ordinary.request.post('/admin/api/login', { headers: { origin }, data: { email, password, code: '000000' } })).status()).toBe(401)
    const me = await (await ordinary.request.get(`${frontend}/api/users/me`)).json()
    const userId = me.userId
    expect(userId).toBeTruthy()
    const title = `adminmoderation${Date.now()}`
    const create = await ordinary.request.post(`${frontend}/api/posts`, { data: { title, content: 'moderation integration', tags: ['admin'], mediaIds: [] } })
    expect(create.status()).toBe(201)
    const post = await create.json()
    const commentResponse = await ordinary.request.post(`${frontend}/api/posts/${post.id}/comments`, { data: { content: 'moderation comment' } })
    expect(commentResponse.status()).toBe(201)
    const comment = await commentResponse.json()
    const getAdminPost = async () => (await (await admin.request.get(`/admin/api/manage/posts/${post.id}`)).json()) as { revision: number; isHidden: boolean }
    const initial = await getAdminPost()
    const hideKeys = [crypto.randomUUID(), crypto.randomUUID()]
    const hideBody = { revision: initial.revision, reason: 'test moderation' }
    const hideResults = await Promise.all(hideKeys.map(key => moderation(admin, session.csrfToken, `posts/${post.id}/hide`, hideBody, key)))
    expect(hideResults.map(response => response.status()).sort()).toEqual([204, 409])
    const successfulKey = hideKeys[hideResults.findIndex(response => response.status() === 204)]!
    expect((await moderation(admin, session.csrfToken, `posts/${post.id}/hide`, hideBody, successfulKey)).status()).toBe(204)
    expect((await moderation(admin, session.csrfToken, `posts/${post.id}/hide`, { ...hideBody, reason: 'changed keyed request' }, successfulKey)).status()).toBe(422)
    expect((await ordinary.request.get(`/api/v1/posts/${post.id}`)).status()).toBe(404)
    expect((await ordinary.request.post(`${frontend}/api/posts/${post.id}/comments`, { data: { content: 'must not comment hidden post' } })).status()).toBe(404)
    const searchContains = async () => {
      const result = await (await ordinary.request.get('/api/v1/search/posts', { params: { q: title, page: 1, pageSize: 20 } })).json()
      return result.items.some((item: { postId: string }) => item.postId === post.id)
    }
    await expect.poll(searchContains, { timeout: 60_000, intervals: [1000, 2000] }).toBe(false)
    const hidden = await getAdminPost()
    expect((await moderation(admin, session.csrfToken, `posts/${post.id}/restore`, { revision: hidden.revision, reason: 'restore verified' })).status()).toBe(204)
    expect((await ordinary.request.get(`/api/v1/posts/${post.id}`)).status()).toBe(200)
    await expect.poll(searchContains, { timeout: 60_000, intervals: [1000, 2000] }).toBe(true)
    const comments = await (await admin.request.get('/admin/api/manage/comments', { params: { q: 'moderation comment' } })).json()
    const editable = comments.items.find((item: { id: string }) => item.id === comment.id)
    expect(editable).toBeTruthy()
    expect((await moderation(admin, session.csrfToken, `comments/${comment.id}/hide`, { revision: editable.revision, reason: 'hide comment' })).status()).toBe(204)
    const list = await (await ordinary.request.get(`/api/v1/posts/${post.id}/comments`, { params: { page: 1, pageSize: 50 } })).json()
    expect(list.items.some((item: { id: string }) => item.id === comment.id)).toBe(false)
    expect((await moderation(admin, session.csrfToken, `comments/${comment.id}/restore`, { revision: editable.revision + 1, reason: 'restore comment' })).status()).toBe(204)

    const restriction = { publishingRestricted: true, commentingRestricted: true, revision: 0, reason: 'test community restriction', expiresAtUtc: new Date(Date.now() + 60_000).toISOString() }
    const restrictionKey = crypto.randomUUID()
    expect((await moderation(admin, session.csrfToken, `users/${userId}/restrictions`, restriction, restrictionKey)).status()).toBe(204)
    expect((await moderation(admin, session.csrfToken, `users/${userId}/restrictions`, restriction, restrictionKey)).status()).toBe(204)
    expect((await moderation(admin, session.csrfToken, `users/${userId}/restrictions`, restriction)).status()).toBe(409)
    expect((await ordinary.request.post(`${frontend}/api/posts`, { data: { title: 'blocked', content: 'blocked', tags: [], mediaIds: [] } })).status()).toBe(403)
    expect((await ordinary.request.post(`${frontend}/api/posts/${post.id}/comments`, { data: { content: 'blocked' } })).status()).toBe(403)
    if (process.env.REDNOTE_ADMIN_TEST_REDIS_CONTAINER) {
      const ordinarySession = await (await ordinary.request.get(`${frontend}/api/_auth/session`)).json()
      const stored = execFileSync(process.env.REDNOTE_TEST_DOCKER ?? 'docker', ['exec', process.env.REDNOTE_ADMIN_TEST_REDIS_CONTAINER, 'sh', '-c',
        'REDISCLI_AUTH="$REDIS_PASSWORD" exec redis-cli --raw GET "$1"', 'test-admin', `rednote:auth-tokens:session:${ordinarySession.id}`], { encoding: 'utf8', stdio: 'pipe' }).trim()
      const { accessToken } = JSON.parse(stored)
      for (const path of ['/api/v1/posts/', '/API/v1/POSTS', `/api/v1/posts/${post.id}/comments/`, `/api/v1/posts/${post.id}/COMMENTS`]) {
        const response = await ordinary.request.post(path, { headers: { Authorization: `Bearer ${accessToken}` }, data: { title: 'blocked', content: 'blocked', tags: [], mediaIds: [] } })
        expect(response.status(), 'Restrictions cover all equivalent endpoint paths').toBe(403)
      }
    }
    expect((await moderation(admin, session.csrfToken, `users/${userId}/restrictions`, { ...restriction, publishingRestricted: false, commentingRestricted: false, revision: 1, expiresAtUtc: null, reason: 'release restriction' })).status()).toBe(204)
    expect((await ordinary.request.post(`${frontend}/api/posts/${post.id}/comments`, { data: { content: 'released' } })).status()).toBe(201)
    await expect.poll(async () => (await (await admin.request.get('/admin/api/manage/content-audit', { params: { target: post.id } })).json()).total,
      { timeout: 30_000, intervals: [500, 1000] }).toBe(2)
    const audit = await (await admin.request.get('/admin/api/manage/content-audit', { params: { target: post.id } })).json()
    expect(audit.items.map((item: { action: string }) => item.action).sort()).toEqual(['post.hide', 'post.restore'])
    await expect.poll(async () => (await (await admin.request.get('/admin/api/manage/user-audit', { params: { target: userId } })).json()).total,
      { timeout: 30_000, intervals: [500, 1000] }).toBe(2)
    const unified = await (await admin.request.get('/admin/api/manage/audit', { params: { target: userId } })).json()
    expect(unified.total).toBe(2)
    expect(unified.items.every((item: { source: string }) => item.source === 'user')).toBe(true)
    const datedAudit = await admin.request.get('/admin/api/manage/audit', { params: {
      target: userId, from: '2026-01-01T00:00:00+08:00', to: '2099-01-01T00:00:00+08:00'
    } })
    expect(datedAudit.status()).toBe(200)
    expect((await datedAudit.json()).total).toBe(2)
    // This page loads on mount; wait for client hydration and its initial query before editing filters.
    await Promise.all([
      admin.waitForResponse(response => new URL(response.url()).pathname === '/admin/api/manage/audit'
        && response.request().method() === 'GET'),
      admin.goto('/admin/audit')
    ])
    await expect(admin.getByRole('heading', { name: '操作审计', exact: true })).toBeVisible()
    await admin.getByPlaceholder('目标 ID').fill(userId)
    await admin.getByRole('button', { name: '查询', exact: true }).click()
    await expect(admin.locator('tbody tr')).toHaveCount(2)
    await expect(admin.locator('tbody')).toContainText('用户服务')
    await admin.goto('/admin/content'); await expect(admin.getByRole('heading', { name: '内容管理', exact: true })).toBeVisible()
    const row = admin.getByRole('row').filter({ hasText: title })
    await expect(row).toBeVisible()
    await row.getByRole('button', { name: '查看并下架', exact: true }).click()
    await admin.getByPlaceholder('请输入明确的处理原因').fill('UI moderation verification')
    await admin.getByRole('button', { name: '确认下架', exact: true }).click()
    await expect(admin.getByRole('dialog')).toHaveCount(0)
    await row.getByRole('button', { name: '查看并恢复', exact: true }).click()
    await admin.getByPlaceholder('请输入明确的处理原因').fill('UI restore verification')
    await admin.getByRole('button', { name: '确认恢复', exact: true }).click()
    await expect(admin.getByRole('dialog')).toHaveCount(0)
    await admin.screenshot({ path: resolve('../artifacts/admin-content.png'), fullPage: true })
    const beforeDelete = await getAdminPost()
    expect((await ordinary.request.delete(`${frontend}/api/posts/${post.id}`)).status()).toBe(204)
    expect((await moderation(admin, session.csrfToken, `posts/${post.id}/restore`, { revision: beforeDelete.revision + 1, reason: 'must not restore author deletion' })).status()).toBe(409)
    expect((await moderation(admin, session.csrfToken, 'logout', {})).status()).toBe(200)
    expect((await admin.request.get('/admin/api/manage/session')).status()).toBe(401)
    await admin.goto('/admin/auth/rednote'); await expect(admin.getByRole('heading', { name: 'RedNote 管理后台', exact: true })).toBeVisible()
    expect(new URL(admin.url()).pathname).toBe('/admin/login')
  } finally { await Promise.all(contexts.map(context => context.close())) }
})
