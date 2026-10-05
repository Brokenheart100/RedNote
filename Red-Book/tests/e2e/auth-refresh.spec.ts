import { expect, test } from './fixtures/auth'

// Refresh rotation/concurrency is exercised deterministically in
// tests/unit/auth-refresh.test.mjs. This test verifies the real login chain.
test('登录后可以读取当前用户，浏览器 Session 不包含 OAuth Token', async ({ authenticatedPage }) => {
    const response = await authenticatedPage.request.get('/api/users/me')
    expect(response.status()).toBe(200)
    const sessionResponse = await authenticatedPage.request.get('/api/_auth/session')
    expect(sessionResponse.status()).toBe(200)
    const session = await sessionResponse.json()
    expect(session.user).toMatchObject({ authenticated: true, provider: 'oidc' })
    expect(session).not.toHaveProperty('accessToken')
    expect(session).not.toHaveProperty('refreshToken')
})