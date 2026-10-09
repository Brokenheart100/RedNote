<script setup lang="ts">
import { useDebounceFn } from '@vueuse/core'
import { parseSearch } from '~~/shared/schemas/requests'
import { getRequestValidationMessage } from '~/utils/request-validation'
const {
    loggedIn,
} = useUserSession()

const {
    user,
    fetchCurrentUser,
    refreshCurrentUser,
    clearCurrentUser,
} = useCurrentUser()

const postStore = usePostStore()

const route = useRoute()

const searchQuery = ref(
    typeof route.query.q === 'string'
        ? route.query.q
        : '',
)
const { logout, logoutPending } = useAuth()
const searchError = ref<string | null>(null)

const displayName = computed(() => {
    return user.value?.nickname?.trim() || 'RedNote 用户'
})

const avatarUrl = computed(
    () => user.value?.avatarUrl ?? undefined,
)

const avatarFallback = computed(() => {
    const name = displayName.value.trim()

    return name
        ? name.charAt(0).toUpperCase()
        : 'R'
})

async function navigateSearch(replace = false): Promise<void> {
    const value = searchQuery.value.trim()
    searchError.value = null
    if (!value) {
        return
    }
    if (route.path === '/search' && route.query.q === value) return
    try { parseSearch({ q: value }) }
    catch (error) {
        searchError.value = getRequestValidationMessage(error) ?? '搜索关键词无效。'
        return
    }
    await navigateTo({
        path: '/search',
        query: {
            q: value,
        },
    }, { replace })
}

const debouncedSearch = useDebounceFn(() => {
    if (route.path === '/search') return navigateSearch(true)
}, 350)

async function submitSearch(): Promise<void> {
    debouncedSearch.cancel()
    await navigateSearch()
}

watch(searchQuery, value => {
    searchError.value = null
    debouncedSearch.cancel()
    if (route.path === '/search' && value.trim() && value.trim() !== route.query.q) {
        void debouncedSearch()
    }
})
// Cancel before an asynchronous navigation can be overtaken by a pending search.
const removeSearchGuard = useRouter().beforeEach(() => {
    debouncedSearch.cancel()
})
onScopeDispose(removeSearchGuard)
onScopeDispose(() => debouncedSearch.cancel())

const userMenuItems = computed(() => [
    [
        {
            label: '个人主页',
            icon: 'i-lucide-user',
            to: '/me',
        },
        {
            label: '设置',
            icon: 'i-lucide-settings',
            to: '/settings',
        },
    ],
    [
        {
            label: logoutPending.value ? '正在退出...' : '退出登录',
            icon: 'i-lucide-log-out',
            disabled: logoutPending.value,
            onSelect: logout,
        },
    ],
])

/*
 * SSR 首次渲染必须等待当前用户加载完成。
 */
if (loggedIn.value) {
    await fetchCurrentUser()
}
else {
    clearCurrentUser()
}

/*
 * 这里只处理登录状态后续变化。
 * 不再 immediate，避免首次 SSR 与客户端 hydration 状态不同。
 */
watch(
    loggedIn,
    async value => {
        if (!value) {
            postStore.clear()
            clearCurrentUser()
            return
        }

        await refreshCurrentUser()
    },
)

watch(
    () => route.query.q,
    value => {
        const nextValue = typeof value === 'string'
            ? value
            : ''

        if (searchQuery.value !== nextValue) {
            searchQuery.value = nextValue
        }
    },
)
</script>

<template>
    <header class="
            sticky top-0 z-50
            h-16 border-b border-default
            bg-default/95 backdrop-blur
        ">
        <div class="
                mx-auto flex h-full
                max-w-[1600px]
                items-center gap-4
                px-4 sm:px-6
            ">
            <NuxtLink to="/" class="
                    flex shrink-0 items-center
                    gap-2 text-lg font-semibold
                ">
                <UIcon name="i-lucide-book-heart" class="size-6 text-primary" />

                <span class="hidden sm:inline">
                    RedNote
                </span>
            </NuxtLink>

            <form class="
                    relative mx-auto flex w-full
                    max-w-xl
                " @submit.prevent="submitSearch">
                <UInput v-model="searchQuery" icon="i-lucide-search" placeholder="搜索 RedNote" size="lg"
                    aria-label="搜索 RedNote" :aria-invalid="!!searchError"
                    :aria-describedby="searchError ? 'search-error' : undefined" class="w-full" />
                <span v-if="searchError" id="search-error" role="alert"
                    class="absolute top-full mt-1 rounded bg-default px-2 text-sm text-error">{{ searchError }}</span>
            </form>

            <div class="flex shrink-0 items-center gap-2">
                <template v-if="!loggedIn">
                    <UButton to="/register" color="neutral" variant="ghost">
                        注册
                    </UButton>

                    <UButton to="/login" icon="i-lucide-log-in">
                        登录
                    </UButton>
                </template>

                <UDropdownMenu v-else :items="userMenuItems">
                    <UButton color="neutral" variant="ghost" class="
                            flex max-w-48
                            items-center gap-2
                        ">
                        <UAvatar densities="1" :src="avatarUrl" :alt="displayName" :text="avatarFallback" size="sm" />

                        <span class="
                                hidden max-w-28
                                truncate md:inline
                            ">
                            {{ displayName }}
                        </span>

                        <UIcon name="i-lucide-chevron-down" class="size-4 shrink-0" />
                    </UButton>
                </UDropdownMenu>
            </div>
        </div>
    </header>
</template>
