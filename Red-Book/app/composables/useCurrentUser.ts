import type { CurrentUser } from '~~/shared/types/users'
import { getApiErrorMessage } from '~/utils/api-error'

export function useCurrentUser() {
    const requestFetch = useRequestFetch()
    const request = useAsyncData('current-user',
        (_nuxtApp, { signal }) => requestFetch<CurrentUser>('/api/users/me', { signal }),
        { immediate: false, dedupe: 'defer', default: () => null },
    )

    async function fetchCurrentUser(force = false): Promise<CurrentUser | null> {
        if (!force && request.status.value === 'success') return request.data.value
        await request.execute()
        return request.data.value
    }

    return {
        user: computed(() => request.data.value),
        pending: request.pending,
        loaded: computed(() => request.status.value === 'success'),
        error: computed(() => request.error.value
            ? getApiErrorMessage(request.error.value, '获取当前用户失败。')
            : null),
        fetchCurrentUser,
        refreshCurrentUser: () => fetchCurrentUser(true),
        // Nuxt clear() aborts the request and prevents late results from writing.
        clearCurrentUser: request.clear,
    }
}
