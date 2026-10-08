// Keep one key for an unchanged submission after an ambiguous network failure.
// Edited content is a new intent and receives a new key.
export function useSubmissionKey() {
    let previousBody: string | undefined
    let key: string | undefined
    const getKey = (body: unknown): string => {
        const serialized = JSON.stringify(body)
        if (!key || serialized !== previousBody) {
            key = crypto.randomUUID()
            previousBody = serialized
        }
        return key
    }
    return { getKey, reset: () => { previousBody = undefined; key = undefined } }
}
