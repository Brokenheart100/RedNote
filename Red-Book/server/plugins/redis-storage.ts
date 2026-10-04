import redisDriver from 'unstorage/drivers/redis'

export default defineNitroPlugin(() => {
    if (import.meta.prerender) {
        console.log(
            'ℹ️ [REDIS] Prerender 阶段跳过 Redis storage 初始化。',
        )

        return
    }

    const redisUrl = process.env.REDIS_URI

    if (!redisUrl) {
        throw new Error(
            'REDIS_URI is not configured.',
        )
    }

    const storage = useStorage()

    storage.mount(
        'authTokens',
        redisDriver({
            base: 'rednote:auth-tokens',
            url: redisUrl,
        }),
    )

    console.log(
        '✅ [REDIS] authTokens storage mounted.',
    )
})