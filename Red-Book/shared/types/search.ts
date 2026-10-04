import type { PostResponse } from './posts'

export interface SearchPostsResponse {
    page: number
    pageSize: number
    totalCount: number
    items: PostResponse[]
}