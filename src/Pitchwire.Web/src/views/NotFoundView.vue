<script setup lang="ts"></script>

<template>
    <section class="empty">
        <h1>Nothing here</h1>
        <p>That address does not match anything in this service.</p>
        <RouterLink to="/">Back to the live list</RouterLink>
    </section>
</template>

<style scoped>
.empty {
    padding: 48px 12px;
}

h1 {
    font-size: var(--text-xl);
    margin: 0 0 8px;
}

a {
    color: var(--accent);
}
</style>
