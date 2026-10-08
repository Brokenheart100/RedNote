import assert from 'node:assert/strict'
import { readFile, readdir, writeFile } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { chromium } from '@playwright/test'
import { execFileSync } from 'node:child_process'

const root = fileURLToPath(new URL('../../', import.meta.url))
const frontend = process.env.REDNOTE_FRONTEND_URL ?? 'http://localhost:3000'
let account = { email: process.env.REDNOTE_TEST_EMAIL, password: process.env.REDNOTE_TEST_PASSWORD }
if (!account.email || !account.password) {
    const bulk = path.join(root, 'artifacts/bulk-posts')
    const directories = (await readdir(bulk)).sort().reverse()
    for (const directory of directories) {
        try {
            const report = JSON.parse(await readFile(path.join(bulk, directory, 'report.json'), 'utf8'))
            if (report.status !== 'passed') continue
            const accounts = JSON.parse(await readFile(path.join(bulk, directory, 'accounts.local.json'), 'utf8'))
            const selected = (Array.isArray(accounts) ? accounts : accounts.accounts)?.[0]
            if (!selected) continue
            account = selected
            break
        }
        catch { /* Try the next completed local fixture. */ }
    }
}
assert.ok(account.email && account.password, 'Provide a test account or run the image-post fixture first.')
const browser = await chromium.launch()
const context = await browser.newContext({ baseURL: frontend })
const api = context.request
try {
    const login = await api.post('/api/auth/login', { data: { email: account.email, password: account.password } })
    assert.equal(login.status(), 200, 'BFF login failed')
    const signInPage = await context.newPage()
    await signInPage.goto('/auth/rednote')
    await signInPage.waitForURL(`${frontend}/`, { timeout: 30_000 })
    await signInPage.close()
    let feed
    for (let attempt = 0; attempt < 120; attempt++) {
        const response = await api.get('/api/posts/recommended?pageSize=20')
        assert.equal(response.status(), 200)
        feed = await response.json()
        if (feed.strategy === 'gorse' && feed.items.length > 0) break
        await new Promise(resolve => setTimeout(resolve, 1000))
    }
    assert.equal(feed.strategy, 'gorse', 'Recommendations did not switch from fallback to Gorse')
    assert.ok(feed.items.length > 0)
    assert.ok(feed.nextCursor)
    const next = await api.get('/api/posts/recommended', { params: { pageSize: 20, cursor: feed.nextCursor } })
    assert.equal(next.status(), 200)
    const second = await next.json()
    assert.equal(second.requestId, feed.requestId)
    const ids = new Set(feed.items.map(post => post.id))
    assert.ok(second.items.every(post => !ids.has(post.id)), 'Pagination duplicated posts')
    const pictured = feed.items.find(post => post.media.length > 0)
    assert.ok(pictured, 'Recommended image posts were not hydrated')
    const image = await api.get(pictured.media[0].url)
    assert.equal(image.status(), 200)
    assert.match(image.headers()['content-type'], /^image\//)
    const page = await context.newPage()
    const failures = []
    page.on('response', response => { if (response.status() >= 500 && response.url().startsWith(frontend)) failures.push(response.status()) })
    const feedback = type => page.waitForResponse(response => response.url().includes('/api/posts/recommendations/feedback')
        && response.request().method() === 'POST' && response.request().postDataJSON()?.items?.some(item => item.type === type), { timeout: 30_000 })
    const read = feedback('read')
    await page.goto(frontend, { waitUntil: 'domcontentloaded' })
    const card = page.locator('article[role="button"]').first()
    await card.waitFor({ state: 'visible', timeout: 30_000 })
    assert.equal((await read).status(), 204)
    const click = feedback('click')
    await card.click()
    assert.equal((await click).status(), 204)
    assert.deepEqual(failures, [])
    const outages = []
    if (process.env.REDNOTE_TEST_COMPOSE_PROJECT) {
        const compose = (...args) => execFileSync('docker', ['compose', '--project-name', process.env.REDNOTE_TEST_COMPOSE_PROJECT,
            '--env-file', path.join(root, 'RedNote.AppHost/aspire-output/.env.Production'),
            '-f', path.join(root, 'RedNote.AppHost/aspire-output/docker-compose.yaml'), ...args], { stdio: 'pipe' })
        const postgres = execFileSync('docker', ['ps', '--filter', `label=com.docker.compose.project=${process.env.REDNOTE_TEST_COMPOSE_PROJECT}`,
            '--filter', 'label=com.docker.compose.service=postgres', '--format', '{{.Names}}'], { encoding: 'utf8' }).trim()
        assert.ok(postgres && !postgres.includes('\n'), 'Expected one PostgreSQL container in the selected project')
        const sql = (database, query) => execFileSync('docker', ['exec', postgres, 'sh', '-c',
            'export PGPASSWORD="$POSTGRES_PASSWORD"; exec psql -U postgres -d "$1" -Atc "$2"', 'psql', database, query], { encoding: 'utf8' }).trim()
        const profileResponse = await api.get('/api/users/me')
        assert.equal(profileResponse.status(), 200)
        const user = (await profileResponse.json()).userId
        const post = feed.items.find(item => !item.isLiked)?.id
        const uuid = /^[a-f0-9-]{36}$/i
        assert.ok(uuid.test(user) && uuid.test(post), 'Expected a user and an initially unliked post')
        const synchronized = async active => {
            for (let attempt = 0; attempt < 40; attempt++) {
                const state = sql('recommendationdb', `SELECT "IsActive"::text || ':' || ("Version" = "LastAcknowledgedRevision")::text FROM "Feedback" WHERE "PostId"='${post}' AND "UserId"='${user}' AND "Type"='like'`)
                const engine = sql('gorsedb', `SELECT count(*), coalesce(max(value),0) FROM feedback WHERE item_id='${post}' AND user_id='${user}' AND feedback_type='like'`)
                if (active ? state === 'true:true' && engine === '1|1' : state === 'false:true' && engine === '0|0') return
                await new Promise(resolve => setTimeout(resolve, 500))
            }
            assert.fail(`Like ${active ? 'activation' : 'cancellation'} did not synchronize through RabbitMQ`)
        }
        try {
            assert.equal((await api.post(`/api/posts/${post}/likes`)).status(), 204)
            await synchronized(true)
        }
        finally {
            assert.equal((await api.delete(`/api/posts/${post}/likes`)).status(), 204)
            await synchronized(false)
        }
        outages.push('rabbitmq: like PUT=1 and unlike DELETE confirmed; initial state restored')
        try {
            compose('stop', 'gorse')
            const fallback = await api.get('/api/posts/recommended?pageSize=20')
            assert.equal(fallback.status(), 200)
            const latest = await fallback.json()
            assert.equal(latest.strategy, 'latest')
            assert.ok(latest.items.length > 0)
            outages.push('gorse: latest candidates with full content')
        }
        finally { compose('start', 'gorse') }
        try {
            compose('stop', 'recommendation-service')
            const unavailable = await api.get('/api/posts/recommended?pageSize=20')
            assert.ok([502, 503, 504].includes(unavailable.status()))
            await page.goto(frontend, { waitUntil: 'domcontentloaded' })
            await page.locator('article[role="button"]').first().waitFor({ state: 'visible', timeout: 30_000 })
            const home = await page.evaluate(() => window.__NUXT__?.data?.['home-recommended-feed'])
            // Nuxt payload may be extracted; the rendered card and original latest API are authoritative checks.
            if (home) assert.equal(home.mode, 'latest')
            const latest = await api.get('/api/posts/feed?page=1&pageSize=20')
            assert.equal(latest.status(), 200)
            outages.push('recommendation service: homepage/latest feed available')
        }
        finally { compose('start', 'recommendation-service') }
    }
    const summary = { strategy: feed.strategy, firstPage: feed.items.length, secondPage: second.items.length,
        imageStatus: image.status(), readStatus: 204, clickStatus: 204, outages, at: new Date().toISOString() }
    await writeFile(path.join(root, 'artifacts/gorse-e2e-report.json'), JSON.stringify(summary, null, 2))
    console.log(JSON.stringify(summary))
}
finally { await browser.close() }
