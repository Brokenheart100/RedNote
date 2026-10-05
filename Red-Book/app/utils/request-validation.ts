import { RequestValidationError } from '../../shared/schemas/requests.ts'

const labels: Record<string, string> = {
    email: '邮箱', password: '密码', displayName: '显示名称', familyName: '姓氏',
    title: '标题', content: '正文', mediaIds: '图片', tags: '标签', nickname: '昵称',
    avatarUrl: '头像地址', bio: '个人简介', parentCommentId: '回复对象', q: '搜索关键词',
}

export function getRequestValidationMessage(error: unknown): string | undefined {
    if (!(error instanceof RequestValidationError)) return undefined
    if (error.field === 'email') return '请输入有效的邮箱地址。'
    if (error.field === 'avatarUrl') return '头像地址无效，请重新上传图片或使用 HTTP/HTTPS 地址。'
    const label = labels[error.field] ?? '输入内容'
    const limits = error.issue?.code === 'custom' ? error.issue.params : undefined
    if (typeof limits?.maximum === 'number') {
        return limits.minimum === 1
            ? `${label}不能为空，且不能超过 ${limits.maximum} 个字符。`
            : `${label}不能超过 ${limits.maximum} 个字符。`
    }
    if (error.field === 'mediaIds') return '最多上传 9 张图片，请确认图片上传成功。'
    if (error.field === 'tags') return '最多添加 10 个标签，每个标签不能为空且不超过 30 个字符。'
    return `${label}格式不正确，请检查后重试。`
}
