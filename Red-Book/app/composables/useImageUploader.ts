import Uppy from '@uppy/core'
import XHRUpload from '@uppy/xhr-upload'

import type { MediaUploadResponse } from '~~/shared/types/media'
import { mediaUploadResponseSchema } from '~~/shared/schemas/media'

export type UploadMeta = Record<string, unknown>

interface UseImageUploaderOptions {
    maxNumberOfFiles: number
    maxFileSize?: number
}

const DEFAULT_MAX_FILE_SIZE = 10 * 1024 * 1024
const ALLOWED_IMAGE_TYPES = ['image/jpeg', 'image/png', 'image/webp'] as const

function parseUploadResponse(xhr: XMLHttpRequest): MediaUploadResponse {
    const result = mediaUploadResponseSchema.safeParse(JSON.parse(xhr.responseText))
    if (!result.success) {
        throw new Error('Media upload response is invalid.')
    }
    return result.data
}

export function useImageUploader(options: UseImageUploaderOptions) {
    const maxFileSize = options.maxFileSize ?? DEFAULT_MAX_FILE_SIZE

    const uppy = new Uppy<UploadMeta, MediaUploadResponse>({
        autoProceed: false,
        allowMultipleUploadBatches: false,
        restrictions: {
            maxNumberOfFiles: options.maxNumberOfFiles,
            maxFileSize,
            allowedFileTypes: [...ALLOWED_IMAGE_TYPES],
        },
    })

    uppy.use(XHRUpload<UploadMeta, MediaUploadResponse>, {
        endpoint: '/api/media/images',
        method: 'POST',
        formData: true,
        fieldName: 'file',
        withCredentials: true,
        allowedMetaFields: false,
        responseType: 'text',
        getResponseData: parseUploadResponse,
    })

    function getUploadedMediaIds(): string[] {
        return uppy.getFiles()
            .map(file => file.response?.body?.id)
            .filter((id): id is string => typeof id === 'string' && id.length > 0)
    }

    function removeAllFiles(): void {
        for (const file of uppy.getFiles()) {
            uppy.removeFile(file.id)
        }
    }

    function destroy(): void {
        uppy.destroy()
    }

    return {
        uppy,
        getUploadedMediaIds,
        removeAllFiles,
        destroy,
        maxFileSize,
        allowedImageTypes: ALLOWED_IMAGE_TYPES,
    }
}
