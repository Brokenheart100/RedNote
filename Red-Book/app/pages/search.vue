<script setup lang="ts">
import type { PostResponse } from '~~/shared/types/posts'
import type { SearchPostsResponse } from '~~/shared/types/search'

const route = useRoute()
const postStore = usePostStore()

const pageSize = 20

const keyword = computed(() => {
    const value = route.query.q
    return typeof value === 'string' ? value.trim() : ''
})

const page = computed(() => {
    const value = route.query.page

    if (typeof value !== 'string') {
        return 1
    }

    const parsed = Number.parseInt(value, 10)

    return Number.isInteger(parsed) && parsed > 0 ? parsed : 1
})

const {
    data,
    pending,
    error,
    refresh,
} = await useAsyncData(
    'post-search',
    async (): Promise<SearchPostsResponse> => {
        if (!keyword.value) {
            return {
                page: 1,
                pageSize,
                totalCount: 0,
                items: [],
            }
        }

        return await $fetch<SearchPostsResponse>('/api/posts/search', {
            query: {
                q: keyword.value,
                page: page.value,
                pageSize,
            },
        })
    },
    {
        watch: [keyword, page],
    },
)

watch(
    () => data.value?.items,
    items => {
        if (!items) {
            return
        }

        postStore.upsertPosts(items)
    },
    {
        immediate: true,
    },
)

const posts = computed<PostResponse[]>(() => {
    const items = data.value?.items ?? []

    return items.map(post => postStore.getPost(post.id) ?? post)
})

const totalCount = computed(() => data.value?.totalCount ?? 0)

const totalPages = computed(() => Math.max(
    1,
    Math.ceil(totalCount.value / pageSize),
))

async function refreshSearch(): Promise<void> {
    await refresh()
}

async function goToPage(targetPage: number): Promise<void> {
    if (targetPage < 1 || targetPage > totalPages.value || targetPage === page.value) {
        return
    }

    await navigateTo({
        path: '/search',
        query: {
            q: keyword.value,
            page: targetPage > 1 ? targetPage.toString() : undefined,
        },
    })

    if (import.meta.client) {
        window.scrollTo({
            top: 0,
            behavior: 'smooth',
        })
    }
}

useSeoMeta({
    title: () => keyword.value ? `搜索：${keyword.value}` : '搜索',
    description: () => keyword.value
        ? `搜索与“${keyword.value}”相关的 RedNote 内容。`
        : '搜索 RedNote 内容。',
})
</script>
<template>
    <div class="mx-auto w-full max-w-6xl space-y-6">
        <!-- 没有关键词 -->
        <div v-if="!keyword" class="flex min-h-[50vh] flex-col items-center justify-center text-center">
            <div class="flex size-16 items-center justify-center rounded-2xl bg-elevated">
                <UIcon name="i-lucide-search" class="size-7 text-muted" />
            </div>

            <h1 class="mt-5 text-xl font-semibold">
                搜索 RedNote
            </h1>

            <p class="mt-2 max-w-md text-sm text-muted">
                在顶部搜索框输入关键词，发现你感兴趣的帖子。
            </p>
        </div>

        <template v-else>
            <section class="flex flex-wrap items-end justify-between gap-4">
                <div class="min-w-0">
                    <p class="text-sm text-muted">
                        搜索结果
                    </p>

                    <h1 class="mt-1 truncate text-2xl font-semibold tracking-tight">
                        “{{ keyword }}”
                    </h1>
                </div>

                <div v-if="!pending && !error" class="text-sm text-muted">
                    共 {{ totalCount }} 个结果
                </div>
            </section>

            <PostFeedGrid :posts="posts" :pending="pending" :error="error" :page="page" :total-pages="totalPages"
                empty-icon="i-lucide-file-search" empty-title="没有找到相关内容"
                :empty-description="`没有找到与“${keyword}”相关的帖子，可以尝试更换关键词。`" @refresh="refreshSearch"
                @page-change="goToPage" />
        </template>
    </div>
</template>