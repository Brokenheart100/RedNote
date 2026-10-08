<script setup lang="ts">
interface Entry { id: string; source: string; actorUserId: string; targetId: string; action: string; reason: string; change: string; createdAtUtc: string; traceId: string }
const { request, session } = useAdminApi(); if (!session.value?.permissions.includes('audit.read')) throw createError({ statusCode: 403 })
const source = ref('audit'); const actor = ref(''); const target = ref(''); const action = ref(''); const from = ref(''); const to = ref(''); const page = ref(1); const items = ref<Entry[]>([]); const total = ref(0); const error = ref('')
async function load() { const query: Record<string, string | number> = { page: page.value }; for (const [key, value] of Object.entries({ actor: actor.value, target: target.value, action: action.value, from: from.value ? new Date(from.value).toISOString() : '', to: to.value ? new Date(to.value).toISOString() : '' })) if (value) query[key] = value; try { const data = await request<{ items: Entry[]; total: number }>(source.value, { query }); items.value = data.items; total.value = data.total; error.value = '' } catch { error.value = '查询失败，请检查筛选条件。' } }
watch(source, () => { page.value = 1; load() }); onMounted(load)
</script>
<template>
    <div>
        <h1 class="title">操作审计</h1>
        <p class="subtitle">集中查询内容和用户操作审计。新记录通过事件同步，可能短暂延迟。</p>
        <div class="toolbar">
            <USelect v-model="source"
                :items="[{ label: '全部来源', value: 'audit' }, { label: '内容操作', value: 'content-audit' }, { label: '用户操作', value: 'user-audit' }]" />
            <UInput v-model="actor" placeholder="操作者 ID" />
            <UInput v-model="target" placeholder="目标 ID" />
            <UInput v-model="action" placeholder="操作，例如 post.hide" />
            <UInput v-model="from" type="datetime-local" aria-label="开始时间" />
            <UInput v-model="to" type="datetime-local" aria-label="结束时间" />
            <UButton label="查询" @click="page = 1; load()" />
        </div>
        <p v-if="error" class="error">{{ error }}</p>
        <div class="table-wrap">
            <table>
                <thead>
                    <tr>
                        <th>时间 / 操作</th>
                        <th>操作者 / 目标</th>
                        <th>原因</th>
                        <th>变更结果</th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="entry in items" :key="`${entry.source}:${entry.id}`">
                        <td>
                            <div class="font-semibold">{{ entry.action }}</div>
                            <div class="muted">{{ entry.source === 'content' ? '内容服务' : '用户服务' }}</div>
                            <div class="muted">{{ new Date(entry.createdAtUtc).toLocaleString() }}</div>
                        </td>
                        <td class="muted">{{ entry.actorUserId }}<br>{{ entry.targetId }}</td>
                        <td>{{ entry.reason }}</td>
                        <td>
                            <div class="muted">{{ entry.change }}</div>
                            <div class="muted">{{ entry.traceId }}</div>
                        </td>
                    </tr>
                </tbody>
            </table>
            <div v-if="!items.length" class="empty">没有符合条件的记录</div>
        </div>
        <div class="pagination"><span class="muted">共 {{ total }} 条 · 第 {{ page }} 页</span>
            <div class="actions">
                <UButton label="上一页" color="neutral" :disabled="page <= 1" @click="page--; load()" />
                <UButton label="下一页" color="neutral" :disabled="page * 20 >= total" @click="page++; load()" />
            </div>
        </div>
    </div>
</template>
