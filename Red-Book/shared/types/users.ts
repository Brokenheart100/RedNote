import type { z } from 'zod'
import type { profileSchema } from '../schemas/requests'

export interface CurrentUser {
    userId: string
    nickname: string | null
    avatarUrl: string | null
    bio: string | null
    followersCount: number
    followingCount: number
    isFollowing: boolean
    createdAtUtc: string
    updatedAtUtc: string
}

export type UpdateMeRequest = z.output<typeof profileSchema>

export interface UpdateMeResponse {
    userId: string
    nickname: string | null
    avatarUrl: string | null
    bio: string | null
    createdAtUtc: string
    updatedAtUtc: string
}
