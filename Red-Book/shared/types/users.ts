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

export interface UpdateMeRequest {
    nickname?: string | null
    avatarUrl?: string | null
    bio?: string | null
}

export interface UpdateMeResponse {
    userId: string
    nickname: string | null
    avatarUrl: string | null
    bio: string | null
    createdAtUtc: string
    updatedAtUtc: string
}