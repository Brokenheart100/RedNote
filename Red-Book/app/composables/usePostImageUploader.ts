import type { MediaUploadResponse } from '~~/shared/types/media'

import {
    useImageUploader,
    type UploadMeta,
} from '~/composables/useImageUploader'

export type { MediaUploadResponse } from '~~/shared/types/media'
export type { UploadMeta } from '~/composables/useImageUploader'

const MAX_IMAGE_COUNT = 9

export function usePostImageUploader() {
    const uploader = useImageUploader({
        maxNumberOfFiles: MAX_IMAGE_COUNT,
    })

    return {
        ...uploader,
        maxImageCount: MAX_IMAGE_COUNT,
    }
}