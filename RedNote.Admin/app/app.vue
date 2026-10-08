<script setup lang="ts">
const route=useRoute();const {session,request}=useAdminApi()
const permissions=computed(()=>session.value?.permissions??[])
async function logout(){await request('logout',{method:'POST'});session.value=null;await navigateTo('/login')}
</script>
<template><UApp><NuxtPage v-if="route.path==='/login'"/><div v-else class="shell"><aside class="sidebar"><div class="brand"><span>RedNote</span> Console</div><nav><NuxtLink to="/">工作台</NuxtLink><NuxtLink v-if="permissions.includes('content.moderate')" to="/content">内容管理</NuxtLink><NuxtLink v-if="permissions.includes('users.restrict')" to="/users">用户管理</NuxtLink><NuxtLink v-if="permissions.includes('audit.read')" to="/audit">操作审计</NuxtLink><NuxtLink to="/account">我的账号</NuxtLink></nav></aside><main class="min-w-0"><header class="topbar"><span>管理后台</span><div class="actions"><span class="muted">{{session?.email}}</span><UButton label="退出登录" color="neutral" variant="ghost" @click="logout"/></div></header><div class="workspace"><NuxtPage/></div></main></div></UApp></template>
