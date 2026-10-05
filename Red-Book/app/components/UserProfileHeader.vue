<script setup lang="ts">
import type { CurrentUser } from '~~/shared/types/users'

const props = withDefaults(
    defineProps<{
        user: CurrentUser
        pending?: boolean
    }>(),
    {
        pending: false,
    },
)

const emit = defineEmits<{
    edit: []
}>()

const displayName = computed(
    () => props.user.nickname?.trim() || 'RedNote 用户',
)

const avatarUrl = computed(
    () => props.user.avatarUrl ?? undefined,
)

const avatarFallback = computed(() => {
    const value = displayName.value.trim()

    return value.charAt(0).toUpperCase() || 'R'
})

const bio = computed(
    () => props.user.bio?.trim()
        || '这个用户还没有填写个人简介。',
)

function formatDate(value: string): string {
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
        },
    ).format(date)
}

function handleEdit(): void {
    if (import.meta.dev) {
        console.log('✏️ [USER PROFILE] 点击编辑资料', {
            userId: props.user.userId,
        })
    }

    emit('edit')
}
</script>

<template>
    <UCard>
        <div class="
                flex flex-col gap-6
                md:flex-row
                md:items-start
                md:justify-between
            ">
            <div class="flex min-w-0 items-start gap-4">
                <UAvatar densities="1" :src="avatarUrl" :alt="displayName" :text="avatarFallback" size="3xl" class="shrink-0" />

                <div class="min-w-0">
                    <div class="flex flex-wrap items-center gap-2">
                        <h1 class="truncate text-2xl font-semibold">
                            {{ displayName }}
                        </h1>

                        <UBadge variant="soft" color="neutral">
                            ID {{ user.userId.slice(0, 8) }}
                        </UBadge>
                    </div>

                    <p class="
                            mt-3 max-w-2xl
                            whitespace-pre-wrap
                            text-sm leading-6
                            text-muted
                        ">
                        {{ bio }}
                    </p>

                    <div class="
                            mt-4 flex
                            flex-wrap gap-6
                            text-sm
                        ">
                        <div>
                            <span class="font-semibold">
                                {{ user.followingCount }}
                            </span>

                            <span class="ml-1 text-muted">
                                关注
                            </span>
                        </div>

                        <div>
                            <span class="font-semibold">
                                {{ user.followersCount }}
                            </span>

                            <span class="ml-1 text-muted">
                                粉丝
                            </span>
                        </div>
                    </div>

                    <div class="mt-4 space-y-1 text-xs text-muted">
                        <div>
                            创建于：{{ formatDate(user.createdAtUtc) }}
                        </div>

                        <div>
                            最近更新：{{ formatDate(user.updatedAtUtc) }}
                        </div>
                    </div>
                </div>
            </div>

            <UButton icon="i-lucide-pencil" label="编辑资料" variant="outline" :loading="pending" :disabled="pending"
                @click="handleEdit" />
        </div>
    </UCard>
</template>