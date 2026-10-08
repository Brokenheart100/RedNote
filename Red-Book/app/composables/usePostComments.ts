import type { Ref } from 'vue'
import type { PostCommentItem, PostCommentResponse, PostCommentsResponse, PostResponse } from '~~/shared/types/posts'
import { createLatestRequest } from '~/utils/latest-request'
import { getApiErrorMessage, getApiErrorStatus } from '~/utils/api-error'
import { commentSchema, parseComment } from '~~/shared/schemas/requests'
import { getRequestValidationMessage } from '~/utils/request-validation'

export function usePostComments(post: Readonly<Ref<PostResponse>>, open: Ref<boolean>) {
    const store = usePostStore()
    const toast = useToast()
    const submissionKey = useSubmissionKey()
    const requests = createLatestRequest()
    const submissions = createLatestRequest()
    const deletions = createLatestRequest()
    const commentDeletePending = ref(false)
    const comments = ref<PostCommentItem[]>([])
    const commentsPending = ref(false)
    const commentsLoaded = ref(false)
    const commentsError = ref<string | null>(null)
    const commentContent = ref('')
    const commentSubmitting = ref(false)
    const localCommentCount = ref(post.value.commentCount)
    const replyingTo = ref<PostCommentItem | null>(null)
    const page = ref(0)
    const total = ref(0)
    const hasMoreComments = computed(() => comments.value.length < total.value)
    const canSubmitComment = computed(() =>
        commentSchema.safeParse({ content: commentContent.value, parentCommentId: replyingTo.value?.id }).success
        && !commentSubmitting.value && !commentDeletePending.value && !commentsPending.value)

    async function deleteComment(comment: PostCommentResponse): Promise<boolean> {
        if (commentDeletePending.value || commentSubmitting.value) return false
        const id = post.value.id
        const ticket = deletions.start()
        requests.invalidate()
        commentsPending.value = false
        commentDeletePending.value = true
        try {
            await $fetch(`/api/posts/${encodeURIComponent(id)}/comments/${encodeURIComponent(comment.id)}`, {
                method: 'DELETE', retry: 0, signal: ticket.signal,
            })
            if (!ticket.isCurrent() || id !== post.value.id) return false
            const root = comments.value.find(item => item.id === comment.id)
            localCommentCount.value = Math.max(0, localCommentCount.value - 1 - (root?.replies.length ?? 0))
            store.patchPost(id, { commentCount: localCommentCount.value })
            if (replyingTo.value?.id === comment.id) replyingTo.value = null
            // Offset pagination shifts after deletion. Reload page one rather
            // than skip records by continuing from the previous page offset.
            comments.value = []
            page.value = 0
            total.value = 0
            commentsLoaded.value = false
            await loadComments()
            return ticket.isCurrent() && id === post.value.id
        }
        catch (error) {
            if (ticket.isCurrent()) {
                toast.add({ title: '评论删除失败',
                    description: getApiErrorMessage(error, '请稍后重试。'), color: 'error' })
            }
            return false
        }
        finally {
            if (ticket.isCurrent()) commentDeletePending.value = false
        }
    }

    async function loadComments(append = false) {
        if (commentsPending.value) return
        const id = post.value.id
        const ticket = requests.start()
        const nextPage = append ? page.value + 1 : 1
        commentsPending.value = true
        commentsError.value = null
        try {
            const response = await $fetch<PostCommentsResponse>(
                `/api/posts/${encodeURIComponent(id)}/comments`,
                { query: { page: nextPage, pageSize: 50 }, signal: ticket.signal },
            )
            if (!ticket.isCurrent() || id !== post.value.id) return
            const items = append ? [...comments.value, ...response.items] : response.items
            comments.value = [...new Map(items.map(item => [item.id, item])).values()]
            page.value = nextPage
            total.value = response.totalCount
            localCommentCount.value = post.value.commentCount
            commentsLoaded.value = true
        }
        catch (error) {
            if (ticket.isCurrent()) {
                commentsError.value = getApiErrorMessage(error, '评论加载失败，请稍后重试。')
            }
        }
        finally {
            if (ticket.isCurrent()) commentsPending.value = false
        }
    }

    async function submitComment() {
        if (!canSubmitComment.value) return
        const id = post.value.id
        const parent = replyingTo.value
        const ticket = submissions.start()
        commentSubmitting.value = true
        try {
            const body = parseComment({ content: commentContent.value, parentCommentId: parent?.id ?? null })
            const created = await $fetch<PostCommentResponse>(
                `/api/posts/${encodeURIComponent(id)}/comments`,
                {
                    method: 'POST', retry: 0, signal: ticket.signal,
                    headers: { 'Idempotency-Key': submissionKey.getKey({ postId: id, ...body }) },
                    body,
                },
            )
            submissionKey.reset()
            if (!ticket.isCurrent() || id !== post.value.id) return
            const alreadyListed = comments.value.some(item => item.id === created.id
                || item.replies.some(reply => reply.id === created.id))
            if (!alreadyListed && parent) {
                const target = comments.value.find(item => item.id === parent.id)
                target?.replies.push(created)
            }
            else if (!alreadyListed) {
                comments.value.unshift({ ...created, replies: [] })
                total.value++
            }
            commentContent.value = ''
            replyingTo.value = null
            if (!alreadyListed) localCommentCount.value++
            store.patchPost(id, { commentCount: localCommentCount.value })
        }
        catch (error) {
            if (ticket.isCurrent()) {
                if (getApiErrorStatus(error) === 409) {
                    toast.add({ title: '评论仍在处理中，请稍后重试。', color: 'info' })
                    return
                }
                toast.add({
                    title: '评论发布失败',
                    description: getRequestValidationMessage(error) ?? getApiErrorMessage(error, '请稍后重试。'),
                    color: 'error',
                })
            }
        }
        finally {
            if (ticket.isCurrent()) commentSubmitting.value = false
        }
    }

    function reset() {
        requests.invalidate()
        submissions.invalidate()
        deletions.invalidate()
        commentDeletePending.value = false
        comments.value = []
        commentsPending.value = false
        commentsLoaded.value = false
        commentsError.value = null
        commentSubmitting.value = false
        commentContent.value = ''
        replyingTo.value = null
        page.value = 0
        total.value = 0
        localCommentCount.value = post.value.commentCount
    }

    watch(() => post.value.id, () => {
        reset()
        if (open.value) void loadComments()
    })
    watch(open, value => {
        if (!value) {
            // An aborted submission can still finish on the server. Reload on
            // reopening so a late response cannot leave a stale cached list.
            reset()
        }
        else if (!commentsLoaded.value) {
            void loadComments()
        }
    }, { immediate: true })
    onBeforeUnmount(reset)

    return {
        comments, commentsPending, commentsLoaded, commentsError,
        commentContent, commentSubmitting, localCommentCount,
        replyingTo, canSubmitComment, loadComments, submitComment,
        deleteComment, commentDeletePending,
        hasMoreComments,
        loadMoreComments: () => loadComments(true),
        startReply: (comment: PostCommentItem) => { replyingTo.value = comment },
        cancelReply: () => { replyingTo.value = null },
    }
}
