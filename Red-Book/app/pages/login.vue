<script setup lang="ts">
import { parseLogin } from '~~/shared/schemas/requests'
import { getRequestValidationMessage } from '~/utils/request-validation'
import {
  consumeDevelopmentLogin,
  createAuthRoute,
  resolveReturnUrl,
} from '~/utils/auth-form'

import {
  getApiErrorMessage,
  getApiErrorStatus,
} from '~/utils/api-error'

const route = useRoute()

const email = ref(
  typeof route.query.email === 'string'
    ? route.query.email
    : '',
)

const password = ref('')

const submitting = ref(false)
const errorMessage = ref<string | null>(null)

const registrationSucceeded = computed(
  () => route.query.registered === '1',
)

const returnUrl = computed(
  () => resolveReturnUrl(route.query.returnUrl),
)

const registerRoute = computed(
  () => createAuthRoute(
    '/register',
    returnUrl.value,
  ),
)

function restoreDevelopmentLogin(): void {
  const login = consumeDevelopmentLogin()

  if (!login) {
    return
  }

  email.value = login.email
  password.value = login.password
}

function resolveLoginError(error: unknown): string {
  const validationMessage = getRequestValidationMessage(error)
  if (validationMessage) return validationMessage
  switch (getApiErrorStatus(error)) {
    case 400:
      return getApiErrorMessage(
        error,
        '登录请求无效。',
      )

    case 401:
      return '邮箱或密码错误。'

    case 423:
      return '账号暂时被锁定，请稍后再试。'

    case 429:
      return '登录尝试过于频繁，请稍后再试。'

    default:
      return '登录失败，请稍后重试。'
  }
}

async function handleSubmit(): Promise<void> {
  if (submitting.value) {
    return
  }

  errorMessage.value = null

  submitting.value = true

  try {
    await $fetch('/api/auth/login', {
      method: 'POST',

      body: parseLogin({ email: email.value, password: password.value }),
    })

    if (import.meta.dev) {
      console.log(
        '[LOGIN] ✅ Identity Cookie 登录成功，进入 OIDC 流程',
        {
          requestedReturnUrl: returnUrl.value,
          next: '/auth/rednote',
        },
      )
    }

    /*
     * Identity Application Cookie 已建立。
     * 接下来进入标准 OIDC Authorization Code + PKCE 流程。
     */
    window.location.href = '/auth/rednote'
  }
  catch (error: unknown) {
    errorMessage.value = resolveLoginError(error)
  }
  finally {
    submitting.value = false
  }
}

onMounted(() => {
  restoreDevelopmentLogin()
})
</script>

<template>
  <UContainer class="
            flex min-h-screen
            items-center justify-center
            py-12
        ">
    <div class="w-full max-w-md">
      <UCard>
        <template #header>
          <div class="space-y-2 text-center">
            <div class="
                                mx-auto flex size-12
                                items-center justify-center
                                rounded-xl bg-primary/10
                            ">
              <UIcon name="i-lucide-book-heart" class="size-6 text-primary" />
            </div>

            <div>
              <h1 class="text-2xl font-semibold">
                RedNote
              </h1>

              <p class="mt-1 text-sm text-muted">
                登录后继续使用 RedNote
              </p>
            </div>
          </div>
        </template>

        <form class="space-y-5" @submit.prevent="handleSubmit">
          <UAlert v-if="registrationSucceeded" color="success" variant="soft" icon="i-lucide-circle-check" title="注册成功"
            description="账号已创建，可以直接登录。" />

          <UFormField label="邮箱" required>
            <UInput v-model="email" type="email" autocomplete="email" placeholder="请输入邮箱" icon="i-lucide-mail"
              :disabled="submitting" class="w-full" autofocus />
          </UFormField>

          <UFormField label="密码" required>
            <UInput v-model="password" type="password" autocomplete="current-password" placeholder="请输入密码"
              icon="i-lucide-lock-keyhole" :disabled="submitting" class="w-full" />
          </UFormField>

          <UAlert v-if="errorMessage" color="error" variant="soft" icon="i-lucide-circle-alert" title="登录失败"
            :description="errorMessage" />

          <UButton type="submit" block size="lg" icon="i-lucide-log-in" :loading="submitting" :disabled="submitting">
            登录
          </UButton>
        </form>

        <template #footer>
          <p class="text-center text-sm text-muted">
            还没有账号？

            <UButton :to="registerRoute" variant="link" size="sm" class="px-1">
              注册
            </UButton>
          </p>
        </template>
      </UCard>
    </div>
  </UContainer>
</template>
