export interface MediaUploadResponse extends Record<string, unknown> {
    id: string
    fileName: string
    contentType: string
    size: number
    objectKey: string
    createdAtUtc: string
}