<script setup lang="ts">
import type { PostResponse } from '~~/shared/types/posts'

const props = withDefaults(
    defineProps<{
        posts: readonly PostResponse[]
        pending?: boolean
        error?: unknown
        page?: number
        totalPages?: number
        skeletonCount?: number
        emptyTitle?: string
        emptyDescription?: string
        emptyIcon?: string
    }>(),
    {
        pending: false,
        page: 1,
        totalPages: 1,
        skeletonCount: 8,
        emptyTitle: '暂无帖子',
        emptyDescription: '当前没有可以显示的帖子。',
        emptyIcon: 'i-lucide-file-text',
    },
)

const emit = defineEmits<{
    refresh: []
    pageChange: [page: number]
}>()

const showInitialLoading = computed(() => props.pending && props.posts.length === 0)
const hasPreviousPage = computed(() => props.page > 1)
const hasNextPage = computed(() => props.page < props.totalPages)

function changePage(targetPage: number): void {
    if (props.pending || targetPage < 1 || targetPage > props.totalPages || targetPage === props.page) {
        return
    }

    emit('pageChange', targetPage)
}
</script>

<template>
    <div class="space-y-6">
        <!-- Loading -->
        <div v-if="showInitialLoading" class="grid gap-5 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
            <div v-for="index in skeletonCount" :key="index" class="space-y-3">
                <USkeleton class="aspect-3/4 w-full rounded-xl" />
                <USkeleton class="h-4 w-4/5" />

                <div class="flex items-center justify-between">
                    <div class="flex items-center gap-2">
                        <USkeleton class="size-6 rounded-full" />
                        <USkeleton class="h-3 w-20" />
                    </div>

                    <USkeleton class="h-3 w-10" />
                </div>
            </div>
        </div>

        <!-- Error -->
        <UAlert v-else-if="error" color="error" variant="soft" icon="i-lucide-circle-alert" title="内容加载失败"
            description="暂时无法加载帖子，请稍后重试。">
            <template #actions>
                <UButton color="error" variant="soft" size="sm" icon="i-lucide-refresh-cw" @click="emit('refresh')">
                    重新加载
                </UButton>
            </template>
        </UAlert>

        <!-- Empty -->
        <div v-else-if="posts.length === 0" class="flex min-h-72 flex-col items-center justify-center text-center">
            <div class="flex size-16 items-center justify-center rounded-2xl bg-elevated">
                <UIcon :name="emptyIcon" class="size-7 text-muted" />
            </div>

            <h2 class="mt-5 text-lg font-semibold">
                {{ emptyTitle }}
            </h2>

            <p class="mt-2 max-w-md text-sm leading-6 text-muted">
                {{ emptyDescription }}
            </p>
        </div>

        <!-- Posts -->
        <template v-else>
            <div class="grid gap-5 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
                <PostFeedCard v-for="post in posts" :key="post.id" :post="post" />
            </div>

            <!-- Pagination -->
            <div v-if="totalPages > 1" class="flex items-center justify-center gap-3 border-t border-default pt-6">
                <UButton color="neutral" variant="outline" icon="i-lucide-chevron-left"
                    :disabled="!hasPreviousPage || pending" @click="changePage(page - 1)">
                    上一页
                </UButton>

                <span class="min-w-24 text-center text-sm text-muted">
                    {{ page }} / {{ totalPages }}
                </span>

                <UButton color="neutral" variant="outline" trailing-icon="i-lucide-chevron-right"
                    :disabled="!hasNextPage || pending" @click="changePage(page + 1)">
                    下一页
                </UButton>
            </div>
        </template>
    </div>
</template>