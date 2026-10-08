import assert from 'node:assert/strict'
import { createServer } from 'node:http'
import test from 'node:test'
import { createUpstreamCookieHeader, getIdentityCsrfContext, getUpstreamSetCookies } from '../../server/utils/identity-antiforgery.ts'

test('shared CSRF helper preserves separate cookies and rejects missing token or cookie', async () => {
    let mode = 'valid'
    const server = createServer((req, res) => {
        assert.equal(req.url, '/api/v1/auth/csrf')
        assert.equal(req.method, 'GET')
        assert.equal(req.headers['x-request-id'], 'csrf-test')
        if (mode === 'existing') assert.equal(req.headers.cookie, 'existing=one')
        res.setHeader('Content-Type', 'application/json')
        if (mode !== 'missing-cookie' && mode !== 'existing') {
            res.setHeader('Set-Cookie', ['csrf=one; HttpOnly; Path=/', 'session=two; Expires=Wed, 21 Oct 2037 07:28:00 GMT; Path=/'])
        }
        res.end(JSON.stringify(mode === 'missing-token' ? {} : { token: 'test-token', headerName: 'X-CSRF-TOKEN' }))
    })
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve))
    const url = `http://127.0.0.1:${server.address().port}`
    const event = { context: {} }
    try {
        const result = await getIdentityCsrfContext(event, url, 'csrf-test')
        assert.equal(result.token, 'test-token')
        assert.equal(result.headerName, 'X-CSRF-TOKEN')
        assert.equal(result.setCookies.length, 2)
        assert.equal(result.cookieHeader, 'csrf=one; session=two')
        mode = 'missing-token'
        await assert.rejects(getIdentityCsrfContext(event, url, 'csrf-test'), { statusCode: 502 })
        mode = 'missing-cookie'
        await assert.rejects(getIdentityCsrfContext(event, url, 'csrf-test'), { statusCode: 502 })
        mode = 'existing'
        assert.equal((await getIdentityCsrfContext(event, url, 'csrf-test', 'existing=one')).cookieHeader, 'existing=one')
    }
    finally {
        await new Promise(resolve => server.close(resolve))
    }
})

test('cookie helpers handle empty responses without creating credentials', () => {
    assert.deepEqual(getUpstreamSetCookies(new Headers()), [])
    assert.equal(createUpstreamCookieHeader([]), '')
})
