<script setup lang="ts">
import type { FeedResponse, RecommendationFeedResponse, PostResponse } from '~~/shared/types/posts'

definePageMeta({ middleware: 'auth' })
useSeoMeta({ title: '首页', description: '发现你感兴趣的内容' })

type FeedTab = 'recommend' | 'following'
type HomeFeed = RecommendationFeedResponse & { mode: 'recommendation' | 'latest', page: number }
const postStore = usePostStore()
const activeTab = ref<FeedTab>('recommend')
const tabs = [{ label: '推荐', value: 'recommend' }, { label: '关注', value: 'following' }] satisfies Array<{ label: string, value: FeedTab }>
const pageSize = 20
const requestFetch = useRequestFetch()
function unavailable(error: unknown): boolean {
  const status = (error as { statusCode?: number, status?: number }).statusCode
    ?? (error as { status?: number }).status
  return status === undefined || [502, 503, 504].includes(status)
}
async function latest(page = 1): Promise<HomeFeed> {
  const result = await requestFetch<FeedResponse>('/api/posts/feed', { query: { page, pageSize } })
  return { items: result.items, hasMore: page * pageSize < result.totalCount,
    nextCursor: null, requestId: '', strategy: 'latest', mode: 'latest', page }
}
async function initial(): Promise<HomeFeed> {
  try {
    const result = await requestFetch<RecommendationFeedResponse>('/api/posts/recommended', { query: { pageSize } })
    return { ...result, mode: 'recommendation', page: 0 }
  }
  catch (error) {
    if (unavailable(error)) return latest()
    throw error
  }
}
const { data, status, error, refresh } = await useAsyncData('home-recommended-feed', initial)
const accumulated = ref<PostResponse[]>([])
const nextCursor = ref<string | null>(null)
const requestId = ref('')
const mode = ref<HomeFeed['mode']>('recommendation')
const latestPage = ref(0)
const hasMore = ref(false)
const loadingMore = ref(false)
const loadMoreError = ref(false)
const { record } = useRecommendationTracking()
function replace(value: HomeFeed): void {
  postStore.upsertPosts(value.items)
  accumulated.value = value.items
  nextCursor.value = value.nextCursor
  requestId.value = value.requestId
  mode.value = value.mode
  latestPage.value = value.page
  hasMore.value = value.hasMore
}
watch(data, value => { if (value) replace(value) }, { immediate: true })
const posts = computed(() => accumulated.value.filter(post => !postStore.isDeleted(post.id))
  .map(post => postStore.getPost(post.id) ?? post))
async function loadMore(): Promise<void> {
  if (!hasMore.value || loadingMore.value) return
  loadingMore.value = true
  loadMoreError.value = false
  try {
    const result = mode.value === 'latest' ? await latest(latestPage.value + 1)
      : await requestFetch<RecommendationFeedResponse>('/api/posts/recommended', {
        query: { pageSize, cursor: nextCursor.value },
      })
    postStore.upsertPosts(result.items)
    const known = new Set(accumulated.value.map(post => post.id))
    accumulated.value.push(...result.items.filter(post => !known.has(post.id)))
    nextCursor.value = result.nextCursor
    hasMore.value = result.hasMore
    if ('page' in result && typeof result.page === 'number') latestPage.value = result.page
  }
  catch (error) {
    if ((error as { statusCode?: number }).statusCode === 410) await refresh()
    else if (mode.value === 'recommendation' && unavailable(error)) {
      try { replace(await latest()) }
      catch { loadMoreError.value = true }
    }
    else loadMoreError.value = true
  }
  finally { loadingMore.value = false }
}
const pending = computed(() => status.value === 'pending')
const hasError = computed(() => Boolean(error.value))
const placeholderPosts = Array.from({ length: 8 }, (_, index) => ({ id: index + 1 }))
function selectTab(tab: FeedTab): void { activeTab.value = tab }
async function retry(): Promise<void> { await refresh() }
</script>

<template>
  <div class="
    mx-auto w-full
    max-w-6xl
    space-y-6
  ">
    <div class="
      flex items-center
      justify-between
    ">
      <div>
        <h1 class="
          text-2xl font-semibold
          tracking-tight
        ">
          首页
        </h1>

        <p class="
          mt-1 text-sm
          text-muted
        ">
          发现你感兴趣的内容
        </p>
      </div>
    </div>

    <div class="
      flex gap-2
      border-b border-default
      pb-3
    ">
      <UButton v-for="tab in tabs" :key="tab.value" :variant="activeTab === tab.value
          ? 'soft'
          : 'ghost'
        " color="neutral" @click="selectTab(tab.value)">
        {{ tab.label }}
      </UButton>
    </div>

    <!-- 推荐 Feed -->
    <template v-if="
      activeTab
      === 'recommend'
    ">

      <!-- Loading -->
      <div v-if="pending" class="
          grid gap-5
          sm:grid-cols-2
          lg:grid-cols-3
          xl:grid-cols-4
        ">
        <UCard v-for="post in placeholderPosts" :key="post.id" class="overflow-hidden">
          <div class="space-y-4">
            <USkeleton class="
              aspect-3/4
              w-full rounded-lg
            " />

            <div class="space-y-2">
              <USkeleton class="
                h-4 w-5/6
              " />

              <USkeleton class="
                h-4 w-3/5
              " />
            </div>

            <div class="
              flex items-center
              justify-between
            ">
              <div class="
                flex items-center
                gap-2
              ">
                <USkeleton class="
                  size-7 rounded-full
                " />

                <USkeleton class="
                  h-3 w-20
                " />
              </div>

              <USkeleton class="
                h-3 w-10
              " />
            </div>
          </div>
        </UCard>
      </div>

      <!-- Error -->
      <div v-else-if="hasError" class="
          flex min-h-80
          flex-col items-center
          justify-center gap-4
          text-center
        ">
        <div class="
          flex size-12
          items-center justify-center
          rounded-full
          bg-error/10
        ">
          <UIcon name="i-lucide-circle-alert" class="
              size-6
              text-error
            " />
        </div>

        <div>
          <p class="font-medium">
            暂时无法加载内容
          </p>

          <p class="
            mt-1 text-sm
            text-muted
          ">
            请稍后重试
          </p>
        </div>

        <UButton icon="i-lucide-refresh-cw" color="neutral" variant="soft" @click="retry">
          重新加载
        </UButton>
      </div>

      <!-- Empty -->
      <div v-else-if="
        posts.length === 0
      " class="
          flex min-h-80
          flex-col items-center
          justify-center gap-3
          text-center
        ">
        <div class="
          flex size-12
          items-center justify-center
          rounded-full
          bg-muted
        ">
          <UIcon name="i-lucide-images" class="
              size-6
              text-muted
            " />
        </div>

        <div>
          <p class="font-medium">
            暂时还没有内容
          </p>

          <p class="
            mt-1 text-sm
            text-muted
          ">
            发布第一篇笔记吧
          </p>
        </div>

        <UButton to="/publish" icon="i-lucide-square-pen">
          发布笔记
        </UButton>
      </div>

      <!-- Feed -->
      <template v-else>
        <div class="
          grid gap-5
          sm:grid-cols-2
          lg:grid-cols-3
          xl:grid-cols-4
        ">
          <PostFeedCard v-for="post in posts" :key="`${requestId}:${post.id}`" :post="post"
            :recommendation-request-id="requestId" @feedback="record" />
        </div>

        <div v-if="hasMore" class="flex justify-center">
          <UButton variant="soft" color="neutral" :loading="loadingMore" @click="loadMore">
            {{ loadMoreError ? '加载失败，点击重试' : '加载更多' }}
          </UButton>
        </div>
        <p class="
          text-center text-xs
          text-muted
        ">
          已加载 {{ posts.length }} 篇内容
        </p>
      </template>
    </template>

    <!-- 关注 Feed 暂未实现 -->
    <div v-else class="
        flex min-h-80
        flex-col items-center
        justify-center gap-3
        text-center
      ">
      <div class="
        flex size-12
        items-center justify-center
        rounded-full
        bg-muted
      ">
        <UIcon name="i-lucide-users" class="
            size-6
            text-muted
          " />
      </div>

      <div>
        <p class="font-medium">
          关注内容正在完善
        </p>

        <p class="
          mt-1 text-sm
          text-muted
        ">
          当前先使用推荐内容
        </p>
      </div>
    </div>
  </div>
</template>
