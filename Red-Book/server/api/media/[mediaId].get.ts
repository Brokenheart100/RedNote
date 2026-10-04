interface MediaResponse {
    id: string
    ownerUserId: string
    fileName: string
    contentType: string
    size: number
    createdAtUtc: string
    url: string
}

export default defineEventHandler(
    async event => {
        const mediaId =
            getRouterParam(
                event,
                'mediaId',
            )

        if (!mediaId) {
            throw createError({
                statusCode: 400,
                statusMessage:
                    'Media ID is required.',
            })
        }

        const config =
            useRuntimeConfig(
                event,
            )

        const path =
            `/api/v1/media/${encodeURIComponent(
                mediaId,
            )}`

        try {
            const media =
                await $fetch<
                    MediaResponse
                >(
                    path,
                    {
                        baseURL:
                            config.public
                                .apiBaseUrl,
                    },
                )

            if (!media.url) {
                throw createError({
                    statusCode: 502,
                    statusMessage:
                        'Media URL is unavailable.',
                })
            }

            /*
             * MediaService 每次都会生成新的
             * 15 分钟 GET Presigned URL。
             *
             * 浏览器最终直接从 MinIO / S3 读取图片。
             */
            return sendRedirect(
                event,
                media.url,
                302,
            )
        }
        catch (error: unknown) {
            const fetchError =
                error as {
                    status?: number
                    statusCode?: number
                }

            const statusCode =
                fetchError.status
                ?? fetchError.statusCode
                ?? 502

            if (
                statusCode === 404
            ) {
                throw createError({
                    statusCode: 404,
                    statusMessage:
                        'Media not found.',
                })
            }

            throw createError({
                statusCode,
                statusMessage:
                    'MediaService request failed.',
            })
        }
    },
)