import { createServer } from 'node:http'

const id = '11111111-1111-1111-1111-111111111111'
const post = {
    id, authorUserId: id, author: { userId: id, nickname: '测试作者', avatarUrl: null },
    title: '架构测试笔记', content: '验证搜索和评论链路。', mediaIds: [], media: [],
    tags: ['Vue'], likeCount: 0, commentCount: 0, isLiked: false, isFavorited: false,
    createdAtUtc: '2026-10-01T00:00:00Z', updatedAtUtc: '2026-10-01T00:00:00Z',
}

createServer((request, response) => {
    const url = new URL(request.url, 'http://localhost:4010')
    response.setHeader('Content-Type', 'application/json')
    if (url.pathname === '/health') {
        response.end('{}')
    }
    else if (url.pathname === '/api/v1/posts/search') {
        response.end(JSON.stringify({ page: 1, pageSize: 20, totalCount: 1, items: [post] }))
    }
    else if (url.pathname.endsWith('/comments') && request.method === 'GET') {
        response.end(JSON.stringify({ page: 1, pageSize: 50, totalCount: 0, items: [] }))
    }
    else {
        response.statusCode = 401
        response.end(JSON.stringify({ title: 'Unauthorized' }))
    }
}).listen(4010, '127.0.0.1')
