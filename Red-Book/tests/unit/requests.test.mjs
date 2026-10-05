import assert from 'node:assert/strict'
import { test } from 'node:test'
import {
    parsePost, parseLogin, parseRegister, parseProfile, parseComment,
    parsePagination, parseSearch, RequestValidationError,
    loginSchema, registerSchema, postSchema, profileSchema, commentSchema,
} from '../../shared/schemas/requests.ts'
import { getFetchErrorData } from '../../server/utils/fetch-error.ts'
import { createLatestRequest } from '../../app/utils/latest-request.ts'
import { getApiErrorMessage } from '../../app/utils/api-error.ts'

const id = '11111111-1111-1111-1111-111111111111'
const valid = { title: 'title', content: 'content', mediaIds: [id], tags: ['Vue'] }

test('shared schemas and boundary parsers normalize the same form payloads', () => {
    const cases = [
        [loginSchema, parseLogin, { email: ' a@example.com ', password: ' pass ' }],
        [registerSchema, parseRegister, { email: 'a@example.com', password: 'pass', displayName: '  ' }],
        [postSchema, parsePost, { ...valid, title: ' title ', tags: [' Vue ', 'vue'] }],
        [profileSchema, parseProfile, { nickname: ' Nick ', avatarUrl: `/api/media/${id}`, bio: '  ' }],
        [commentSchema, parseComment, { content: ' Hello ' }],
    ]
    for (const [schema, parser, input] of cases) {
        assert.deepEqual(schema.parse(input), parser(input))
    }
    assert.equal(registerSchema.parse({ email: 'a@example.com', password: 'pass' }).displayName, null)
})

test('retain UTF-16 limits, field errors and strip unknown request fields', () => {
    assert.equal(parsePost({ ...valid, title: '😀'.repeat(50), admin: true }).title.length, 100)
    assert.throws(() => parsePost({ ...valid, title: '😀'.repeat(51) }),
        error => error instanceof RequestValidationError && error.field === 'title')
    assert.equal('admin' in parsePost({ ...valid, admin: true }), false)
    assert.throws(() => parseProfile({ bio: 'x'.repeat(501) }),
        error => error instanceof RequestValidationError && error.field === 'bio')
    assert.throws(() => parsePost({ ...valid, title: 42 }),
        error => error.field === 'title' && error.message === 'title must be a string.')
})

test('reject malformed JSON shapes and array elements before business logic', () => {
    for (const value of [null, [], 3, { ...valid, title: 42 },
        { ...valid, tags: [123] }, { ...valid, mediaIds: ['bad-id'] }]) {
        assert.throws(() => parsePost(value), RequestValidationError)
    }
})

test('normalize and deduplicate tags and image IDs', () => {
    assert.deepEqual(parsePost({
        ...valid, title: ' title ', mediaIds: [id, id], tags: [' Vue ', 'vue', 'Nuxt'],
    }), { ...valid, mediaIds: [id], tags: ['Vue', 'Nuxt'] })
})

test('validate credentials without trimming passwords', () => {
    assert.deepEqual(parseLogin({ email: ' a@example.com ', password: ' pass ' }),
        { email: 'a@example.com', password: ' pass ' })
    assert.throws(() => parseLogin({ email: 'invalid', password: 'password' }))
    assert.throws(() => parseRegister({ email: 'a@example.com', password: [] }))
})

test('validate profile and comments including URL protocols and parent IDs', () => {
    assert.throws(() => parseProfile({ nickname: 2 }))
    assert.throws(() => parseProfile({ avatarUrl: 'javascript:alert(1)' }))
    assert.throws(() => parseComment({ content: 'hello', parentCommentId: 'invalid' }))
    assert.equal(parseComment({ content: ' hello ', parentCommentId: id }).content, 'hello')
})

test('avatar accepts a stable local media path but rejects other relative URLs', () => {
    assert.equal(parseProfile({ avatarUrl: `/api/media/${id}` }).avatarUrl, `/api/media/${id}`)
    assert.equal(parseProfile({ avatarUrl: 'https://example.com/avatar.png' }).avatarUrl,
        'https://example.com/avatar.png')
    for (const avatarUrl of ['/api/media/not-a-guid', '/api/media/' + id + '?redirect=evil',
        '//example.com/avatar.png', '/api/users/me', '/api/media/../users/me',
        '/api/media/00000000-0000-0000-0000-000000000000', 'data:image/png;base64,abc']) {
        assert.throws(() => parseProfile({ avatarUrl }), RequestValidationError)
    }
})

test('pagination rejects partial numbers, arrays, overflow and zero', () => {
    for (const page of ['1abc', '0', '-1', ['1'], '99999999999999999']) {
        assert.throws(() => parsePagination({ page }))
    }
    assert.deepEqual(parsePagination({}), { page: 1, pageSize: 20 })
    assert.throws(() => parsePagination({ pageSize: '101' }))
    assert.equal(parseSearch({ q: ' Vue ' }).q, 'Vue')
})

test('upstream internal details do not cross the error boundary', () => {
    assert.equal(getFetchErrorData({ status: 500, data: { detail: 'secret', errors: { db: ['secret'] } } }), undefined)
    assert.deepEqual(getFetchErrorData({
        status: 400, data: { detail: 'internal', errors: { title: ['Required'], bad: [123] } },
    }), { errors: { title: ['Required'] } })
})

test('reset and navigation abort old requests and reject late writes', () => {
    const requests = createLatestRequest()
    const a = requests.start()
    const b = requests.start()
    assert.equal(a.signal.aborted, true)
    assert.equal(a.isCurrent(), false)
    assert.equal(b.isCurrent(), true)
    requests.invalidate()
    assert.equal(b.signal.aborted, true)
    assert.equal(b.isCurrent(), false)
})

test('UI reads validation messages from the Nitro error envelope', () => {
    assert.equal(getApiErrorMessage({
        statusCode: 400,
        data: { message: 'Invalid request', data: { errors: { title: ['Title required'] } } },
    }, 'Fallback'), 'Title required')
})
