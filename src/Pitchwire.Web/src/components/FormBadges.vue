<script setup lang="ts">
import type { FormEntry } from "@/api/contracts";

defineProps<{ form: FormEntry[] }>();

const letter = (outcome: string) => outcome.charAt(0).toUpperCase();
</script>

<template>
    <span class="form">
        <span
            v-for="entry in form"
            :key="entry.matchId"
            class="badge"
            :class="entry.outcome.toLowerCase()"
            :title="`${entry.atHome ? 'Home' : 'Away'} against ${entry.opponent.name}, ${entry.goalsFor}-${entry.goalsAgainst}`"
        >
            {{ letter(entry.outcome) }}
        </span>
        <span v-if="form.length === 0" class="none">no matches yet</span>
    </span>
</template>

<style scoped>
.form {
    display: inline-flex;
    gap: 3px;
    align-items: center;
}

.badge {
    width: 1.15rem;
    height: 1.15rem;
    display: grid;
    place-items: center;
    border-radius: 3px;
    font-size: var(--text-xs);
    font-weight: 700;
    color: var(--surface);
}

.won {
    background: var(--accent);
}

.drawn {
    background: var(--muted);
}

.lost {
    background: var(--warn);
}

.none {
    color: var(--muted);
    font-size: var(--text-xs);
}
</style>
