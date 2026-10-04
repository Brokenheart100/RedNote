<script setup lang="ts">
const {
    loggedIn,
    fetch: fetchSession,
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
const logoutPending = ref(false)

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

async function submitSearch(): Promise<void> {
    const value = searchQuery.value.trim()

    if (!value) {
        return
    }

    await navigateTo({
        path: '/search',
        query: {
            q: value,
        },
    })
}

async function logout(): Promise<void> {
    if (logoutPending.value) {
        return
    }

    logoutPending.value = true

    try {
        await $fetch('/api/auth/logout', {
            method: 'POST',
        })

        postStore.clear()
        clearCurrentUser()
        await fetchSession()
        await navigateTo('/login')
    }
    catch (error: unknown) {
        console.error('❌ [HEADER] 退出登录失败', {
            error,
        })
    }
    finally {
        logoutPending.value = false
    }
}

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
                    mx-auto flex w-full
                    max-w-xl
                " @submit.prevent="submitSearch">
                <UInput v-model="searchQuery" icon="i-lucide-search" placeholder="搜索 RedNote" size="lg"
                    class="w-full" />
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
                        <UAvatar :src="avatarUrl" :alt="displayName" :text="avatarFallback" size="sm" />

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