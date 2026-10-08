<script setup lang="ts">
import { parsePost } from '~~/shared/schemas/requests'
import { getRequestValidationMessage } from '~/utils/request-validation'
import type {
    PostResponse,
} from '~~/shared/types/posts'

import PostImageUploader from '~/components/publish/PostImageUploader.vue'

import {
    getApiErrorMessage,
    getApiErrorStatus,
} from '~/utils/api-error'

definePageMeta({
    middleware: 'auth',
})

useSeoMeta({
    title: '发布笔记',
    description: '在 RedNote 发布新的生活笔记。',
    robots: 'noindex, nofollow',
})

const toast = useToast()
const postStore = usePostStore()
const submissionKey = useSubmissionKey()

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

const maxTagCount = 10
const fileCount = ref(uppy.getFiles().length)

const remainingTitle = computed(() => 100 - title.value.length)
const remainingContent = computed(() => 5000 - content.value.length)

function refreshFileCount(): void {
    fileCount.value = uppy.getFiles().length
}

uppy.on('file-added', refreshFileCount)
uppy.on('file-removed', refreshFileCount)

function addTag(): void {
    const value = tagInput.value.trim().replace(/^#+/, '')

    if (!value) {
        return
    }

    if (value.length > 30) {
        errorMessage.value = '单个标签不能超过 30 个字符。'
        return
    }

    if (tags.value.includes(value)) {
        tagInput.value = ''
        return
    }

    if (tags.value.length >= maxTagCount) {
        errorMessage.value = `最多添加 ${maxTagCount} 个标签。`
        return
    }

    tags.value.push(value)
    tagInput.value = ''
    errorMessage.value = null
}

function removeTag(tag: string): void {
    tags.value = tags.value.filter(item => item !== tag)
}

function handleTagKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Enter' && event.key !== ',') {
        return
    }

    event.preventDefault()
    addTag()
}

function validate(): string | null {
    if (fileCount.value > maxImageCount) {
        return `最多只能上传 ${maxImageCount} 张图片。`
    }
    try {
        // Validate text before uploading; IDs are checked once uploads complete.
        parsePost({ title: title.value, content: content.value, tags: tags.value, mediaIds: [] })
    }
    catch (error) {
        return getRequestValidationMessage(error) ?? '发布内容不符合要求。'
    }
    return null
}

function resolvePublishError(error: unknown): string {
    const validationMessage = getRequestValidationMessage(error)
    if (validationMessage) return validationMessage
    switch (getApiErrorStatus(error)) {
        case 400:
            return getApiErrorMessage(error, '发布内容不符合要求，请检查后重试。')

        case 401:
            return '登录状态已失效，请重新登录。'

        case 403:
            return '当前账号没有发布权限。'
        case 409:
            return '这次提交仍在处理中，请稍后重试。'

        case 413:
            return '上传内容过大，请减少图片数量或文件大小。'

        case 429:
            return '发布请求过于频繁，请稍后再试。'

        default:
            return getApiErrorMessage(error, '发布失败，请稍后重试。')
    }
}

async function publish(): Promise<void> {
    if (submitting.value) {
        return
    }

    errorMessage.value = null
    addTag()

    const validationError = validate()

    if (validationError) {
        errorMessage.value = validationError
        return
    }

    submitting.value = true

    try {
        if (uppy.getFiles().length > 0) {
            const result = await uppy.upload()

            if (result?.failed && result.failed.length > 0) {
                throw new Error('One or more images failed to upload.')
            }
        }

        const mediaIds = getUploadedMediaIds()

        if (mediaIds.length !== uppy.getFiles().length) {
            throw new Error('Uploaded media response is incomplete.')
        }

        const request = parsePost({
            title: title.value,
            content: content.value,
            mediaIds,
            tags: [...tags.value],
        })

        const post = await $fetch<PostResponse>('/api/posts', {
            method: 'POST',
            headers: { 'Idempotency-Key': submissionKey.getKey(request) },
            retry: 0,
            body: request,
        })

        submissionKey.reset()
        postStore.upsertPost(post)

        if (import.meta.dev) {
            console.log('✅ [PUBLISH] 笔记发布成功', {
                postId: post.id,
                authorUserId: post.authorUserId,
            })
        }

        toast.add({
            title: '发布成功',
            description: '你的笔记已经发布。',
            color: 'success',
            icon: 'i-lucide-circle-check',
        })

        await refreshNuxtData('home-feed')
        await navigateTo('/')
    }
    catch (error: unknown) {
        errorMessage.value = resolvePublishError(error)

        console.error('❌ [PUBLISH] 笔记发布失败', {
            status: getApiErrorStatus(error) ?? null,
            message: errorMessage.value,
        })
    }
    finally {
        submitting.value = false
    }
}

onBeforeUnmount(() => {
    uppy.off('file-added', refreshFileCount)
    uppy.off('file-removed', refreshFileCount)
    destroy()
})
</script>
<template>
    <main class="
        mx-auto w-full
        max-w-6xl
        px-4 py-8
        sm:px-6
    ">
        <header class="
            mb-6 flex
            items-start
            justify-between gap-4
        ">
            <div>
                <h1 class="
                    text-2xl
                    font-semibold
                ">
                    发布笔记
                </h1>

                <p class="
                    mt-1
                    text-sm
                    text-muted
                ">
                    分享这一刻的生活和想法
                </p>
            </div>

            <UButton label="发布" icon="i-lucide-send" size="lg" :loading="submitting" :disabled="submitting"
                @click="publish" />
        </header>


        <UAlert v-if="errorMessage" class="mb-6" color="error" variant="soft" icon="i-lucide-circle-alert"
            title="暂时无法发布" :description="errorMessage" />


        <div class="
            grid gap-6
            lg:grid-cols-[minmax(0,1fr)_22rem]
        ">
            <UCard>
                <div class="space-y-7">

                    <PostImageUploader :uppy="uppy" :disabled="submitting" :max-files="maxImageCount" />


                    <USeparator />


                    <UFormField label="标题" required>
                        <UInput v-model="title" class="w-full" size="xl" placeholder="写一个吸引人的标题" :maxlength="100"
                            :disabled="submitting" />

                        <template #hint>
                            {{ remainingTitle }}/100
                        </template>
                    </UFormField>


                    <UFormField label="正文" required>
                        <UTextarea v-model="content" class="w-full" autoresize :rows="10" :maxlength="5000"
                            placeholder="分享你的真实体验和感受..." :disabled="submitting" />

                        <template #hint>
                            {{ remainingContent }}/5000
                        </template>
                    </UFormField>


                    <UFormField label="标签" description="按 Enter 或逗号添加标签">
                        <UInput v-model="tagInput" class="w-full" placeholder="例如：摄影" icon="i-lucide-tag"
                            :maxlength="30" :disabled="submitting
                                || tags.length
                                >= maxTagCount
                                " @keydown="handleTagKeydown" @blur="addTag" />

                        <div v-if="tags.length" class="
                                mt-3 flex
                                flex-wrap gap-2
                            ">
                            <UBadge v-for="tag in tags" :key="tag" color="neutral" variant="soft" size="lg">
                                #{{ tag }}

                                <button type="button" class="
                                        ml-1 inline-flex
                                        items-center
                                    " :disabled="submitting" @click="
                                        removeTag(
                                            tag,
                                        )
                                        ">
                                    <UIcon name="i-lucide-x" class="size-3.5" />
                                </button>
                            </UBadge>
                        </div>
                    </UFormField>
                </div>
            </UCard>


            <aside class="
                space-y-4
                lg:sticky
                lg:top-24
                lg:self-start
            ">
                <UCard>
                    <template #header>
                        <div class="
                            flex items-center
                            gap-2 font-medium
                        ">
                            <UIcon name="i-lucide-circle-check" class="
                                    size-5
                                    text-primary
                                " />

                            发布检查
                        </div>
                    </template>


                    <div class="
                        space-y-3
                        text-sm
                    ">
                        <div class="
                            flex
                            justify-between
                        ">
                            <span class="text-muted">
                                图片
                            </span>

                            <span>
                                {{ fileCount }}/9
                            </span>
                        </div>


                        <div class="
                            flex
                            justify-between
                        ">
                            <span class="text-muted">
                                标题
                            </span>

                            <span>
                                {{ title.length }}/100
                            </span>
                        </div>


                        <div class="
                            flex
                            justify-between
                        ">
                            <span class="text-muted">
                                正文
                            </span>

                            <span>
                                {{ content.length }}/5000
                            </span>
                        </div>


                        <div class="
                            flex
                            justify-between
                        ">
                            <span class="text-muted">
                                标签
                            </span>

                            <span>
                                {{ tags.length }}/10
                            </span>
                        </div>
                    </div>


                    <template #footer>
                        <UButton label="发布笔记" icon="i-lucide-send" size="lg" block :loading="submitting"
                            :disabled="submitting" @click="publish" />
                    </template>
                </UCard>
            </aside>
        </div>
    </main>
</template>
