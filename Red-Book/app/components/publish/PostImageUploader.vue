<script setup lang="ts">
import type Uppy from '@uppy/core'
import type { UppyFile } from '@uppy/core'

import type { MediaUploadResponse } from '~~/shared/types/media'
import type { UploadMeta } from '~/composables/useImageUploader'

type UploadFile = UppyFile<UploadMeta, MediaUploadResponse>
type UploadStatus = 'idle' | 'uploading' | 'complete' | 'error'

interface Props {
    uppy: Uppy<UploadMeta, MediaUploadResponse>
    disabled?: boolean
    maxFiles?: number
}

const props = withDefaults(
    defineProps<Props>(),
    {
        disabled: false,
        maxFiles: 9,
    },
)

const fileInput = ref<HTMLInputElement | null>(null)
const files = shallowRef<UploadFile[]>([])
const uploadProgress = ref(0)
const uploadStatus = ref<UploadStatus>('idle')
const dragActive = ref(false)

const previewUrls = new Map<string, string>()

function refreshFiles(): void {
    files.value = [...props.uppy.getFiles()]
}

function getPreviewUrl(file: UploadFile): string | undefined {
    const existing = previewUrls.get(file.id)

    if (existing) {
        return existing
    }

    if (!(file.data instanceof Blob)) {
        return undefined
    }

    const url = URL.createObjectURL(file.data)

    previewUrls.set(file.id, url)

    return url
}

function revokePreview(fileId: string): void {
    const url = previewUrls.get(fileId)

    if (!url) {
        return
    }

    URL.revokeObjectURL(url)
    previewUrls.delete(fileId)
}

function revokeAllPreviews(): void {
    for (const url of previewUrls.values()) {
        URL.revokeObjectURL(url)
    }

    previewUrls.clear()
}

function openFilePicker(): void {
    if (!props.disabled) {
        fileInput.value?.click()
    }
}

function addFiles(selectedFiles: FileList | File[]): void {
    if (props.disabled) {
        return
    }

    for (const file of Array.from(selectedFiles)) {
        try {
            props.uppy.addFile({
                source: 'PostImageUploader',
                name: file.name,
                type: file.type,
                data: file,
            })
        }
        catch (error: unknown) {
            console.warn('⚠️ [IMAGE UPLOADER] 无法添加图片', {
                fileName: file.name,
                error,
            })
        }
    }
}

function handleFileInput(event: Event): void {
    const input = event.target as HTMLInputElement

    const selectedFiles = input.files
        ? Array.from(input.files)
        : []

    // 复制完 File 对象以后再清空，
    // 这样仍然允许下一次选择相同文件。
    input.value = ''

    if (selectedFiles.length === 0) {
        return
    }

    addFiles(selectedFiles)
}

function handleDragEnter(event: DragEvent): void {
    event.preventDefault()

    if (!props.disabled) {
        dragActive.value = true
    }
}

function handleDragOver(event: DragEvent): void {
    event.preventDefault()

    if (props.disabled) {
        return
    }

    if (event.dataTransfer) {
        event.dataTransfer.dropEffect = 'copy'
    }

    dragActive.value = true
}

function handleDragLeave(event: DragEvent): void {
    event.preventDefault()
    dragActive.value = false
}

function handleDrop(event: DragEvent): void {
    event.preventDefault()
    dragActive.value = false

    if (props.disabled) {
        return
    }

    const droppedFiles = event.dataTransfer?.files

    if (!droppedFiles?.length) {
        return
    }

    addFiles(droppedFiles)
}

function removeFile(fileId: string): void {
    if (props.disabled) {
        return
    }

    revokePreview(fileId)
    props.uppy.removeFile(fileId)
}

function handleFileAdded(): void {
    refreshFiles()
    uploadStatus.value = 'idle'
}

function handleFileRemoved(file: UploadFile): void {
    revokePreview(file.id)
    refreshFiles()

    if (files.value.length === 0) {
        uploadProgress.value = 0
        uploadStatus.value = 'idle'
    }
}

function handleProgress(progress: number): void {
    uploadProgress.value = progress
}

function handleUpload(): void {
    uploadStatus.value = 'uploading'
}

function handleComplete(): void {
    uploadProgress.value = 100
    uploadStatus.value = 'complete'

    refreshFiles()
}

function handleUploadError(): void {
    uploadStatus.value = 'error'
}

onMounted(() => {
    refreshFiles()

    props.uppy.on('file-added', handleFileAdded)
    props.uppy.on('file-removed', handleFileRemoved)
    props.uppy.on('progress', handleProgress)
    props.uppy.on('upload', handleUpload)
    props.uppy.on('complete', handleComplete)
    props.uppy.on('upload-error', handleUploadError)
})

onBeforeUnmount(() => {
    props.uppy.off('file-added', handleFileAdded)
    props.uppy.off('file-removed', handleFileRemoved)
    props.uppy.off('progress', handleProgress)
    props.uppy.off('upload', handleUpload)
    props.uppy.off('complete', handleComplete)
    props.uppy.off('upload-error', handleUploadError)

    revokeAllPreviews()
})
</script>

<template>
    <section class="space-y-4">
        <header class="flex items-start justify-between gap-4">
            <div>
                <h2 class="font-medium">
                    图片
                </h2>

                <p class="mt-1 text-xs text-muted">
                    JPEG、PNG、WebP，单张最大 10 MB，最多
                    {{ props.maxFiles }} 张
                </p>
            </div>

            <span class="text-sm text-muted">
                {{ files.length }}/{{ props.maxFiles }}
            </span>
        </header>

        <input ref="fileInput" type="file" accept="image/jpeg,image/png,image/webp" multiple class="hidden"
            :disabled="props.disabled" @change="handleFileInput">

        <button type="button" class="
                flex min-h-44 w-full
                flex-col items-center justify-center gap-3
                rounded-xl border border-dashed border-default
                bg-elevated/30 px-6 py-8 text-center
                transition hover:bg-elevated/60
            " :class="{
                'border-primary bg-primary/5': dragActive,
                'cursor-not-allowed opacity-60': props.disabled,
                'cursor-pointer': !props.disabled,
            }" :disabled="props.disabled" @click="openFilePicker" @dragenter="handleDragEnter"
            @dragover="handleDragOver" @dragleave="handleDragLeave" @drop="handleDrop">
            <div class="
                    flex size-12 items-center justify-center
                    rounded-full bg-elevated
                ">
                <UIcon name="i-lucide-upload" class="size-6 text-muted" />
            </div>

            <div>
                <p class="font-medium">
                    选择图片或拖拽到这里
                </p>

                <p class="mt-1 text-sm text-muted">
                    支持 JPEG、PNG、WebP
                </p>
            </div>
        </button>

        <div v-if="files.length > 0" class="
                grid grid-cols-2 gap-3
                sm:grid-cols-3
            ">
            <article v-for="file in files" :key="file.id" class="
                    group relative aspect-square
                    overflow-hidden rounded-xl
                    border border-default bg-elevated
                ">
                <img v-if="getPreviewUrl(file)" :src="getPreviewUrl(file)" :alt="file.name"
                    class="h-full w-full object-cover">

                <div v-else class="
                        flex h-full
                        items-center justify-center
                    ">
                    <UIcon name="i-lucide-image" class="size-8 text-muted" />
                </div>

                <div class="
                        absolute inset-x-0 bottom-0
                        truncate bg-black/55
                        px-2 py-1.5
                        text-xs text-white
                    ">
                    {{ file.name }}
                </div>

                <UButton class="
                        absolute right-2 top-2
                        opacity-0 transition-opacity
                        group-hover:opacity-100
                    " icon="i-lucide-x" color="neutral" variant="solid" size="xs" square :disabled="props.disabled"
                    :aria-label="`删除图片 ${file.name}`" @click.stop="removeFile(file.id)" />
            </article>
        </div>

        <div v-if="uploadStatus === 'uploading'" class="space-y-2">
            <div class="
                    flex justify-between
                    text-xs text-muted
                ">
                <span>正在上传图片</span>
                <span>{{ uploadProgress }}%</span>
            </div>

            <UProgress :model-value="uploadProgress" />
        </div>

        <UAlert v-if="uploadStatus === 'error'" color="error" variant="soft" icon="i-lucide-circle-alert" title="图片上传失败"
            description="请检查网络后重新尝试。" />
    </section>
</template>