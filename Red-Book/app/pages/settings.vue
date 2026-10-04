<script setup lang="ts">
definePageMeta({
    middleware: 'auth',
})

useSeoMeta({
    title: '设置',
    description: '管理 RedNote 的显示与使用偏好。',
    robots: 'noindex, nofollow',
})

type ThemePreference = 'light' | 'dark'

interface ThemeOption {
    value: ThemePreference
    label: string
    description: string
    icon: string
}

const colorMode = useColorMode()

const themeOptions: ThemeOption[] = [
    {
        value: 'light',
        label: '浅色',
        description: '使用明亮的浅色界面。',
        icon: 'i-lucide-sun',
    },
    {
        value: 'dark',
        label: '深色',
        description: '使用更适合暗光环境的深色界面。',
        icon: 'i-lucide-moon',
    },
]

const selectedTheme = computed<ThemePreference>(() => {
    return colorMode.preference === 'dark' ? 'dark' : 'light'
})

function setTheme(theme: ThemePreference): void {
    colorMode.preference = theme
}
</script>

<template>
    <div class="mx-auto w-full max-w-4xl space-y-6">
        <div>
            <h1 class="text-2xl font-semibold tracking-tight">
                设置
            </h1>

            <p class="mt-1 text-sm text-muted">
                管理你的 RedNote 使用偏好。
            </p>
        </div>

        <div class="grid gap-6 lg:grid-cols-[220px_minmax(0,1fr)]">
            <!-- 左侧设置导航 -->
            <aside>
                <UCard>
                    <nav class="space-y-1">
                        <UButton block color="neutral" variant="soft" icon="i-lucide-palette" class="justify-start">
                            外观
                        </UButton>

                        <UButton block color="neutral" variant="ghost" icon="i-lucide-user-round" class="justify-start"
                            disabled>
                            账号
                        </UButton>

                        <UButton block color="neutral" variant="ghost" icon="i-lucide-bell" class="justify-start"
                            disabled>
                            通知
                        </UButton>

                        <UButton block color="neutral" variant="ghost" icon="i-lucide-shield" class="justify-start"
                            disabled>
                            隐私与安全
                        </UButton>
                    </nav>
                </UCard>
            </aside>

            <!-- 右侧设置内容 -->
            <main class="space-y-6">
                <UCard>
                    <template #header>
                        <div class="flex items-center gap-3">
                            <div class="flex size-10 items-center justify-center rounded-xl bg-elevated">
                                <UIcon name="i-lucide-palette" class="size-5" />
                            </div>

                            <div>
                                <h2 class="font-semibold">
                                    外观
                                </h2>

                                <p class="text-sm text-muted">
                                    设置应用的显示主题。
                                </p>
                            </div>
                        </div>
                    </template>

                    <div class="space-y-4">
                        <div>
                            <h3 class="text-sm font-medium">
                                主题模式
                            </h3>

                            <p class="mt-1 text-sm text-muted">
                                选择你偏好的界面颜色。
                            </p>
                        </div>

                        <div class="grid gap-4 md:grid-cols-2">
                            <button v-for="option in themeOptions" :key="option.value" type="button"
                                class="group relative overflow-hidden rounded-2xl border p-4 text-left transition"
                                :class="selectedTheme === option.value
                                        ? 'border-primary ring-2 ring-primary/20'
                                        : 'border-default hover:border-accented'
                                    " :aria-pressed="selectedTheme === option.value" @click="setTheme(option.value)">
                                <!-- 主题预览 -->
                                <div class="mb-4 overflow-hidden rounded-xl border" :class="option.value === 'dark'
                                        ? 'border-white/10 bg-[#0f172a]'
                                        : 'border-gray-200 bg-white'
                                    ">
                                    <div class="flex h-28 flex-col" :class="option.value === 'dark'
                                            ? 'text-white'
                                            : 'text-gray-900'
                                        ">
                                        <div class="flex h-8 items-center gap-2 border-b px-3" :class="option.value === 'dark'
                                                ? 'border-white/10 bg-[#111827]'
                                                : 'border-gray-200 bg-gray-50'
                                            ">
                                            <div class="size-2 rounded-full bg-red-400" />
                                            <div class="size-2 rounded-full bg-yellow-400" />
                                            <div class="size-2 rounded-full bg-green-400" />
                                        </div>

                                        <div class="grid flex-1 grid-cols-[36px_1fr] gap-2 p-3">
                                            <div class="rounded-md" :class="option.value === 'dark'
                                                    ? 'bg-white/10'
                                                    : 'bg-gray-100'
                                                " />

                                            <div class="space-y-2">
                                                <div class="h-2 w-3/4 rounded" :class="option.value === 'dark'
                                                        ? 'bg-white/20'
                                                        : 'bg-gray-200'
                                                    " />

                                                <div class="h-2 w-1/2 rounded" :class="option.value === 'dark'
                                                        ? 'bg-white/10'
                                                        : 'bg-gray-100'
                                                    " />

                                                <div class="mt-3 h-7 rounded-md" :class="option.value === 'dark'
                                                        ? 'bg-white/10'
                                                        : 'bg-gray-100'
                                                    " />
                                            </div>
                                        </div>
                                    </div>
                                </div>

                                <div class="flex items-start justify-between gap-3">
                                    <div class="flex min-w-0 gap-3">
                                        <div
                                            class="flex size-9 shrink-0 items-center justify-center rounded-xl bg-elevated">
                                            <UIcon :name="option.icon" class="size-4" />
                                        </div>

                                        <div class="min-w-0">
                                            <p class="font-medium">
                                                {{ option.label }}
                                            </p>

                                            <p class="mt-1 text-xs leading-5 text-muted">
                                                {{ option.description }}
                                            </p>
                                        </div>
                                    </div>

                                    <div class="flex size-5 shrink-0 items-center justify-center rounded-full border"
                                        :class="selectedTheme === option.value
                                                ? 'border-primary bg-primary text-inverted'
                                                : 'border-default'
                                            ">
                                        <UIcon v-if="selectedTheme === option.value" name="i-lucide-check"
                                            class="size-3" />
                                    </div>
                                </div>
                            </button>
                        </div>

                        <div class="flex items-center gap-2 rounded-xl bg-elevated/60 px-4 py-3 text-sm text-muted">
                            <UIcon name="i-lucide-info" class="size-4 shrink-0" />

                            <span>
                                当前正在使用
                                <strong class="font-medium text-highlighted">
                                    {{ colorMode.value === 'dark' ? '深色模式' : '浅色模式' }}
                                </strong>
                            </span>
                        </div>
                    </div>
                </UCard>
            </main>
        </div>
    </div>
</template>