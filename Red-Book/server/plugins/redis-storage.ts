import { closeAuthRedis, getAuthRedis } from '../utils/auth-redis'

export default defineNitroPlugin(nitroApp => {
    if (import.meta.prerender) return
    getAuthRedis()
    nitroApp.hooks.hook('close', closeAuthRedis)
})