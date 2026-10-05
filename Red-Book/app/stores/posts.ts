import { defineStore } from 'pinia'

import type { PostResponse } from '~~/shared/types/posts'

export const usePostStore = defineStore('posts', () => {
    const postsById = ref<Record<string, PostResponse>>({})
    const likePendingById = ref<Record<string, boolean>>({})
    const favoritePendingById = ref<Record<string, boolean>>({})
    const deletedPostIds = ref<Record<string, boolean>>({})
    const deletePendingById = ref<Record<string, boolean>>({})
    let generation = 0

    const isDeleted = (postId: string) => deletedPostIds.value[postId] === true
    const isDeletePending = (postId: string) => deletePendingById.value[postId] === true

    async function deletePost(postId: string): Promise<void> {
        if (isDeletePending(postId) || isDeleted(postId)) return
        const startedGeneration = generation
        deletePendingById.value[postId] = true
        try {
            await $fetch(`/api/posts/${encodeURIComponent(postId)}`, { method: 'DELETE', retry: 0 })
            if (generation !== startedGeneration) return
            deletedPostIds.value[postId] = true
            delete postsById.value[postId]
        }
        finally {
            if (generation === startedGeneration) delete deletePendingById.value[postId]
        }
    }

    function getPost(postId: string): PostResponse | undefined {
        return postsById.value[postId]
    }

    function isLikePending(postId: string): boolean {
        return likePendingById.value[postId] === true
    }

    function isFavoritePending(postId: string): boolean {
        return favoritePendingById.value[postId] === true
    }

    function clonePost(post: PostResponse): PostResponse {
        return {
            ...post,
            author: { ...post.author },
            mediaIds: [...post.mediaIds],
            media: post.media.map(media => ({ ...media })),
            tags: post.tags ? [...post.tags] : null,
        }
    }

    function upsertPost(post: PostResponse): void {
        if (isDeleted(post.id)) return
        const current = postsById.value[post.id]
        const next = clonePost(post)

        /*
         * Optimistic mutation 进行期间，服务端较旧的查询结果不能覆盖
         * 当前正在变化的交互状态。
         */
        if (current && isLikePending(post.id)) {
            next.isLiked = current.isLiked
            next.likeCount = current.likeCount
        }

        if (current && isFavoritePending(post.id)) {
            next.isFavorited = current.isFavorited
        }

        postsById.value[post.id] = next
    }

    function upsertPosts(posts: readonly PostResponse[]): void {
        for (const post of posts) {
            upsertPost(post)
        }
    }

    function patchPost(postId: string, patch: Partial<PostResponse>): void {
        const post = postsById.value[postId]

        if (!post) {
            return
        }

        postsById.value[postId] = {
            ...post,
            ...patch,
        }
    }

    async function toggleLike(postId: string): Promise<void> {
        const post = getPost(postId)

        if (!post) {
            throw new Error(`Post '${postId}' is not registered in the post store.`)
        }

        if (isLikePending(postId)) {
            return
        }

        const previousLiked = post.isLiked
        const previousLikeCount = post.likeCount
        const nextLiked = !previousLiked

        likePendingById.value[postId] = true

        patchPost(postId, {
            isLiked: nextLiked,
            likeCount: Math.max(0, previousLikeCount + (nextLiked ? 1 : -1)),
        })

        try {
            await $fetch(`/api/posts/${encodeURIComponent(postId)}/likes`, {
                method: nextLiked ? 'POST' : 'DELETE',
            })

            if (import.meta.dev) {
                console.log(nextLiked ? '❤️ [POST STORE] 点赞成功' : '💔 [POST STORE] 取消点赞成功', {
                    postId,
                    likeCount: postsById.value[postId]?.likeCount,
                })
            }
        }
        catch (error: unknown) {
            patchPost(postId, {
                isLiked: previousLiked,
                likeCount: previousLikeCount,
            })

            console.error(nextLiked ? '❌ [POST STORE] 点赞失败，已回滚' : '❌ [POST STORE] 取消点赞失败，已回滚', {
                postId,
                error,
            })

            throw error
        }
        finally {
            delete likePendingById.value[postId]
        }
    }

    /*
     * Favorite mutation 暂不实现。
     *
     * 当前先保留 Favorite 状态同步能力。
     * 等 POST / DELETE favorites BFF handler 完成后，
     * 再在这里实现与 toggleLike 相同的 optimistic mutation。
     */
    function setFavoriteState(postId: string, isFavorited: boolean): void {
        if (!getPost(postId)) {
            return
        }

        patchPost(postId, {
            isFavorited,
        })
    }

    function clear(): void {
        generation++
        deletedPostIds.value = {}
        deletePendingById.value = {}
        postsById.value = {}
        likePendingById.value = {}
        favoritePendingById.value = {}
    }

    return {
        postsById,

        getPost,
        isDeleted, isDeletePending, deletePost,
        upsertPost,
        upsertPosts,
        patchPost,

        isLikePending,
        isFavoritePending,

        toggleLike,
        setFavoriteState,

        clear,
    }
})
