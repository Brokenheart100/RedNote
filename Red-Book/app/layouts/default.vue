<script setup lang="ts">
const navigationItems = [
    {
        label: '首页',
        icon: 'i-lucide-house',
        to: '/',
    },
    {
        label: '个人主页',
        icon: 'i-lucide-user',
        to: '/me',
    },
]
</script>

<template>
    <div class="min-h-screen bg-default">
        <AppHeader />

        <div class="mx-auto flex w-full max-w-[1600px]">
            <aside class="
                    sticky top-16 hidden
                    h-[calc(100vh-4rem)]
                    w-64 shrink-0
                    border-r border-default
                    px-4 py-6
                    lg:block
                ">
                <nav class="space-y-1">
                    <UButton v-for="item in navigationItems" :key="item.to" :to="item.to" :icon="item.icon"
                        :label="item.label" variant="ghost" color="neutral" block class="justify-start" />
                </nav>

                <div class="mt-8">
                    <UButton to="/publish" icon="i-lucide-square-pen" label="发布笔记" block size="lg" />
                </div>
            </aside>

            <main class="
                    min-w-0 flex-1
                    px-4 py-6
                    sm:px-6
                    lg:px-8
                ">
                <slot />
            </main>
        </div>
    </div>
</template>