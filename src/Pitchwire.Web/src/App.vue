<script setup lang="ts">
import { onMounted, ref } from "vue";

const theme = ref<"light" | "dark" | "system">("system");

// Remembered per browser, and only this. A preference nobody else needs to see and nothing else
// depends on is the one thing local storage is genuinely for.
onMounted(() => {
    try {
        const stored = localStorage.getItem("pitchwire-theme");

        if (stored === "light" || stored === "dark") {
            theme.value = stored;
            document.documentElement.dataset["theme"] = stored;
        }
    } catch {
        // Private windows and blocked site data both throw here, and neither is a reason to fail.
    }
});

const cycle = () => {
    theme.value = theme.value === "dark" ? "light" : "dark";
    document.documentElement.dataset["theme"] = theme.value;

    try {
        localStorage.setItem("pitchwire-theme", theme.value);
    } catch {
        // Nothing is lost that matters: the page still looks the way it was just asked to.
    }
};
</script>

<template>
    <a class="skip visually-hidden" href="#main">Skip to content</a>

    <header class="bar">
        <RouterLink to="/" class="brand">pitchwire</RouterLink>

        <nav aria-label="Sections">
            <RouterLink to="/">Live</RouterLink>
            <RouterLink to="/fixtures">Fixtures</RouterLink>
            <RouterLink to="/table">Table</RouterLink>
            <RouterLink to="/scorers">Scorers</RouterLink>
            <RouterLink to="/settings">Settings</RouterLink>
        </nav>

        <button type="button" class="theme" @click="cycle">
            {{ theme === "dark" ? "Light" : "Dark" }}
        </button>
    </header>

    <main id="main" class="shell">
        <RouterView />
    </main>
</template>

<style scoped>
.bar {
    display: flex;
    align-items: center;
    gap: var(--gap);
    padding: 10px 12px;
    border-bottom: 1px solid var(--line);
    background: var(--surface);
    position: sticky;
    top: 0;
    z-index: 1;
}

.brand {
    font-weight: 700;
    letter-spacing: -0.01em;
}

nav {
    display: flex;
    gap: 4px;
    margin-left: auto;
}

nav a {
    padding: 6px 10px;
    border-radius: var(--radius);
    color: var(--ink-soft);
    font-size: var(--text-sm);
}

nav a.router-link-active {
    background: var(--accent-soft);
    color: var(--accent);
    font-weight: 600;
}

.theme {
    font: inherit;
    font-size: var(--text-sm);
    padding: 6px 10px;
    border: 1px solid var(--line);
    border-radius: var(--radius);
    background: transparent;
    color: var(--ink-soft);
    cursor: pointer;
}

.shell {
    max-width: 760px;
    margin: 0 auto;
    padding-bottom: 48px;
}

.skip:focus {
    position: static;
    width: auto;
    height: auto;
    clip-path: none;
    display: inline-block;
    padding: 8px 12px;
}
</style>
