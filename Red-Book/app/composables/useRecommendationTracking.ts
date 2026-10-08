import { useDebounceFn } from '@vueuse/core'
import type { RecommendationFeedback } from '~~/shared/schemas/recommendations'

export function useRecommendationTracking() {
    const pending = new Map<string, RecommendationFeedback>()
    const recorded = new Set<string>()
    async function flush(): Promise<void> {
        const batches = [...pending.values()]
        pending.clear()
        for (const batch of batches) {
            for (let offset = 0; offset < batch.items.length; offset += 50) {
                try {
                    await $fetch('/api/posts/recommendations/feedback', {
                        method: 'POST', body: { ...batch, items: batch.items.slice(offset, offset + 50) }, keepalive: true,
                    })
                }
                catch (error) { console.warn('[RECOMMENDATION] 行为上报失败', error) }
            }
        }
    }
    const send = useDebounceFn(flush, 300, { maxWait: 1000 })
    function record(requestId: string, postId: string, type: 'read' | 'click'): void {
        if (!requestId) return
        const key = `${requestId}:${postId}:${type}`
        if (recorded.has(key)) return
        recorded.add(key)
        const batch = pending.get(requestId) ?? { requestId, items: [] }
        batch.items.push({ postId, type })
        pending.set(requestId, batch)
        void send()
    }
    onScopeDispose(() => { void flush() })
    return { record }
}
