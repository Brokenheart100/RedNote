<script setup lang="ts">
import { useDocumentVisibility, useIntersectionObserver, useTimeoutFn } from '@vueuse/core'
import type { PostResponse } from '~~/shared/types/posts'

const props = defineProps<{
    post: PostResponse
    recommendationRequestId?: string
}>()

const emit = defineEmits<{
    feedback: [requestId: string, postId: string, type: 'read' | 'click']
}>()
const article = ref<HTMLElement | null>(null)
const visible = ref(false)
const documentVisibility = useDocumentVisibility()
let readRecorded = false
const { start: startRead, stop: stopRead } = useTimeoutFn(() => {
    if (!readRecorded && visible.value && documentVisibility.value === 'visible' && props.recommendationRequestId) {
        readRecorded = true
        emit('feedback', props.recommendationRequestId, props.post.id, 'read')
    }
}, 1000, { immediate: false })
useIntersectionObserver(article, ([entry]) => { visible.value = Boolean(entry?.isIntersecting && entry.intersectionRatio >= 0.5) }, { threshold: 0.5 })
watch([visible, documentVisibility], ([isVisible, pageVisibility]) => {
    if (props.recommendationRequestId && isVisible && pageVisibility === 'visible' && !readRecorded) startRead()
    else stopRead()
})

const postStore = usePostStore()
if (!postStore.getPost(props.post.id)) {
    postStore.upsertPost(props.post)
}

const currentPost = computed<PostResponse>(() => {
    return postStore.getPost(props.post.id) ?? props.post
})

const cover = computed(() => currentPost.value.media[0] ?? null)

const authorName = computed(() => {
    const nickname = currentPost.value.author.nickname?.trim()
    return nickname || currentPost.value.authorUserId.slice(0, 8)
})

const authorAvatarUrl = computed(() => currentPost.value.author.avatarUrl ?? undefined)

const authorAvatarFallback = computed(() => {
    return authorName.value.trim().charAt(0).toUpperCase() || 'R'
})

const tags = computed(() => currentPost.value.tags ?? [])
const liked = computed(() => currentPost.value.isLiked)
const likeCount = computed(() => currentPost.value.likeCount)
const likePending = computed(() => postStore.isLikePending(currentPost.value.id))

const detailOpen = ref(false)

function openDetail(): void {
    if (props.recommendationRequestId) emit('feedback', props.recommendationRequestId, props.post.id, 'click')
    detailOpen.value = true
}

function formatCount(value: number): string {
    if (value < 1_000) {
        return value.toString()
    }

    if (value < 10_000) {
        return `${(value / 1_000).toFixed(1).replace(/\.0$/, '')}k`
    }

    return `${(value / 10_000).toFixed(1).replace(/\.0$/, '')}w`
}

async function toggleLike(): Promise<void> {
    if (likePending.value) {
        return
    }

    try {
        await postStore.toggleLike(currentPost.value.id)
    }
    catch {
        /*
         * Store 已完成日志记录与 optimistic rollback。
         * Card 不重复处理错误。
         */
    }
}
</script>

<template>
    <article ref="article" class="group min-w-0 cursor-pointer overflow-hidden rounded-xl" tabindex="0" role="button"
        :aria-label="`查看帖子：${currentPost.title}`" @click="openDetail" @keydown.enter="openDetail"
        @keydown.space.prevent="openDetail">
        <!-- 封面 -->
        <div class="relative overflow-hidden rounded-xl bg-muted">
            <div class="aspect-3/4 w-full">
                <NuxtImg v-if="cover" :src="cover.url" :alt="currentPost.title" densities="1"
                    class="size-full object-cover transition-transform duration-300 group-hover:scale-[1.02]"
                    loading="lazy" decoding="async" />

                <div v-else class="flex size-full items-center justify-center">
                    <UIcon name="i-lucide-image" class="size-10 text-muted" />
                </div>
            </div>

            <div v-if="currentPost.media.length > 1"
                class="absolute right-2 top-2 flex items-center gap-1 rounded-full bg-black/55 px-2 py-1 text-xs text-white backdrop-blur-sm">
                <UIcon name="i-lucide-images" class="size-3.5" />
                <span>{{ currentPost.media.length }}</span>
            </div>
        </div>

        <!-- 内容 -->
        <div class="space-y-3 px-1 py-3">
            <h2 class="line-clamp-2 text-sm font-medium leading-5 text-highlighted" :title="currentPost.title">
                {{ currentPost.title }}
            </h2>

            <div v-if="tags.length > 0" class="flex min-w-0 gap-1.5 overflow-hidden">
                <span v-for="tag in tags.slice(0, 2)" :key="tag" class="max-w-24 truncate text-xs text-muted">
                    #{{ tag }}
                </span>
            </div>

            <div class="flex min-w-0 items-center justify-between gap-3">
                <div class="flex min-w-0 items-center gap-2" :title="authorName">
                    <UAvatar densities="1" :src="authorAvatarUrl" :alt="authorName" :text="authorAvatarFallback" size="xs"
                        class="shrink-0" />

                    <span class="min-w-0 truncate text-xs text-muted">
                        {{ authorName }}
                    </span>
                </div>

                <div class="flex shrink-0 items-center gap-3 text-xs text-muted">
                    <button type="button"
                        class="flex items-center gap-1 rounded-md transition-colors hover:text-error disabled:cursor-default disabled:opacity-60"
                        :class="{ 'text-error': liked }" :disabled="likePending" :aria-pressed="liked"
                        :aria-label="liked ? '取消点赞' : '点赞'" :title="liked ? '取消点赞' : '点赞'" @click.stop="toggleLike">
                        <UIcon name="i-lucide-heart" class="size-4 transition-transform" :class="{
                            'fill-current': liked,
                            'scale-110': liked,
                        }" />

                        <span>{{ formatCount(likeCount) }}</span>
                    </button>

                    <span class="flex items-center gap-1" :title="`${currentPost.commentCount} 条评论`">
                        <UIcon name="i-lucide-message-circle" class="size-4" />
                        <span>{{ formatCount(currentPost.commentCount) }}</span>
                    </span>
                </div>
            </div>
        </div>
    </article>

    <PostDetailModal v-model:open="detailOpen" :post="currentPost" />
</template>
