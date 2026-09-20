<script setup lang="ts">
import { computed } from "vue";

import type { MatchSummary } from "@/api/contracts";

const props = defineProps<{ match: MatchSummary; justScored?: boolean }>();

const live = computed(() => props.match.status === "Live" || props.match.status === "Halftime");

const clock = computed(() => {
    if (props.match.status === "Halftime") {
        return "HT";
    }

    if (props.match.status === "Finished") {
        return "FT";
    }

    if (live.value) {
        return `${props.match.minute}'`;
    }

    // Scheduled: the kick off time in the reader's own zone, which is the only time that means
    // anything to them.
    return new Date(props.match.kickoffUtc).toLocaleTimeString(undefined, {
        hour: "2-digit",
        minute: "2-digit",
    });
});

const label = computed(
    () =>
        `${props.match.home.name} ${props.match.homeScore}, ${props.match.away.name} ${props.match.awayScore}, ${clock.value}`,
);
</script>

<template>
    <RouterLink
        :to="{ name: 'match', params: { matchId: match.id } }"
        class="row"
        :class="{ 'just-scored': justScored }"
        :aria-label="label"
    >
        <span class="clock tabular" :class="{ live }">{{ clock }}</span>

        <span class="teams">
            <span class="team">{{ match.home.name }}</span>
            <span class="team">{{ match.away.name }}</span>
        </span>

        <span class="score tabular" aria-hidden="true">
            <span>{{ match.homeScore }}</span>
            <span>{{ match.awayScore }}</span>
        </span>

        <span
            v-if="match.isDegraded"
            class="degraded"
            title="Some events are missing from this match"
        >
            incomplete
        </span>
    </RouterLink>
</template>

<style scoped>
.row {
    display: grid;
    grid-template-columns: 3.25rem 1fr auto auto;
    gap: var(--gap);
    align-items: center;
    padding: 10px 12px;
    border-bottom: 1px solid var(--line);
}

.row:hover {
    background: var(--surface);
}

.clock {
    color: var(--muted);
    font-size: var(--text-sm);
}

.clock.live {
    color: var(--accent);
    font-weight: 600;
}

.teams,
.score {
    display: grid;
    gap: 2px;
}

.score {
    font-weight: 600;
    text-align: right;
    min-width: 1.5rem;
}

.team {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
}

.degraded {
    font-size: var(--text-sm);
    color: var(--warn);
}
</style>
