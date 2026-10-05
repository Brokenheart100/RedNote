<script setup lang="ts">
import type {
    PostCommentResponse,
    PostResponse,
} from '~~/shared/types/posts'
import { getApiErrorMessage } from '~/utils/api-error'

const props = defineProps<{
    post: PostResponse
}>()

const open = defineModel<boolean>('open', {
    default: false,
})

const postStore = usePostStore()
const toast = useToast()
const { loggedIn } = useUserSession()
const { user: viewer, fetchCurrentUser } = useCurrentUser()
const viewerId = computed(() => loggedIn.value ? viewer.value?.userId : undefined)
const ownsPost = computed(() => Boolean(viewerId.value && viewerId.value === currentPost.value.authorUserId))
const ownsComment = (comment: PostCommentResponse) => Boolean(viewerId.value && viewerId.value === comment.authorUserId)
const deleteTarget = ref<{ kind: 'post' } | { kind: 'comment', comment: PostCommentResponse } | null>(null)
const deletePending = computed(() => postStore.isDeletePending(currentPost.value.id) || commentDeletePending.value)
const confirmOpen = computed({
    get: () => deleteTarget.value !== null,
    set: value => { if (!value && !deletePending.value) deleteTarget.value = null },
})
const deleteDescription = computed(() => {
    if (deleteTarget.value?.kind === 'post') return '删除后，帖子及其评论将不再展示。此操作无法撤销。'
    return deleteTarget.value?.comment.parentCommentId
        ? '删除这条回复？此操作无法撤销。'
        : '删除这条评论及其全部回复？此操作无法撤销。'
})

async function confirmDelete(): Promise<void> {
    const target = deleteTarget.value
    const id = currentPost.value.id
    if (!target || deletePending.value) return
    if (target.kind === 'comment') {
        if (!ownsComment(target.comment)) return
        if (await deleteComment(target.comment)) {
            deleteTarget.value = null
            toast.add({ title: '评论已删除', color: 'success' })
        }
        return
    }
    if (!ownsPost.value) return
    try {
        await postStore.deletePost(id)
        if (id !== currentPost.value.id || !open.value) return
        deleteTarget.value = null
        open.value = false
        toast.add({ title: '帖子已删除', color: 'success' })
    }
    catch (error) {
        toast.add({ title: '帖子删除失败',
            description: getApiErrorMessage(error, '请稍后重试。'), color: 'error' })
    }
}

const currentPost = computed<PostResponse>(() => {
    return postStore.getPost(props.post.id) ?? props.post
})

const likePending = computed(() => postStore.isLikePending(currentPost.value.id))

const activeMediaIndex = ref(0)

const activeMedia = computed(() => currentPost.value.media[activeMediaIndex.value] ?? null)

const authorName = computed(() => {
    const nickname = currentPost.value.author.nickname?.trim()
    return nickname || currentPost.value.authorUserId.slice(0, 8)
})

const authorAvatarUrl = computed(() => currentPost.value.author.avatarUrl ?? undefined)

const authorAvatarFallback = computed(() => {
    return authorName.value.trim().charAt(0).toUpperCase() || 'R'
})

const tags = computed(() => currentPost.value.tags ?? [])

const hasPreviousMedia = computed(() => activeMediaIndex.value > 0)

const hasNextMedia = computed(() => {
    return activeMediaIndex.value < currentPost.value.media.length - 1
})


const {
    comments, commentsPending, commentsError, commentContent,
    commentSubmitting, localCommentCount, replyingTo, canSubmitComment,
    loadComments, submitComment, startReply, cancelReply,
    hasMoreComments, loadMoreComments,
    deleteComment, commentDeletePending,
} = usePostComments(currentPost, open)

function getCommentAuthorName(
    comment: PostCommentResponse,
): string {
    const nickname = comment.author.nickname?.trim()

    return (
        nickname
        || comment.authorUserId.slice(0, 8)
    )
}


function getCommentAuthorAvatarUrl(
    comment: PostCommentResponse,
): string | undefined {
    return (
        comment.author.avatarUrl
        ?? undefined
    )
}


function getCommentAuthorFallback(
    comment: PostCommentResponse,
): string {
    return (
        getCommentAuthorName(comment)
            .trim()
            .charAt(0)
            .toUpperCase()
        || 'R'
    )
}


const commentPlaceholder = computed(() => {
    if (!replyingTo.value) {
        return '说点什么...'
    }

    return `回复 ${getCommentAuthorName(replyingTo.value)}`
})


async function toggleLike(): Promise<void> {
    if (likePending.value) {
        return
    }

    try {
        await postStore.toggleLike(
            currentPost.value.id,
        )
    }
    catch {
        /*
         * Store 已完成：
         * - 日志
         * - optimistic rollback
         *
         * Modal 不重复处理。
         */
    }
}


watch([open, () => props.post.id], () => {
    activeMediaIndex.value = 0
    deleteTarget.value = null
    if (open.value && loggedIn.value) void fetchCurrentUser()
}, { immediate: true })

function previousMedia(): void {
    if (hasPreviousMedia.value) {
        activeMediaIndex.value--
    }
}


function nextMedia(): void {
    if (hasNextMedia.value) {
        activeMediaIndex.value++
    }
}


function selectMedia(
    index: number,
): void {
    if (
        index < 0
        || index >= currentPost.value.media.length
    ) {
        return
    }

    activeMediaIndex.value = index
}


function close(): void {
    open.value = false
}


function formatDate(
    value: string,
): string {
    const date = new Date(value)

    if (Number.isNaN(date.getTime())) {
        return value
    }

    return new Intl.DateTimeFormat(
        'zh-CN',
        {
            year: 'numeric',
            month: '2-digit',
            day: '2-digit',
            hour: '2-digit',
            minute: '2-digit',
        },
    ).format(date)
}


function formatCount(
    value: number,
): string {
    if (value < 1_000) {
        return value.toString()
    }

    if (value < 10_000) {
        return `${(value / 1_000).toFixed(1).replace(/\.0$/, '')}k`
    }

    return `${(value / 10_000).toFixed(1).replace(/\.0$/, '')}w`
}
</script>


<template>
    <UModal v-model:open="open" :dismissible="!deletePending" :ui="{
        overlay: 'bg-black/45 backdrop-blur-xl',
        content: 'w-[min(94vw,1400px)] max-w-none overflow-hidden rounded-3xl bg-default p-0 shadow-2xl ring-1 ring-white/10',
    }">
        <template #content>
            <div class="relative grid h-[min(88vh,900px)] min-h-0 lg:grid-cols-[minmax(0,1.55fr)_minmax(360px,0.85fr)]">

                <!-- ========================================================= -->
                <!-- 左侧媒体区 -->
                <!-- ========================================================= -->

                <section class="relative flex min-h-0 items-center justify-center overflow-hidden bg-black">

                    <!-- 高斯模糊背景 -->
                    <div v-if="activeMedia" class="absolute inset-0 scale-110 bg-cover bg-center opacity-60 blur-3xl"
                        :style="{
                            backgroundImage:
                                `url(${activeMedia.url})`,
                        }" aria-hidden="true" />

                    <div class="absolute inset-0 bg-black/30" aria-hidden="true" />

                    <!-- 主图 -->
                    <NuxtImg v-if="activeMedia" :src="activeMedia.url" :alt="currentPost.title" densities="1"
                        class="relative z-10 max-h-full max-w-full object-contain" decoding="async" />

                    <div v-else class="relative z-10 flex flex-col items-center gap-3 text-white/70">
                        <UIcon name="i-lucide-image-off" class="size-12" />

                        <span class="text-sm">
                            暂无图片
                        </span>
                    </div>

                    <!-- 上一张 -->
                    <button v-if="hasPreviousMedia" type="button"
                        class="absolute left-4 top-1/2 z-20 flex size-10 -translate-y-1/2 items-center justify-center rounded-full bg-black/45 text-white backdrop-blur-md transition hover:bg-black/65"
                        aria-label="上一张图片" @click.stop="previousMedia">
                        <UIcon name="i-lucide-chevron-left" class="size-6" />
                    </button>

                    <!-- 下一张 -->
                    <button v-if="hasNextMedia" type="button"
                        class="absolute right-4 top-1/2 z-20 flex size-10 -translate-y-1/2 items-center justify-center rounded-full bg-black/45 text-white backdrop-blur-md transition hover:bg-black/65"
                        aria-label="下一张图片" @click.stop="nextMedia">
                        <UIcon name="i-lucide-chevron-right" class="size-6" />
                    </button>

                    <!-- 图片计数 -->
                    <div v-if="currentPost.media.length > 1"
                        class="absolute bottom-4 left-1/2 z-20 -translate-x-1/2 rounded-full bg-black/45 px-3 py-1 text-xs text-white backdrop-blur-md">
                        {{ activeMediaIndex + 1 }} / {{ currentPost.media.length }}
                    </div>
                </section>


                <!-- ========================================================= -->
                <!-- 右侧详情 -->
                <!-- ========================================================= -->

                <section class="flex min-h-0 flex-col bg-default">

                    <!-- ===================================================== -->
                    <!-- 顶部作者 -->
                    <!-- ===================================================== -->

                    <header class="shrink-0 flex items-center justify-between border-b border-default px-6 py-4">
                        <div class="flex min-w-0 items-center gap-3">
                            <UAvatar densities="1" :src="authorAvatarUrl" :alt="authorName" :text="authorAvatarFallback" size="md"
                                class="shrink-0" />

                            <div class="min-w-0">
                                <p class="truncate text-sm font-semibold">
                                    {{ authorName }}
                                </p>

                                <p class="truncate text-xs text-muted">
                                    {{ formatDate(currentPost.createdAtUtc) }}
                                </p>
                            </div>
                        </div>

                        <div class="flex items-center gap-2">
                            <UButton v-if="ownsPost" icon="i-lucide-trash-2" color="error" variant="ghost"
                                aria-label="删除帖子" :disabled="deletePending || commentSubmitting"
                                @click="deleteTarget = { kind: 'post' }" />
                            <UButton icon="i-lucide-x" color="neutral" variant="ghost" aria-label="关闭详情"
                                :disabled="deletePending" @click="close" />
                        </div>
                    </header>


                    <!-- ===================================================== -->
                    <!-- 正文 + 评论滚动区 -->
                    <!-- ===================================================== -->

                    <div class="min-h-0 flex-1 overflow-y-auto px-6 py-5">
                        <div class="space-y-6">

                            <!-- 帖子正文 -->
                            <div>
                                <h2 class="text-xl font-semibold leading-8 text-highlighted">
                                    {{ currentPost.title }}
                                </h2>

                                <p class="mt-3 whitespace-pre-wrap wrap-break-word text-sm leading-7 text-default">
                                    {{ currentPost.content }}
                                </p>
                            </div>


                            <!-- Tags -->
                            <div v-if="tags.length > 0" class="flex flex-wrap gap-2">
                                <span v-for="tag in tags" :key="tag"
                                    class="rounded-full bg-elevated px-3 py-1 text-xs text-muted">
                                    #{{ tag }}
                                </span>
                            </div>


                            <!-- 缩略图 -->
                            <div v-if="currentPost.media.length > 1" class="grid grid-cols-5 gap-2">
                                <button v-for="(media, index) in currentPost.media" :key="media.id" type="button"
                                    class="relative aspect-square overflow-hidden rounded-lg ring-offset-2 transition"
                                    :class="activeMediaIndex === index
                                        ? 'ring-2 ring-primary'
                                        : 'opacity-70 hover:opacity-100'
                                        " @click="selectMedia(index)">
                                    <NuxtImg :src="media.url" :alt="`${currentPost.title} ${index + 1}`" densities="1"
                                        class="size-full object-cover" loading="lazy" decoding="async" />
                                </button>
                            </div>


                            <div class="border-t border-default" />


                            <!-- ================================================= -->
                            <!-- 评论标题 -->
                            <!-- ================================================= -->

                            <div class="flex items-center justify-between">
                                <h3 class="text-base font-semibold">
                                    评论
                                </h3>

                                <span class="text-xs text-muted" role="status" aria-label="评论数量">
                                    {{ localCommentCount }}
                                </span>
                            </div>


                            <!-- ================================================= -->
                            <!-- 评论 Loading -->
                            <!-- ================================================= -->

                            <div v-if="commentsPending && comments.length === 0" class="space-y-5">
                                <div v-for="index in 4" :key="index" class="flex gap-3">
                                    <USkeleton class="size-9 shrink-0 rounded-full" />

                                    <div class="min-w-0 flex-1 space-y-2">
                                        <USkeleton class="h-3 w-24" />
                                        <USkeleton class="h-4 w-4/5" />
                                        <USkeleton class="h-3 w-20" />
                                    </div>
                                </div>
                            </div>


                            <!-- ================================================= -->
                            <!-- 评论加载错误 -->
                            <!-- ================================================= -->

                            <div v-else-if="commentsError"
                                class="flex flex-col items-center gap-3 rounded-xl border border-default py-8 text-center">
                                <UIcon name="i-lucide-message-circle-warning" class="size-6 text-muted" />

                                <p class="text-sm text-muted">
                                    {{ commentsError }}
                                </p>

                                <UButton size="sm" color="neutral" variant="soft" @click="loadComments()">
                                    重新加载
                                </UButton>
                            </div>


                            <!-- ================================================= -->
                            <!-- Empty -->
                            <!-- ================================================= -->

                            <div v-else-if="comments.length === 0"
                                class="flex flex-col items-center gap-2 py-10 text-center">
                                <UIcon name="i-lucide-message-circle" class="size-8 text-muted" />

                                <p class="text-sm font-medium">
                                    还没有评论
                                </p>

                                <p class="text-xs text-muted">
                                    来发表第一条评论吧
                                </p>
                            </div>


                            <!-- ================================================= -->
                            <!-- 评论列表 -->
                            <!-- ================================================= -->

                            <div v-else class="space-y-6">

                                <article v-for="comment in comments" :key="comment.id" class="flex gap-3">

                                    <!-- 评论作者头像 -->
                                    <UAvatar densities="1" :src="getCommentAuthorAvatarUrl(comment)"
                                        :alt="getCommentAuthorName(comment)" :text="getCommentAuthorFallback(comment)"
                                        size="sm" class="shrink-0" />

                                    <div class="min-w-0 flex-1">

                                        <!-- 评论作者 -->
                                        <div class="flex items-center justify-between gap-3">
                                            <span class="truncate text-xs font-medium text-muted">
                                                {{ getCommentAuthorName(comment) }}
                                            </span>

                                            <time class="shrink-0 text-xs text-muted">
                                                {{ formatDate(comment.createdAtUtc) }}
                                            </time>
                                        </div>


                                        <!-- 评论正文 -->
                                        <p class="mt-1 whitespace-pre-wrap wrap-break-word text-sm leading-6">
                                            {{ comment.content }}
                                        </p>


                                        <!-- 回复按钮 -->
                                        <button type="button"
                                            class="mt-2 flex items-center gap-1 text-xs text-muted transition hover:text-highlighted"
                                            :disabled="deletePending" @click="startReply(comment)">
                                            <UIcon name="i-lucide-reply" class="size-3.5" />

                                            回复
                                        </button>
                                        <UButton v-if="ownsComment(comment)" size="xs" color="error" variant="ghost"
                                            icon="i-lucide-trash-2" aria-label="删除评论"
                                            :disabled="deletePending || commentSubmitting"
                                            @click="deleteTarget = { kind: 'comment', comment }">
                                            删除
                                        </UButton>


                                        <!-- ===================================== -->
                                        <!-- 回复列表 -->
                                        <!-- ===================================== -->

                                        <div v-if="comment.replies.length > 0"
                                            class="mt-3 space-y-3 rounded-xl bg-elevated/60 p-3">

                                            <div v-for="reply in comment.replies" :key="reply.id" class="flex gap-2.5">

                                                <UAvatar densities="1" :src="getCommentAuthorAvatarUrl(reply)"
                                                    :alt="getCommentAuthorName(reply)"
                                                    :text="getCommentAuthorFallback(reply)" size="2xs"
                                                    class="shrink-0" />

                                                <div class="min-w-0 flex-1">
                                                    <div class="flex items-center gap-2">
                                                        <span class="truncate text-xs font-medium">
                                                            {{ getCommentAuthorName(reply) }}
                                                        </span>

                                                        <time class="shrink-0 text-[11px] text-muted">
                                                            {{ formatDate(reply.createdAtUtc) }}
                                                        </time>
                                                    </div>

                                                    <p
                                                        class="mt-1 whitespace-pre-wrap wrap-break-word text-xs leading-5">
                                                        {{ reply.content }}
                                                    </p>
                                                    <UButton v-if="ownsComment(reply)" size="xs" color="error" variant="ghost"
                                                        icon="i-lucide-trash-2" aria-label="删除回复"
                                                        :disabled="deletePending || commentSubmitting"
                                                        @click="deleteTarget = { kind: 'comment', comment: reply }">
                                                        删除
                                                    </UButton>
                                                </div>
                                            </div>
                                        </div>
                                    </div>
                                </article>
                            </div>
                            <UButton v-if="hasMoreComments" :loading="commentsPending"
                                color="neutral" variant="soft" @click="loadMoreComments()">
                                加载更多评论
                            </UButton>
                        </div>
                    </div>


                    <!-- ===================================================== -->
                    <!-- 固定评论输入区 -->
                    <!-- ===================================================== -->

                    <div class="shrink-0 border-t border-default bg-default px-6 py-4">

                        <!-- 回复目标 -->
                        <div v-if="replyingTo"
                            class="mb-3 flex items-center justify-between gap-3 rounded-xl bg-elevated/70 px-3 py-2">

                            <div class="flex min-w-0 items-center gap-2 text-xs">
                                <UIcon name="i-lucide-reply" class="size-4 shrink-0 text-muted" />

                                <span class="shrink-0 text-muted">
                                    回复
                                </span>

                                <span class="truncate font-medium">
                                    {{ getCommentAuthorName(replyingTo) }}
                                </span>
                            </div>

                            <button type="button" class="shrink-0 text-muted transition hover:text-highlighted"
                                aria-label="取消回复" @click="cancelReply">
                                <UIcon name="i-lucide-x" class="size-4" />
                            </button>
                        </div>


                        <!-- 输入框 -->
                        <div class="rounded-2xl border border-default bg-elevated/40 px-4 py-3">
                            <textarea v-model="commentContent" rows="2" maxlength="1000"
                                :placeholder="commentPlaceholder"
                                class="block w-full resize-none bg-transparent text-sm leading-6 outline-none placeholder:text-muted" />

                            <div class="mt-2 flex items-center justify-between gap-3">
                                <span class="text-xs text-muted">
                                    {{ commentContent.length }} / 1000
                                </span>

                                <UButton size="sm" :icon="replyingTo
                                    ? 'i-lucide-reply'
                                    : 'i-lucide-send'
                                    " :loading="commentSubmitting" :disabled="!canSubmitComment"
                                    @click="submitComment">
                                    {{ replyingTo ? '回复' : '发布' }}
                                </UButton>
                            </div>
                        </div>
                    </div>


                    <!-- ===================================================== -->
                    <!-- 底部互动区 -->
                    <!-- ===================================================== -->

                    <footer class="shrink-0 flex items-center justify-between border-t border-default px-6 py-4">

                        <div class="flex items-center gap-5 text-sm text-muted">

                            <!-- 点赞 -->
                            <button type="button"
                                class="flex items-center gap-1.5 transition-colors hover:text-error disabled:cursor-default disabled:opacity-60"
                                :class="{
                                    'text-error':
                                        currentPost.isLiked,
                                }" :disabled="likePending" :aria-pressed="currentPost.isLiked" :aria-label="currentPost.isLiked
                                    ? '取消点赞'
                                    : '点赞'
                                    " :title="currentPost.isLiked
                                        ? '取消点赞'
                                        : '点赞'
                                        " @click="toggleLike">

                                <UIcon name="i-lucide-heart" class="size-5 transition-transform" :class="{
                                    'fill-current':
                                        currentPost.isLiked,

                                    'scale-110':
                                        currentPost.isLiked,
                                }" />

                                {{ formatCount(currentPost.likeCount) }}
                            </button>


                            <!-- 评论 -->
                            <span class="flex items-center gap-1.5">
                                <UIcon name="i-lucide-message-circle" class="size-5" />

                                {{ formatCount(currentPost.commentCount) }}
                            </span>


                            <!-- 收藏 -->
                            <button type="button"
                                class="flex items-center gap-1.5 transition-colors hover:text-primary disabled:cursor-default disabled:opacity-60"
                                :class="{
                                    'text-primary':
                                        currentPost.isFavorited,
                                }" :aria-pressed="currentPost.isFavorited" :aria-label="currentPost.isFavorited
                                    ? '取消收藏'
                                    : '收藏'
                                    " :title="currentPost.isFavorited
                                        ? '取消收藏'
                                        : '收藏'
                                        ">

                                <UIcon name="i-lucide-bookmark" class="size-5 transition-transform" :class="{
                                    'fill-current':
                                        currentPost.isFavorited,

                                    'scale-110':
                                        currentPost.isFavorited,
                                }" />
                            </button>
                        </div>


                        <span class="text-xs text-muted">
                            {{ formatDate(currentPost.updatedAtUtc) }}
                        </span>
                    </footer>
                </section>
            </div>
        </template>
    </UModal>
    <UModal v-model:open="confirmOpen" title="确认删除" :description="deleteDescription"
        :dismissible="!deletePending" :close="!deletePending">
        <template #footer>
            <div class="flex w-full justify-end gap-3">
                <UButton color="neutral" variant="outline" :disabled="deletePending" @click="confirmOpen = false">
                    取消
                </UButton>
                <UButton color="error" :loading="deletePending" @click="confirmDelete">确认删除</UButton>
            </div>
        </template>
    </UModal>
</template>
