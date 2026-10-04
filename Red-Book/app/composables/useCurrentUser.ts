import type { CurrentUser } from '~~/shared/types/users'
import { getApiErrorMessage } from '~/utils/api-error'

interface CurrentUserState {
    user: CurrentUser | null
    pending: boolean
    loaded: boolean
    error: string | null
}

export function useCurrentUser() {
    const state = useState<CurrentUserState>(
        'current-user',
        () => ({
            user: null,
            pending: false,
            loaded: false,
            error: null,
        }),
    )

    const user = computed(
        () => state.value.user,
    )

    const pending = computed(
        () => state.value.pending,
    )

    const loaded = computed(
        () => state.value.loaded,
    )

    const error = computed(
        () => state.value.error,
    )

    async function fetchCurrentUser(
        force = false,
    ): Promise<CurrentUser | null> {
        if (
            state.value.loaded
            && !force
        ) {
            return state.value.user
        }

        if (state.value.pending) {
            return state.value.user
        }

        state.value.pending = true
        state.value.error = null

        try {
            const requestFetch =
                useRequestFetch()

            const result =
                await requestFetch<CurrentUser>(
                    '/api/users/me',
                )

            state.value.user = result
            state.value.loaded = true

            if (import.meta.dev) {
                console.log(
                    '✅ [CURRENT USER] 用户资料加载成功',
                    {
                        userId: result.userId,
                    },
                )
            }

            return result
        }
        catch (error: unknown) {
            state.value.user = null
            state.value.loaded = false

            state.value.error =
                getApiErrorMessage(
                    error,
                    '获取当前用户失败。',
                )

            if (import.meta.dev) {
                console.error(
                    '❌ [CURRENT USER] 用户资料加载失败',
                    {
                        message:
                            state.value.error,
                    },
                )
            }

            return null
        }
        finally {
            state.value.pending = false
        }
    }

    async function refreshCurrentUser():
        Promise<CurrentUser | null> {
        return await fetchCurrentUser(true)
    }

    function clearCurrentUser(): void {
        state.value.user = null
        state.value.pending = false
        state.value.loaded = false
        state.value.error = null
    }

    return {
        user,
        pending,
        loaded,
        error,
        fetchCurrentUser,
        refreshCurrentUser,
        clearCurrentUser,
    }
}