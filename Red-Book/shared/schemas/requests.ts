import { z } from 'zod'

export class RequestValidationError extends Error {
    readonly field: string
    readonly issue?: z.ZodError['issues'][number]
    constructor(field: string, message: string, issue?: z.ZodError['issues'][number]) {
        super(message)
        this.name = 'RequestValidationError'
        this.field = field
        this.issue = issue
    }
}

function text(field: string, max: number, trim = true) {
    const schema = z.string({ error: `${field} must be a string.` })
    // UTF-16 length matches the existing .NET string limits, including emoji.
    return (trim ? schema.trim() : schema).refine(
        value => value.length > 0 && value.length <= max,
        { error: `${field} must contain between 1 and ${max} characters.`, params: { minimum: 1, maximum: max } },
    )
}

function optionalText(field: string, max: number) {
    const error = `${field} must be a string of at most ${max} characters.`
    return z.string({ error }).refine(value => value.length <= max, { error, params: { maximum: max } })
        .transform(value => value.trim() || null).nullish()
}

function id(field: string) {
    const error = `${field} must be a non-empty UUID.`
    return z.string({ error }).regex(
        /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i,
        { error },
    ).refine(value => value !== '00000000-0000-0000-0000-000000000000', { error })
        .transform(value => value.toLowerCase())
}

const bodyError = { error: 'Request body must be an object.' }

export const loginSchema = z.object({
    email: text('email', 256).regex(/^[^\s@]+@[^\s@]+\.[^\s@]+$/, { error: 'Email is invalid.' }),
    password: text('password', 1024, false),
}, bodyError)

export const registerSchema = loginSchema.extend({
    displayName: optionalText('displayName', 64).default(null),
    familyName: optionalText('familyName', 64).default(null),
})

export const postSchema = z.object({
    title: text('title', 100),
    content: text('content', 5000),
    mediaIds: z.array(id('mediaIds'), { error: 'MediaIds must be an array of at most 9 UUIDs.' })
        .max(9, { error: 'MediaIds must be an array of at most 9 UUIDs.' })
        .transform(values => [...new Set(values)]),
    tags: z.array(text('tags', 30), { error: 'Tags must be an array of at most 10 strings.' })
        .max(10, { error: 'Tags must be an array of at most 10 strings.' })
        .transform(values => {
            const seen = new Set<string>()
            return values.filter(value => {
                const key = value.toLowerCase()
                if (seen.has(key)) return false
                seen.add(key)
                return true
            })
        }),
}, bodyError)

export const commentSchema = z.object({
    content: text('content', 1000),
    parentCommentId: id('parentCommentId').nullish().transform(value => value ?? null),
}, bodyError)

const avatarSchema = optionalText('avatarUrl', 2048).superRefine((value, context) => {
    if (!value) return
    const localMedia = /^\/api\/media\/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$/i.exec(value)
    if (localMedia && id('avatarUrl').safeParse(localMedia[1]).success) return
    let url: URL
    try { url = new URL(value) }
    catch {
        context.addIssue({ code: 'custom', message: 'Avatar URL is invalid.' })
        return
    }
    if (!['http:', 'https:'].includes(url.protocol)) {
        context.addIssue({ code: 'custom', message: 'Avatar URL must use HTTP or HTTPS.' })
    }
})

export const profileSchema = z.object({
    nickname: optionalText('nickname', 64),
    avatarUrl: avatarSchema,
    bio: optionalText('bio', 500),
}, bodyError)

function integer(field: string, fallback: number, maximum: number) {
    const error = `${field} must be a positive integer.`
    return z.string({ error }).regex(/^[1-9]\d*$/, { error }).transform(Number)
        .refine(value => Number.isSafeInteger(value) && value <= maximum,
            { error: `${field} is out of range.` }).default(fallback)
}

export const paginationSchema = z.object({
    page: integer('page', 1, 1_000_000),
    pageSize: integer('pageSize', 20, 100),
}, bodyError)
export const searchSchema = paginationSchema.extend({ q: text('q', 100) })

// Browser and BFF share validation and normalization, preserving the HTTP error contract.
export function parseRequest<T>(schema: z.ZodType<T>, value: unknown, rootField = 'body'): T {
    const result = schema.safeParse(value)
    if (result.success) return result.data
    const issue = result.error.issues[0]!
    throw new RequestValidationError(String(issue.path[0] ?? rootField), issue.message, issue)
}

export const parseId = (value: unknown, field = 'id') => parseRequest(id(field), value, field)
export const parseLogin = (value: unknown) => parseRequest(loginSchema, value)
export const parseRegister = (value: unknown) => parseRequest(registerSchema, value)
export const parsePost = (value: unknown) => parseRequest(postSchema, value)
export const parseComment = (value: unknown) => parseRequest(commentSchema, value)
export const parseProfile = (value: unknown) => parseRequest(profileSchema, value)
export const parsePagination = (value: unknown) => parseRequest(paginationSchema, value)
export const parseSearch = (value: unknown) => parseRequest(searchSchema, value)
