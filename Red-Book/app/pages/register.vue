<script setup lang="ts">
import { parseRegister } from '~~/shared/schemas/requests'
import { getRequestValidationMessage } from '~/utils/request-validation'
import type { RegisterResponse } from '~~/shared/types/auth'

import {
    createAuthRoute,
    createDevelopmentRandomText,
    resolveReturnUrl,
    storeDevelopmentLogin,
} from '~/utils/auth-form'

import {
    getApiErrorMessage,
    getApiErrorStatus,
} from '~/utils/api-error'

const route = useRoute()

const email = ref('')
const displayName = ref('')
const familyName = ref('')
const password = ref('')
const confirmPassword = ref('')

const submitting = ref(false)
const errorMessage = ref<string | null>(null)

const returnUrl = computed(() => resolveReturnUrl(route.query.returnUrl))

const loginRoute = computed(() => createAuthRoute('/login', returnUrl.value))

function fillDevelopmentForm(): void {
    if (!import.meta.dev) {
        return
    }

    const suffix = createDevelopmentRandomText(8)
    const generatedPassword = `RedNote@${createDevelopmentRandomText(12)}Aa1`

    email.value = `user_${suffix}@example.com`
    displayName.value = `测试用户 ${suffix}`
    familyName.value = 'RedNote'
    password.value = generatedPassword
    confirmPassword.value = generatedPassword
}

function resolveRegisterError(error: unknown): string {
    const validationMessage = getRequestValidationMessage(error)
    if (validationMessage) return validationMessage
    switch (getApiErrorStatus(error)) {
        case 400:
            return getApiErrorMessage(error, '注册信息无效。')

        case 409:
            return '该邮箱已经注册。'

        case 429:
            return '注册请求过于频繁，请稍后再试。'

        default:
            return '注册失败，请稍后重试。'
    }
}

async function handleSubmit(): Promise<void> {
    if (submitting.value) {
        return
    }

    errorMessage.value = null

    if (password.value !== confirmPassword.value) {
        errorMessage.value = '两次输入的密码不一致。'
        return
    }

    submitting.value = true

    try {
        const request = parseRegister({
            email: email.value,
            password: password.value,
            displayName: displayName.value,
            familyName: familyName.value,
        })
        await $fetch<RegisterResponse>('/api/auth/register', {
            method: 'POST',

            body: request,
        })

        storeDevelopmentLogin({
            email: request.email,
            password: password.value,
        })

        await navigateTo({
            path: '/login',

            query: {
                registered: '1',
                email: request.email,
                ...(returnUrl.value
                    ? {
                        returnUrl: returnUrl.value,
                    }
                    : {}),
            },
        })
    }
    catch (error: unknown) {
        errorMessage.value = resolveRegisterError(error)
    }
    finally {
        submitting.value = false
    }
}

onMounted(() => {
    if (typeof route.query.email === 'string') {
        email.value = route.query.email
    }

    fillDevelopmentForm()
})
</script>

<template>
    <UContainer class="flex min-h-screen items-center justify-center py-12">
        <div class="w-full max-w-md">
            <UCard>
                <template #header>
                    <div class="space-y-2 text-center">
                        <div class="mx-auto flex size-12 items-center justify-center rounded-xl bg-primary/10">
                            <UIcon name="i-lucide-user-plus" class="size-6 text-primary" />
                        </div>

                        <div>
                            <h1 class="text-2xl font-semibold">
                                创建 RedNote 账号
                            </h1>

                            <p class="mt-1 text-sm text-muted">
                                注册后即可开始使用 RedNote
                            </p>
                        </div>
                    </div>
                </template>

                <form class="space-y-5" @submit.prevent="handleSubmit">
                    <UFormField label="邮箱" required>
                        <UInput v-model="email" type="email" autocomplete="email" placeholder="请输入邮箱"
                            icon="i-lucide-mail" :disabled="submitting" class="w-full" autofocus />
                    </UFormField>

                    <UFormField label="显示名称">
                        <UInput v-model="displayName" type="text" autocomplete="name" placeholder="请输入显示名称"
                            icon="i-lucide-user" :disabled="submitting" class="w-full" />
                    </UFormField>

                    <UFormField label="姓氏">
                        <UInput v-model="familyName" type="text" autocomplete="family-name" placeholder="请输入姓氏"
                            icon="i-lucide-signature" :disabled="submitting" class="w-full" />
                    </UFormField>

                    <UFormField label="密码" required>
                        <UInput v-model="password" type="password" autocomplete="new-password" placeholder="请输入密码"
                            icon="i-lucide-lock-keyhole" :disabled="submitting" class="w-full" />
                    </UFormField>

                    <UFormField label="确认密码" required>
                        <UInput v-model="confirmPassword" type="password" autocomplete="new-password"
                            placeholder="请再次输入密码" icon="i-lucide-lock-keyhole" :disabled="submitting" class="w-full" />
                    </UFormField>

                    <UAlert v-if="errorMessage" color="error" variant="soft" icon="i-lucide-circle-alert" title="注册失败"
                        :description="errorMessage" />

                    <UButton type="submit" block size="lg" icon="i-lucide-user-plus" :loading="submitting"
                        :disabled="submitting">
                        注册
                    </UButton>
                </form>

                <template #footer>
                    <p class="text-center text-sm text-muted">
                        已有账号？

                        <UButton :to="loginRoute" variant="link" size="sm" class="px-1">
                            返回登录
                        </UButton>
                    </p>
                </template>
            </UCard>
        </div>
    </UContainer>
</template>
