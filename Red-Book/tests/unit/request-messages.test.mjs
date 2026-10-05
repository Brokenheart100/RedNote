import assert from 'node:assert/strict'
import { test } from 'node:test'
import { RequestValidationError, parsePost, parseProfile } from '../../shared/schemas/requests.ts'
import { getRequestValidationMessage } from '../../app/utils/request-validation.ts'

test('localized validation uses structured limits independently of English wording', () => {
    const issue = {
        code: 'custom', path: ['password'], message: 'Different wording',
        params: { minimum: 1, maximum: 1024 },
    }
    assert.equal(getRequestValidationMessage(new RequestValidationError('password', issue.message, issue)),
        '密码不能为空，且不能超过 1024 个字符。')
    assert.throws(() => parsePost({ title: '😀'.repeat(51), content: 'body', mediaIds: [], tags: [] }),
        error => getRequestValidationMessage(error) === '标题不能为空，且不能超过 100 个字符。')
    assert.throws(() => parseProfile({ bio: 'x'.repeat(501) }),
        error => getRequestValidationMessage(error) === '个人简介不能超过 500 个字符。')
    assert.equal(getRequestValidationMessage(new Error('Network failure')), undefined)
})
