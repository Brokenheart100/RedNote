<script setup lang="ts">
const { session } = useAdminApi()
</script>
<template>
    <div>
        <h1 class="title">我的账号</h1>
        <p class="subtitle">管理身份和访问权限</p>
        <UCard class="max-w-2xl">
            <dl class="grid gap-5">
                <div>
                    <dt class="muted">邮箱</dt>
                    <dd>{{ session?.email }}</dd>
                </div>
                <div>
                    <dt class="muted">账号 ID</dt>
                    <dd>{{ session?.userId }}</dd>
                </div>
                <div>
                    <dt class="muted">多因素认证</dt>
                    <dd>
                        <UBadge color="success" variant="soft">已通过身份验证器验证</UBadge>
                    </dd>
                </div>
                <div>
                    <dt class="muted">角色</dt>
                    <dd class="flex flex-wrap gap-2 mt-2">
                        <UBadge v-for="role in session?.roles" :key="role" color="neutral" variant="soft">{{ role }}
                        </UBadge>
                    </dd>
                </div>
                <div>
                    <dt class="muted">操作权限</dt>
                    <dd class="flex flex-wrap gap-2 mt-2">
                        <UBadge v-for="permission in session?.permissions" :key="permission" variant="soft">
                            {{ permission }}</UBadge>
                    </dd>
                </div>
            </dl>
        </UCard>
    </div>
</template>
