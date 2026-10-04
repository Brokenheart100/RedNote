<script setup lang="ts">
import type {
  FeedResponse,
  PostResponse,
} from '~~/shared/types/posts'

definePageMeta({
  middleware: 'auth',
})

useSeoMeta({
  title: '首页',
  description: '发现你感兴趣的内容',
})

type FeedTab =
  | 'recommend'
  | 'following'

const postStore = usePostStore()

const activeTab =
  ref<FeedTab>('recommend')

const tabs = [
  {
    label: '推荐',
    value: 'recommend',
  },
  {
    label: '关注',
    value: 'following',
  },
] satisfies Array<{
  label: string
  value: FeedTab
}>

const page =
  ref(1)

const pageSize =
  20

const {
  data,
  status,
  error,
  refresh,
} = await useFetch<FeedResponse>(
  '/api/posts/feed',
  {
    query: {
      page,
      pageSize,
    },
    key: 'home-feed',
  },
)

/*
 * API 查询结果进入统一 Post Store。
 *
 * 首页只负责：
 * - Feed 顺序
 * - 分页
 * - loading / error
 *
 * Post 实体状态统一由 Pinia 管理。
 */
watch(
  () => data.value?.items,
  items => {
    if (!items) {
      return
    }

    postStore.upsertPosts(
      items,
    )
  },
  {
    immediate: true,
  },
)

/*
 * FeedResponse.items 决定当前 Feed 顺序。
 *
 * 实际实体优先使用 Store 中的最新版本，
 * 保证首页、搜索、喜欢页、详情弹窗之间状态一致。
 */
const posts =
  computed<PostResponse[]>(() => {
    const items =
      data.value?.items
      ?? []

    return items.map(
      post =>
        postStore.getPost(
          post.id,
        )
        ?? post,
    )
  })

const totalCount =
  computed(() =>
    data.value?.totalCount
    ?? 0,
  )

const pending =
  computed(() =>
    status.value === 'pending',
  )

const hasError =
  computed(() =>
    Boolean(
      error.value,
    ),
  )

const placeholderPosts =
  Array.from(
    {
      length: 8,
    },
    (_, index) => ({
      id:
        index + 1,
    }),
  )

function selectTab(
  tab: FeedTab,
): void {
  activeTab.value =
    tab
}

async function retry():
  Promise<void> {
  await refresh()
}
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
          <PostFeedCard v-for="post in posts" :key="post.id" :post="post" />
        </div>

        <p class="
          text-center text-xs
          text-muted
        ">
          共 {{ totalCount }} 篇内容
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