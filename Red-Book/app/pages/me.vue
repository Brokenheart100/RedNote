<script setup lang="ts">
import type {
    UpdateMeRequest,
    UpdateMeResponse,
} from '~~/shared/types/users'

import {
    getApiErrorMessage,
    getApiErrorStatus,
} from '~/utils/api-error'

definePageMeta({
    middleware: 'auth',
})

import type {
    LikedPostsResponse,
    PostResponse,
} from '~~/shared/types/posts'


const postStore = usePostStore()

const likedPostsPageSize = 20

const likedPostsPage = ref(1)

const {
    data: likedPostsData,
    pending: likedPostsPending,
    error: likedPostsError,
    refresh: refreshLikedPosts,
} = await useAsyncData(
    'my-liked-posts',
    () => $fetch<LikedPostsResponse>('/api/posts/liked', {
        query: {
            page: likedPostsPage.value,
            pageSize: likedPostsPageSize,
        },
    }),
    {
        watch: [likedPostsPage],
    },
)

watch(
    () => likedPostsData.value?.items,
    posts => {
        if (posts) {
            postStore.upsertPosts(posts)
        }
    },
    {
        immediate: true,
    },
)

const likedPosts = computed<PostResponse[]>(() => {
    const items = likedPostsData.value?.items ?? []

    return items
        .map(post => postStore.getPost(post.id) ?? post)
        .filter(post => post.isLiked)
})

const removedLikedPostCount = computed(() => {
    const items = likedPostsData.value?.items ?? []

    return items.reduce((count, post) => {
        const current = postStore.getPost(post.id) ?? post
        return count + (current.isLiked ? 0 : 1)
    }, 0)
})

const likedPostsTotalCount = computed(() => {
    return Math.max(
        0,
        (likedPostsData.value?.totalCount ?? 0) - removedLikedPostCount.value,
    )
})

const likedPostsTotalPages = computed(() => Math.max(
    1,
    Math.ceil(likedPostsTotalCount.value / likedPostsPageSize),
))

const hasPreviousLikedPostsPage = computed(() => likedPostsPage.value > 1)

const hasNextLikedPostsPage = computed(
    () => likedPostsPage.value < likedPostsTotalPages.value,
)

function goToLikedPostsPage(page: number): void {
    if (
        page < 1
        || page > likedPostsTotalPages.value
        || page === likedPostsPage.value
    ) {
        return
    }

    likedPostsPage.value = page
}

const {
    user,
    pending,
    error,
    fetchCurrentUser,
    refreshCurrentUser,
} = useCurrentUser()

const {
    uppy: avatarUppy,
    removeAllFiles: removeAllAvatarFiles,
    destroy: destroyAvatarUploader,
} = useImageUploader({
    maxNumberOfFiles: 1,
})


const editOpen = ref(false)
const editPending = ref(false)
const avatarUploading = ref(false)
const editError = ref<string | null>(null)

const nickname = ref('')
const avatarUrl = ref('')
const bio = ref('')

const avatarFileInput = ref<HTMLInputElement | null>(null)

async function loadCurrentUser(): Promise<void> {
    if (user.value) {
        return
    }

    await fetchCurrentUser()
}

function openEditProfile(): void {
    if (!user.value) {
        return
    }

    nickname.value = user.value.nickname ?? ''
    avatarUrl.value = user.value.avatarUrl ?? ''
    bio.value = user.value.bio ?? ''

    editError.value = null
    editOpen.value = true

    if (import.meta.dev) {
        console.log('✏️ [ME PAGE] 打开编辑资料', {
            userId: user.value.userId,
        })
    }
}

function selectAvatar(): void {
    avatarFileInput.value?.click()
}

async function handleAvatarSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement
    const file = input.files?.[0]

    // 允许用户再次选择同一个文件。
    input.value = ''

    if (!file || avatarUploading.value) {
        return
    }

    avatarUploading.value = true
    editError.value = null

    try {
        removeAllAvatarFiles()

        avatarUppy.addFile({
            source: 'AvatarUploader',
            name: file.name,
            type: file.type,
            data: file,
        })

        const result = await avatarUppy.upload()

        if (result?.failed?.length) {
            throw result.failed[0]?.error
            ?? new Error('Avatar upload failed.')
        }

        const uploadedFile = avatarUppy.getFiles()[0]
        const media = uploadedFile?.response?.body

        if (!media?.id) {
            throw new Error(
                'Media upload response does not contain an id.',
            )
        }

        avatarUrl.value = `/api/media/${media.id}`

        if (import.meta.dev) {
            console.log('✅ [ME PAGE] 头像上传成功', {
                mediaId: media.id,
                fileName: media.fileName,
            })
        }
    }
    catch (error: unknown) {
        const message = error instanceof Error
            ? error.message
            : ''

        if (
            message.includes('10 MB')
            || message.includes('maximum allowed size')
        ) {
            editError.value = '头像不能超过 10 MB。'
        }
        else if (
            message.includes('file type')
            || message.includes('allowed')
        ) {
            editError.value = '头像仅支持 JPEG、PNG、WebP。'
        }
        else {
            editError.value = '头像上传失败，请稍后重试。'
        }

        console.error('❌ [ME PAGE] 头像上传失败', {
            message: editError.value,
        })
    }
    finally {
        avatarUploading.value = false
        removeAllAvatarFiles()
    }
}

async function saveProfile(): Promise<void> {
    if (
        editPending.value
        || avatarUploading.value
        || !user.value
    ) {
        return
    }

    editPending.value = true
    editError.value = null

    const request: UpdateMeRequest = {
        nickname: nickname.value.trim() || null,
        avatarUrl: avatarUrl.value.trim() || null,
        bio: bio.value.trim() || null,
    }

    try {
        const result = await $fetch<UpdateMeResponse>(
            '/api/users/me',
            {
                method: 'PATCH',
                body: request,
            },
        )

        if (import.meta.dev) {
            console.log('✅ [ME PAGE] 用户资料更新成功', {
                userId: result.userId,
                updatedAtUtc: result.updatedAtUtc,
            })
        }

        await refreshCurrentUser()
        editOpen.value = false
    }
    catch (error: unknown) {
        const status = getApiErrorStatus(error)

        if (status === 401) {
            editError.value = '登录状态已失效，请重新登录。'
        }
        else if (status === 400) {
            editError.value = getApiErrorMessage(
                error,
                '提交的资料不正确。',
            )
        }
        else {
            editError.value = getApiErrorMessage(
                error,
                '资料更新失败，请稍后重试。',
            )
        }

        console.error('❌ [ME PAGE] 用户资料更新失败', {
            status: status ?? null,
            message: editError.value,
        })
    }
    finally {
        editPending.value = false
    }
}

async function handleRefresh(): Promise<void> {
    await Promise.all([
        refreshCurrentUser(),
        refreshLikedPosts(),
    ])
}

onMounted(async () => {
    await loadCurrentUser()
})

onBeforeUnmount(() => {
    destroyAvatarUploader()
})
</script>

<template>
    <div class="mx-auto w-full max-w-5xl space-y-6">
        <div class="flex items-center justify-between gap-4">
            <div>
                <h1 class="text-2xl font-semibold">
                    个人主页
                </h1>

                <p class="mt-1 text-sm text-muted">
                    查看和管理你的个人资料
                </p>
            </div>

            <UButton icon="i-lucide-refresh-cw" variant="ghost" :loading="pending" :disabled="pending"
                @click="handleRefresh">
                刷新
            </UButton>
        </div>

        <UAlert v-if="error" color="error" variant="soft" icon="i-lucide-circle-alert" title="用户资料加载失败"
            :description="error" />

        <div v-if="pending && !user" class="space-y-4">
            <USkeleton class="h-40 w-full" />
            <USkeleton class="h-40 w-full" />
        </div>

        <UserProfileHeader v-else-if="user" :user="user" :pending="pending" @edit="openEditProfile" />

        <UAlert v-else color="neutral" variant="soft" icon="i-lucide-user-x" title="暂无用户资料"
            description="当前没有可显示的用户资料。" />

        <section v-if="user" class="space-y-4">
            <div class="flex items-center justify-between gap-4">
                <div>
                    <div class="flex items-center gap-2">
                        <UIcon name="i-lucide-heart" class="size-5" />

                        <h2 class="text-xl font-semibold">
                            我喜欢的帖子
                        </h2>
                    </div>

                    <p class="mt-1 text-sm text-muted">
                        共 {{ likedPostsTotalCount }} 篇
                    </p>
                </div>
            </div>

            <PostFeedGrid :posts="likedPosts" :pending="likedPostsPending" :error="likedPostsError"
                :page="likedPostsPage" :total-pages="likedPostsTotalPages" :skeleton-count="6"
                empty-icon="i-lucide-heart" empty-title="还没有喜欢的帖子" empty-description="给喜欢的内容点个赞后，它们会显示在这里。"
                @refresh="refreshLikedPosts" @page-change="goToLikedPostsPage" />
        </section>

        <UModal v-model:open="editOpen" title="编辑资料" description="修改头像、昵称和个人简介。">
            <template #body>
                <form class="space-y-5" @submit.prevent="saveProfile">
                    <UFormField label="头像" description="支持 JPEG、PNG、WebP，最大 10 MB">
                        <div class="flex items-center gap-4">
                            <UAvatar :src="avatarUrl || undefined" :alt="nickname || '头像'" size="3xl" />

                            <div>
                                <input ref="avatarFileInput" type="file" accept="image/jpeg,image/png,image/webp"
                                    class="hidden" @change="handleAvatarSelected">

                                <UButton type="button" icon="i-lucide-image-up" variant="outline"
                                    :loading="avatarUploading" :disabled="avatarUploading || editPending"
                                    @click="selectAvatar">
                                    上传头像
                                </UButton>

                                <UButton v-if="avatarUrl" type="button" color="neutral" variant="ghost" class="ml-2"
                                    :disabled="avatarUploading || editPending" @click="avatarUrl = ''">
                                    移除头像
                                </UButton>
                            </div>
                        </div>
                    </UFormField>

                    <UFormField label="昵称" description="最多 64 个字符">
                        <UInput v-model="nickname" maxlength="64" placeholder="请输入昵称" :disabled="editPending"
                            class="w-full" />
                    </UFormField>

                    <UFormField label="个人简介" description="最多 500 个字符">
                        <UTextarea v-model="bio" :rows="5" maxlength="500" placeholder="介绍一下自己" :disabled="editPending"
                            class="w-full" />

                        <div class="mt-1 text-right text-xs text-muted">
                            {{ bio.length }}/500
                        </div>
                    </UFormField>

                    <UAlert v-if="editError" color="error" variant="soft" icon="i-lucide-circle-alert" title="保存失败"
                        :description="editError" />

                    <div class="flex justify-end gap-3">
                        <UButton type="button" color="neutral" variant="ghost"
                            :disabled="editPending || avatarUploading" @click="editOpen = false">
                            取消
                        </UButton>

                        <UButton type="submit" icon="i-lucide-save" :loading="editPending"
                            :disabled="editPending || avatarUploading">
                            保存
                        </UButton>
                    </div>
                </form>
            </template>
        </UModal>
    </div>
</template>