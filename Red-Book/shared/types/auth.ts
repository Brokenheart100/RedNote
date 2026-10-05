import type { z } from 'zod'
import type { loginSchema, registerSchema } from '../schemas/requests'

export type RegisterRequest = z.output<typeof registerSchema>

export interface RegisterResponse {
    id: string
    email: string
    displayName: string | null
    familyName: string | null
    createdAtUtc: string
}

export type LoginRequest = z.output<typeof loginSchema>
