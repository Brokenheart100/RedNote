// A new request (or a state reset) invalidates responses from earlier requests.
export function createLatestRequest() {
    let version = 0
    let controller: AbortController | undefined

    return {
        start() {
            controller?.abort()
            controller = new AbortController()
            const current = ++version
            return {
                signal: controller.signal,
                isCurrent: () => current === version,
            }
        },
        invalidate() {
            version++
            controller?.abort()
            controller = undefined
        },
    }
}
