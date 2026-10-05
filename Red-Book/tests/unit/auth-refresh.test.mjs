import assert from 'node:assert/strict'
import { test } from 'node:test'
import { createTokenRefresher, shouldRefreshToken, TokenRefreshError } from '../../server/utils/token-refresher.ts'

function expired() {
    return { accessToken: 'old', refreshToken: 'refresh-old', tokenType: 'Bearer', expiresAt: Date.now() - 1 }
}
function renewed() {
    return { accessToken: 'new', refreshToken: 'refresh-new', tokenType: 'Bearer', expiresAt: Date.now() + 3_600_000 }
}
function repository() {
    const tokens = new Map([['session', expired()]])
    const locks = new Map()
    const equal = (a, b) => JSON.stringify(a) === JSON.stringify(b)
    return {
        tokens, locks,
        async get(id) { return tokens.get(id) ?? null },
        async acquire(id, owner) {
            if (locks.has(id)) return false
            locks.set(id, owner)
            return true
        },
        async release(id, owner) { if (locks.get(id) === owner) locks.delete(id) },
        async replace(id, expected, next, owner) {
            if (locks.get(id) !== owner || !equal(tokens.get(id), expected)) return false
            tokens.set(id, next)
            return true
        },
        async removeIfCurrent(id, expected, owner) {
            if (locks.get(id) === owner && equal(tokens.get(id), expected)) tokens.delete(id)
        },
    }
}
function deferred() {
    let resolve
    const promise = new Promise(r => { resolve = r })
    return { promise, resolve }
}

test('separate refresher instances share one refresh and rotated token', async () => {
    const store = repository()
    const a = createTokenRefresher(store, { pollMs: 1 })
    const b = createTokenRefresher(store, { pollMs: 1 })
    const gate = deferred()
    const entered = deferred()
    let calls = 0
    const exchange = async current => {
        calls++
        assert.equal(current.refreshToken, 'refresh-old')
        entered.resolve()
        await gate.promise
        return renewed()
    }
    const first = a('session', exchange)
    await entered.promise
    const second = b('session', exchange)
    gate.resolve()
    const values = await Promise.all([first, second])
    assert.equal(calls, 1)
    assert.equal(values[0].accessToken, 'new')
    assert.equal(values[1].refreshToken, 'refresh-new')
    assert.equal(store.locks.size, 0)
})

test('logout during refresh cannot recreate the token set', async () => {
    const store = repository()
    const entered = deferred()
    const gate = deferred()
    const result = createTokenRefresher(store)('session', async () => {
        entered.resolve()
        await gate.promise
        return renewed()
    })
    await entered.promise
    store.tokens.delete('session')
    gate.resolve()
    await assert.rejects(result, error => error.statusCode === 401)
    assert.equal(store.tokens.has('session'), false)
})

test('rejected refresh clears current tokens and releases the lock', async () => {
    const store = repository()
    await assert.rejects(createTokenRefresher(store)('session', async () => {
        throw new TokenRefreshError(401, 'Rejected')
    }), error => error.statusCode === 401)
    assert.equal(store.tokens.size, 0)
    assert.equal(store.locks.size, 0)
})

test('temporary upstream outage preserves refresh credentials', async () => {
    const store = repository()
    await assert.rejects(createTokenRefresher(store)('session', async () => {
        throw new TokenRefreshError(502, 'Unavailable')
    }), error => error.statusCode === 502)
    assert.equal(store.tokens.get('session').refreshToken, 'refresh-old')
    assert.equal(store.locks.size, 0)
})

test('expired lock holder cannot overwrite or release a new owner', async () => {
    const store = repository()
    const entered = deferred()
    const gate = deferred()
    const result = createTokenRefresher(store)('session', async () => {
        entered.resolve()
        await gate.promise
        return { ...renewed(), accessToken: 'stale-result' }
    })
    await entered.promise
    store.locks.set('session', 'new-owner')
    store.tokens.set('session', renewed())
    gate.resolve()
    assert.equal((await result).accessToken, 'new')
    assert.equal(store.locks.get('session'), 'new-owner')
})

test('refresh wait has a deadline and does not discard credentials', async () => {
    const store = repository()
    store.locks.set('session', 'busy-owner')
    await assert.rejects(
        createTokenRefresher(store, { waitMs: 0 })('session', async () => renewed()),
        error => error.statusCode === 503,
    )
    assert.equal(store.tokens.has('session'), true)
})

test('unexpired tokens skip the exchange and use a 60 second threshold', async () => {
    const store = repository()
    store.tokens.set('session', renewed())
    const result = await createTokenRefresher(store)('session', async () => {
        assert.fail('should not refresh')
    })
    assert.equal(result.accessToken, 'new')
    assert.equal(shouldRefreshToken({ ...renewed(), expiresAt: 61_000 }, 0), false)
    assert.equal(shouldRefreshToken({ ...renewed(), expiresAt: 60_000 }, 0), true)
})
