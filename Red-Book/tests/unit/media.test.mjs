import assert from 'node:assert/strict'
import { test } from 'node:test'
import { mediaUploadResponseSchema } from '../../shared/schemas/media.ts'

const media = {
    id: '11111111-1111-1111-1111-111111111111',
    fileName: 'avatar.png', contentType: 'image/png', size: 67,
    objectKey: 'images/avatar.png', createdAtUtc: '2026-10-05T00:00:00Z',
}

test('upload contract rejects malformed responses before they become image references', () => {
    for (const value of [null, [], {}, { ...media, id: 'bad-id' },
        { ...media, size: -1 }, { ...media, size: '67' },
        { ...media, size: 1.5 }, { ...media, objectKey: '' }]) {
        assert.equal(mediaUploadResponseSchema.safeParse(value).success, false)
    }
    assert.deepEqual(mediaUploadResponseSchema.parse({ ...media, internal: 'hidden' }), media)
})
