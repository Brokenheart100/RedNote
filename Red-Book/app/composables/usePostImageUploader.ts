import {
    useImageUploader,
} from '~/composables/useImageUploader'

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
