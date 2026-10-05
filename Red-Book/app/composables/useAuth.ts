import { getApiErrorMessage } from '~/utils/api-error'

export function useAuth() {
    const session = useUserSession()
    const currentUser = useCurrentUser()
    const posts = usePostStore()
    const toast = useToast()
    const logoutPending = useState('logout-pending', () => false)

    function clearUserData() {
        posts.clear()
        currentUser.clearCurrentUser()
        clearNuxtData()
    }

    async function logout() {
        if (logoutPending.value) return
        logoutPending.value = true
        let failed = false
        try {
            await $fetch('/api/auth/logout', { method: 'POST', retry: 0 })
        }
        catch (error) {
            failed = true
            toast.add({
                title: '退出登录未完全完成',
                description: getApiErrorMessage(error, '请稍后重试。'),
                color: 'warning',
            })
        }
        finally {
            try {
                // The BFF may have cleared the local session before reporting
                // an Identity outage. Always reconcile the UI with the server.
                await session.fetch()
                if (!session.loggedIn.value) {
                    clearUserData()
                    await navigateTo('/login')
                }
                else if (!failed) {
                    toast.add({ title: '会话状态更新失败，请重试。', color: 'error' })
                }
            }
            finally {
                logoutPending.value = false
            }
        }
    }

    return { logout, logoutPending, clearUserData }
}
