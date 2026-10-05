import type { z } from 'zod'
import type { postSchema } from '../schemas/requests'

export interface PostMediaResponse {
    id: string
    fileName: string
    contentType: string
    size: number
    url: string
}

export interface PostAuthorResponse {
    userId: string
    nickname: string | null
    avatarUrl: string | null
}

export interface PostResponse {
    id: string
    authorUserId: string
    author: PostAuthorResponse
    title: string
    content: string
    mediaIds: string[]
    media: PostMediaResponse[]
    tags: string[] | null
    likeCount: number
    commentCount: number
    isLiked: boolean
    isFavorited: boolean
    createdAtUtc: string
    updatedAtUtc: string
}

export interface FeedResponse {
    page: number
    pageSize: number
    totalCount: number
    items: PostResponse[]
}

export interface LikedPostsResponse {
    page: number
    pageSize: number
    totalCount: number
    items: PostResponse[]
}

export type CreatePostRequest = z.output<typeof postSchema>

export interface CommentAuthorResponse {
    userId: string
    nickname: string | null
    avatarUrl: string | null
}

export interface PostCommentResponse {
    id: string
    postId: string
    authorUserId: string
    author: CommentAuthorResponse
    content: string
    parentCommentId: string | null
    createdAtUtc: string
    updatedAtUtc: string
}

export interface PostCommentItem extends PostCommentResponse {
    replies: PostCommentResponse[]
}

export interface PostCommentsResponse {
    page: number
    pageSize: number
    totalCount: number
    items: PostCommentItem[]
}
