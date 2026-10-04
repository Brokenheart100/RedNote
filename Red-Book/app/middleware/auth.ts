export default defineNuxtRouteMiddleware(
    async to => {
        const {
            ready,
            loggedIn,
            fetch,
        } = useUserSession()

        if (!ready.value) {
            await fetch()
        }

        if (loggedIn.value) {
            return
        }

        return navigateTo({
            path: '/login',
            query: {
                returnUrl: to.fullPath,
            },
        })
    },
)