import assert from 'node:assert/strict'
import { randomUUID, createHash } from 'node:crypto'
import { execFileSync } from 'node:child_process'
import { mkdir, readFile, writeFile, rename } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import path from 'node:path'
import { parseArgs } from 'node:util'
import { setTimeout as delay } from 'node:timers/promises'
import { chromium, expect } from '@playwright/test'
import sharp from 'sharp'

// Opt-in data creation against the running application; successful data is retained.
const { values } = parseArgs({ options: {
    users: { type: 'string', default: '3' },
    'posts-per-user': { type: 'string', default: '30' },
    frontend: { type: 'string', default: process.env.REDNOTE_FRONTEND_URL ?? 'http://localhost:3000' },
    gateway: { type: 'string', default: process.env.REDNOTE_GATEWAY_URL ?? 'http://localhost:8080' },
    'verify-report': { type: 'string' },
} })
const existing = values['verify-report'] ? JSON.parse(await readFile(path.resolve(values['verify-report']), 'utf8')) : null
const userCount = existing?.users.length ?? Number(values.users)
const postsPerUser = existing ? existing.expectedPosts / userCount : Number(values['posts-per-user'])
assert(Number.isInteger(userCount) && userCount >= 1 && userCount <= 10, '--users must be 1..10')
assert(Number.isInteger(postsPerUser) && postsPerUser >= 1 && postsPerUser <= 100, '--posts-per-user must be 1..100')
const frontend = new URL(existing?.frontend ?? values.frontend).origin
const gateway = new URL(existing?.gateway ?? values.gateway).origin
const marker = existing?.marker ?? `bulktest${randomUUID().replaceAll('-', '').slice(0, 12)}`
const output = existing ? path.dirname(path.resolve(values['verify-report'])) : fileURLToPath(new URL(`../../artifacts/bulk-posts/${marker}/`, import.meta.url))
await mkdir(output, { recursive: true })
const accountsPath = path.join(output, 'accounts.local.json')
const accounts = existing ? JSON.parse(await readFile(accountsPath, 'utf8')).accounts : Array.from({ length: userCount }, (_, index) => ({
    email: `${marker}_${index + 1}@example.com`,
    password: `Bulk@${randomUUID()}Aa1`,
    nickname: ['城市漫游者', '生活记录员', '周末探索家'][index % 3] + ` ${index + 1}`,
}))
if (!existing) await writeFile(accountsPath, JSON.stringify({ marker, frontend, accounts }, null, 2), { mode: 0o600 })
if (process.platform === 'win32') {
    const identity = execFileSync('whoami', { encoding: 'utf8', windowsHide: true }).trim()
    execFileSync('icacls', [accountsPath, '/inheritance:r', '/grant:r', `${identity}:(F)`], { windowsHide: true, stdio: 'pipe' })
}
const report = existing ?? { marker, frontend, gateway, searchUrl: `${frontend}/search?q=${marker}`,
    startedAt: new Date().toISOString(), status: 'running', expectedPosts: userCount * postsPerUser,
    users: [], checks: {}, }
report.status = 'running'
delete report.error
async function saveReport() {
    const temporary = path.join(output, 'report.tmp')
    await writeFile(temporary, JSON.stringify(report, null, 2))
    await rename(temporary, path.join(output, 'report.json'))
}
await saveReport()
console.log(`Batch ${marker}: ${userCount} users x ${postsPerUser} image posts; ${existing ? 'verification only' : 'retained data'}.`)
console.log(`Artifacts: ${output}`)

async function checkedResponse(action, expectedStatus) {
    for (let attempt = 0; attempt < 4; attempt++) {
        const response = await action()
        if (response.status() === 429 && attempt < 3) {
            const seconds = Number(response.headers()['retry-after'])
            const wait = Number.isFinite(seconds) && seconds > 0 ? Math.min(seconds + 1, 120) : 61
            console.log(`Rate limited: waiting ${wait}s (existing limits remain enabled).`)
            await response.dispose()
            await delay(wait * 1000)
            continue
        }
        assert.equal(response.status(), expectedStatus, `HTTP ${response.status()}: ${(await response.text()).slice(0, 1000)}`)
        return response
    }
    throw new Error('Rate limit retry exhausted')
}
async function json(action, status = 200) {
    const response = await checkedResponse(action, status)
    try { return await response.json() }
    finally { await response.dispose() }
}
const themes = [
    { name: '城市漫步', label: 'CITY WALK', color: '#d6e6ff', accent: '#365bd6', text: '沿着街角慢慢走，记录今天遇见的小店和城市风景。' },
    { name: '咖啡时光', label: 'COFFEE TIME', color: '#f1dfcc', accent: '#81563d', text: '一杯咖啡、一本书，把忙碌的节奏放慢一点。' },
    { name: '周末出游', label: 'WEEKEND TRIP', color: '#dcebd5', accent: '#467c42', text: '周末去公园散步，整理一份轻松出行的小清单。' },
    { name: '居家日常', label: 'SLOW LIVING', color: '#f5dce5', accent: '#b65b81', text: '收拾房间、准备晚餐，用简单的小事记录生活。' },
    { name: '运动记录', label: 'KEEP MOVING', color: '#ffe3b5', accent: '#b16b20', text: '今天完成了运动计划，记录训练后的感受和进步。' },
]
async function cover(userIndex, postIndex) {
    const theme = themes[(postIndex + userIndex) % themes.length]
    const number = String(postIndex + 1).padStart(2, '0')
    const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="900" height="1200" viewBox="0 0 900 1200">
      <rect width="900" height="1200" fill="${theme.color}"/>
      <circle cx="760" cy="180" r="210" fill="${theme.accent}" opacity=".12"/>
      <rect x="72" y="90" width="285" height="52" rx="26" fill="${theme.accent}"/>
      <text x="96" y="125" font-family="Arial" font-size="24" fill="white">REDNOTE / TEST DATA</text>
      <text x="72" y="255" font-family="Arial" font-weight="bold" font-size="55" fill="${theme.accent}">${theme.label}</text>
      <rect x="72" y="325" width="756" height="560" rx="40" fill="white" opacity=".85"/>
      <circle cx="450" cy="510" r="105" fill="${theme.accent}" opacity=".2"/>
      <path d="M160 800 L330 580 L470 740 L590 560 L750 800 Z" fill="${theme.accent}" opacity=".7"/>
      <text x="72" y="1010" font-family="Arial" font-weight="bold" font-size="85" fill="${theme.accent}">NOTE ${number}</text>
      <text x="76" y="1080" font-family="Arial" font-size="30" fill="${theme.accent}">CREATOR ${userIndex + 1} / 900 x 1200</text>
      <text x="76" y="1135" font-family="Arial" font-size="24" fill="${theme.accent}">${marker}</text>
    </svg>`
    return sharp(Buffer.from(svg)).png().toBuffer()
}
const browser = await chromium.launch()
const clients = []
let nextUploadAt = 0
try {
    for (const account of accounts) {
        const context = await browser.newContext({ baseURL: frontend })
        clients.push(context)
        if (!existing) await json(() => context.request.post('/api/auth/register', { data: {
            email: account.email, password: account.password, displayName: account.nickname, familyName: '测试',
        } }))
        await checkedResponse(() => context.request.post('/api/auth/login', { data: { email: account.email, password: account.password } }), 200).then(r => r.dispose())
        const page = await context.newPage()
        await page.goto('/auth/rednote')
        await expect(page).toHaveURL(`${frontend}/`, { timeout: 30000 })
        const me = await json(() => context.request.get('/api/users/me'))
        if (existing) assert.equal(me.userId, report.users[clients.length - 1].userId)
        else {
            const updated = await json(() => context.request.patch('/api/users/me', { data: {
                nickname: account.nickname, bio: `带图帖子批量测试 · ${marker}`,
            } }))
            assert.equal(updated.userId, me.userId)
            report.users.push({ userId: me.userId, email: account.email, nickname: account.nickname, posts: [] })
        }
        await page.close()
        await saveReport()
        console.log(`Authenticated ${account.nickname} (${me.userId})`)
    }
    for (let postIndex = 0; !existing && postIndex < postsPerUser; postIndex++) {
        for (let userIndex = 0; userIndex < userCount; userIndex++) {
            const request = clients[userIndex].request
            const user = report.users[userIndex]
            const png = await cover(userIndex, postIndex)
            await delay(Math.max(0, nextUploadAt - Date.now()))
            nextUploadAt = Date.now() + 2200 // Shared IP upload bucket: 30/minute.
            const media = await json(() => request.post('/api/media/images', { multipart: {
                file: { name: `${marker}-${userIndex + 1}-${postIndex + 1}.png`, mimeType: 'image/png', buffer: png },
            } }), 201)
            assert(media.objectKey.startsWith(`images/${user.userId.replaceAll('-', '')}/`), 'Media owner mismatch')
            assert.equal(media.size, png.length)
            assert.equal(media.contentType, 'image/png')
            const theme = themes[(postIndex + userIndex) % themes.length]
            const title = `${theme.name}｜${user.nickname}的第 ${postIndex + 1} 篇记录`
            const post = await json(() => request.post('/api/posts', {
                headers: { 'Idempotency-Key': randomUUID() },
                data: { title, content: `${theme.text}\n\n这是第 ${postIndex + 1} 篇带图测试笔记，用于查看首页瀑布流、详情页和搜索效果。\n批次：${marker}`,
                    mediaIds: [media.id], tags: [theme.name, '带图测试', marker] },
            }), 201)
            // Save identifiers before further assertions so partial runs remain inspectable.
            user.posts.push({ id: post.id, title, mediaId: media.id })
            await saveReport()
            assert.equal(post.authorUserId, user.userId)
            assert.deepEqual(post.mediaIds, [media.id])
            assert.equal(post.media.length, 1)
            assert.equal(post.media[0].id, media.id)
            const image = await checkedResponse(() => request.get(`/api/media/${media.id}`), 200)
            assert.equal(createHash('sha256').update(await image.body()).digest('hex'), createHash('sha256').update(png).digest('hex'), 'Image byte mismatch')
            await image.dispose()
        }
        if ((postIndex + 1) % 5 === 0 || postIndex + 1 === postsPerUser) {
            console.log(`Published and image-verified: ${(postIndex + 1) * userCount}/${userCount * postsPerUser}`)
        }
    }
    const expectedIds = report.users.flatMap(user => user.posts.map(post => post.id)).sort()
    for (const user of report.users) {
        const list = await json(() => clients[0].request.get(`${gateway}/api/v1/posts?authorUserId=${user.userId}&page=1&pageSize=100`))
        assert.equal(list.totalCount, postsPerUser)
        assert.deepEqual(list.items.map(post => post.id).sort(), user.posts.map(post => post.id).sort())
        assert(list.items.every(post => post.authorUserId === user.userId && post.media.length === 1))
    }
    report.checks.authorCountsAndImages = true
    if (existing) {
        for (let userIndex = 0; userIndex < userCount; userIndex++) {
            for (let postIndex = 0; postIndex < postsPerUser; postIndex++) {
                const post = report.users[userIndex].posts[postIndex]
                const png = await cover(userIndex, postIndex)
                const image = await checkedResponse(() => clients[userIndex].request.get(`/api/media/${post.mediaId}`), 200)
                assert.equal(createHash('sha256').update(await image.body()).digest('hex'), createHash('sha256').update(png).digest('hex'), 'Retained image byte mismatch')
                await image.dispose()
            }
        }
    }
    report.checks.verifiedImageCount = expectedIds.length
    if (userCount > 1) {
        const post = report.users[0].posts[0]
        await checkedResponse(() => clients[1].request.delete(`/api/posts/${post.id}`), 403).then(r => r.dispose())
        await checkedResponse(() => clients[0].request.get(`${gateway}/api/v1/posts/${post.id}`), 200).then(r => r.dispose())
        report.checks.crossUserDeletionDenied = true
    }
    // Search projections are asynchronous; poll for at most one minute.
    let indexed = false
    for (let attempt = 0; attempt < 13; attempt++) {
        const ids = []
        let total = 0
        for (let page = 1; ; page++) {
            const result = await json(() => clients[0].request.get(`/api/posts/search?q=${marker}&page=${page}&pageSize=100`))
            total = result.totalCount
            ids.push(...result.items.map(post => post.id))
            assert(result.items.every(post => post.media.length === 1))
            if (page * 100 >= total) break
        }
        if (total === expectedIds.length && JSON.stringify(ids.sort()) === JSON.stringify(expectedIds)) {
            indexed = true
            break
        }
        if (attempt < 12) await delay(5000)
    }
    assert(indexed, 'Search did not index all created posts within 60s')
    report.checks.searchIndexedAllPosts = true
    const page = await clients[0].newPage()
    await page.setViewportSize({ width: 1440, height: 1100 })
    await page.goto(report.searchUrl)
    await expect(page.getByText(`共 ${expectedIds.length} 个结果`, { exact: true })).toBeVisible({ timeout: 30000 })
    const cards = page.locator('article[aria-label^="查看帖子："]')
    await expect(cards).toHaveCount(Math.min(20, expectedIds.length))
    const firstPostCard = cards.first()
    await expect(firstPostCard).toBeVisible()
    await expect.poll(() => firstPostCard.locator('img').evaluateAll(images => images.some(image => image.complete && image.naturalWidth > 0)), { timeout: 30000 }).toBe(true)
    for (const card of await cards.all()) {
        await card.scrollIntoViewIfNeeded()
        await expect.poll(() => card.locator('img').evaluateAll(images => images.some(image => image.complete && image.naturalWidth > 0)), { timeout: 30000 }).toBe(true)
    }
    await page.evaluate(() => window.scrollTo(0, 0))
    await page.screenshot({ path: path.join(output, 'search-preview.png'), fullPage: true })
    report.checks.browserSearchAndCover = true
    report.status = 'passed'
    report.completedAt = new Date().toISOString()
    await saveReport()
    console.log(`PASS: ${expectedIds.length} retained posts and images. Browse ${report.searchUrl}`)
    console.log(`Login credentials (local file): ${accountsPath}`)
}
catch (error) {
    report.status = 'failed'
    report.error = error.message
    await saveReport()
    console.error(`FAILED; partial data retained. ${error.message}`)
    process.exitCode = 1
}
finally {
    await Promise.all(clients.map(context => context.close()))
    await browser.close()
}
