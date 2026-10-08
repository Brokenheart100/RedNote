import assert from 'node:assert/strict'
import { test } from 'node:test'
import { useSubmissionKey } from '../../app/composables/useSubmissionKey.ts'

test('unchanged retries keep their key; edited content and completed submissions get new keys', () => {
    const submission = useSubmissionKey()
    const body = { content: 'hello', parentCommentId: null }
    const first = submission.getKey(body)
    assert.equal(submission.getKey({ ...body }), first)
    const edited = submission.getKey({ ...body, content: 'edited' })
    assert.notEqual(edited, first)
    submission.reset()
    assert.notEqual(submission.getKey({ ...body, content: 'edited' }), edited)
})
