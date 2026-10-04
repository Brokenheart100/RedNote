import type {
    CreatePostRequest,
    PostResponse,
} from '~~/shared/types/posts'

import {
    getApiErrorMessage,
    getApiErrorStatus,
} from '~/utils/api-error'

const MAX_TITLE_LENGTH = 100
const MAX_CONTENT_LENGTH = 5000
const MAX_TAG_COUNT = 10
const MAX_TAG_LENGTH = 30

export function usePublishPost() {
    const toast = useToast()

    const {
        uppy,
        maxImageCount,
        getUploadedMediaIds,
        destroy,
    } = usePostImageUploader()

    const title = ref('')
    const content = ref('')
    const tagInput = ref('')
    const tags = ref<string[]>([])

    const submitting = ref(false)
    const errorMessage = ref<string | null>(null)

    const fileCount = ref(uppy.getFiles().length)

    const remainingTitle = computed(
        () => MAX_TITLE_LENGTH - title.value.length,
    )

    const remainingContent = computed(
        () => MAX_CONTENT_LENGTH - content.value.length,
    )

    function refreshFileCount(): void {
        fileCount.value = uppy.getFiles().length
    }

    function addTag(): boolean {
        const value = tagInput.value
            .trim()
            .replace(/^#+/, '')

        if (!value) {
            return true
        }

        if (value.length > MAX_TAG_LENGTH) {
            errorMessage.value =
                `单个标签不能超过 ${MAX_TAG_LENGTH} 个字符。`

            return false
        }

        if (tags.value.includes(value)) {
            tagInput.value = ''
            return true
        }

        if (tags.value.length >= MAX_TAG_COUNT) {
            errorMessage.value =
                `最多添加 ${MAX_TAG_COUNT} 个标签。`

            return false
        }

        tags.value.push(value)
        tagInput.value = ''
        errorMessage.value = null

        return true
    }

    function removeTag(tag: string): void {
        tags.value = tags.value.filter(
            item => item !== tag,
        )
    }

    function handleTagKeydown(event: KeyboardEvent): void {
        if (
            event.key !== 'Enter'
            && event.key !== ','
        ) {
            return
        }

        event.preventDefault()
        addTag()
    }

    function validate(): string | null {
        const normalizedTitle = title.value.trim()
        const normalizedContent = content.value.trim()

        if (!normalizedTitle) {
            return '请输入标题。'
        }

        if (normalizedTitle.length > MAX_TITLE_LENGTH) {
            return `标题不能超过 ${MAX_TITLE_LENGTH} 个字符。`
        }

        if (!normalizedContent) {
            return '请输入正文。'
        }

        if (normalizedContent.length > MAX_CONTENT_LENGTH) {
            return `正文不能超过 ${MAX_CONTENT_LENGTH} 个字符。`
        }

        if (fileCount.value > maxImageCount) {
            return `最多只能上传 ${maxImageCount} 张图片。`
        }

        return null
    }

    function resolvePublishError(error: unknown): string {
        const status = getApiErrorStatus(error)

        if (status === 401) {
            return '登录状态已失效，请重新登录。'
        }

        if (status === 403) {
            return '当前账号没有发布权限。'
        }

        return getApiErrorMessage(
            error,
            '发布失败，请稍后重试。',
        )
    }

    async function uploadImages(): Promise<string[]> {
        const files = uppy.getFiles()

        if (files.length === 0) {
            return []
        }

        const result = await uppy.upload()

        if (result?.failed?.length) {
            throw new Error(
                'One or more images failed to upload.',
            )
        }

        const mediaIds = getUploadedMediaIds()

        if (mediaIds.length !== files.length) {
            throw new Error(
                'Uploaded media response is incomplete.',
            )
        }

        return mediaIds
    }

    async function publish(): Promise<void> {
        if (submitting.value) {
            return
        }

        errorMessage.value = null

        if (!addTag()) {
            return
        }

        const validationError = validate()

        if (validationError) {
            errorMessage.value = validationError
            return
        }

        submitting.value = true

        try {
            const mediaIds = await uploadImages()

            const request: CreatePostRequest = {
                title: title.value.trim(),
                content: content.value.trim(),
                mediaIds,
                tags: [...tags.value],
            }

            const post = await $fetch<PostResponse>(
                '/api/posts',
                {
                    method: 'POST',
                    body: request,
                },
            )

            if (import.meta.dev) {
                console.log('✅ [PUBLISH] 发布笔记成功', {
                    postId: post.id,
                })
            }

            toast.add({
                title: '发布成功',
                description: '你的笔记已经发布。',
                color: 'success',
                icon: 'i-lucide-circle-check',
            })

            await navigateTo('/')
        }
        catch (error: unknown) {
            errorMessage.value =
                resolvePublishError(error)

            console.error('❌ [PUBLISH] 发布笔记失败', {
                status:
                    getApiErrorStatus(error)
                    ?? null,

                message:
                    errorMessage.value,
            })
        }
        finally {
            submitting.value = false
        }
    }

    uppy.on('file-added', refreshFileCount)
    uppy.on('file-removed', refreshFileCount)

    onBeforeUnmount(() => {
        uppy.off('file-added', refreshFileCount)
        uppy.off('file-removed', refreshFileCount)

        destroy()
    })

    return {
        uppy,

        title,
        content,
        tagInput,
        tags,

        submitting,
        errorMessage,

        fileCount,
        maxImageCount,
        maxTagCount: MAX_TAG_COUNT,

        remainingTitle,
        remainingContent,

        addTag,
        removeTag,
        handleTagKeydown,
        publish,
    }
}