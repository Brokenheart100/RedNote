<script setup lang="ts">
const {session}=useAdminApi()
const entries=computed(()=>[{permission:'content.moderate',title:'内容管理',text:'查看帖子与评论，处理下架和恢复。',to:'/content',icon:'i-lucide-file-check'},{permission:'users.restrict',title:'用户管理',text:'管理社区发布和评论资格。',to:'/users',icon:'i-lucide-users'},{permission:'audit.read',title:'操作审计',text:'追查管理操作、处理原因和结果。',to:'/audit',icon:'i-lucide-history'}].filter(item=>session.value?.permissions.includes(item.permission)))
</script>
<template><div><h1 class="title">工作台</h1><p class="subtitle">{{session?.email}}，请选择需要处理的工作。</p><div class="grid gap-5 md:grid-cols-3"><UCard v-for="item in entries" :key="item.to"><UIcon :name="item.icon" class="size-8 text-rose-600 mb-4"/><h2 class="text-lg font-semibold">{{item.title}}</h2><p class="text-sm text-slate-500 my-3">{{item.text}}</p><UButton :to="item.to" label="进入" variant="soft"/></UCard></div></div></template>
