<script setup lang="ts">
import { computed } from "vue";

const props = defineProps<{ rating: number | null }>();

// Three bands rather than a gradient. A colour that changes by a tenth of a point implies a precision
// the model does not have.
const band = computed(() => {
    if (props.rating === null) {
        return "none";
    }

    return props.rating >= 7.5 ? "high" : props.rating >= 6 ? "fair" : "low";
});
</script>

<template>
    <span
        class="chip tabular"
        :class="band"
        :title="rating === null ? 'Played too little to rate' : undefined"
    >
        {{ rating === null ? "–" : rating.toFixed(1) }}
    </span>
</template>

<style scoped>
.chip {
    display: inline-block;
    min-width: 2.4rem;
    text-align: center;
    padding: 2px 6px;
    border-radius: var(--radius);
    font-size: var(--text-sm);
    font-weight: 600;
    border: 1px solid transparent;
}

.high {
    background: var(--accent-soft);
    color: var(--accent);
    border-color: color-mix(in srgb, var(--accent) 35%, transparent);
}

.fair {
    background: var(--paper);
    color: var(--ink-soft);
    border-color: var(--line);
}

.low {
    background: var(--paper);
    color: var(--warn);
    border-color: color-mix(in srgb, var(--warn) 35%, transparent);
}

.none {
    color: var(--muted);
    border-color: var(--line);
    font-weight: 500;
}
</style>
