<script setup lang="ts">
import { computed } from "vue";

const props = defineProps<{ label: string; home: number; away: number }>();

// A bar that always fills is a bar that says nothing when both sides are level. Nil against nil is
// shown as an even split rather than an empty row.
const total = computed(() => props.home + props.away);
const homeShare = computed(() =>
    total.value === 0 ? 50 : Math.round((props.home / total.value) * 100),
);
</script>

<template>
    <div class="row">
        <span class="value tabular">{{ home }}</span>
        <span class="middle">
            <span class="label">{{ label }}</span>
            <span class="track" aria-hidden="true">
                <span class="fill home" :style="{ width: `${homeShare}%` }"></span>
                <span class="fill away" :style="{ width: `${100 - homeShare}%` }"></span>
            </span>
        </span>
        <span class="value tabular">{{ away }}</span>
    </div>
</template>

<style scoped>
.row {
    display: grid;
    grid-template-columns: 2.5rem 1fr 2.5rem;
    align-items: center;
    gap: var(--gap);
    padding: 8px 0;
}

.value {
    font-weight: 600;
    text-align: center;
}

.middle {
    display: grid;
    gap: 5px;
}

.label {
    text-align: center;
    font-size: var(--text-xs);
    text-transform: uppercase;
    letter-spacing: 0.06em;
    color: var(--muted);
}

.track {
    display: flex;
    height: 5px;
    border-radius: 3px;
    overflow: hidden;
    background: var(--line);
}

.fill.home {
    background: var(--accent);
}

.fill.away {
    background: var(--muted);
}
</style>
