<script setup lang="ts">
import { computed } from "vue";

import type { MatchEventView } from "@/api/contracts";

const props = defineProps<{
    timeline: MatchEventView[];
    homeTeamId: string;
    homeShortName: string;
    awayShortName: string;
}>();

const readable: Record<string, string> = {
    Goal: "Goal",
    OwnGoal: "Own goal",
    PenaltyGoal: "Penalty",
    Yellow: "Yellow card",
    Red: "Red card",
    Substitution: "Substitution",
};

/**
 * The periods, named the way the ground announces them.
 *
 * A reader who sees "Period ended 48'" followed by "Period started 46'" thinks something is broken.
 * Nothing is: the log is in sequence order and the first half ran to three minutes of stoppage. Half
 * time and full time say the same thing without the arithmetic, so the minute goes away with them.
 */
const periods = ["Kick-off", "Half-time", "Second half", "Full-time"];

const rows = computed(() => {
    let period = 0;

    return props.timeline.map((entry) => {
        const isPeriod = entry.kind === "PeriodStart" || entry.kind === "PeriodEnd";
        const label = isPeriod
            ? (periods[period++] ?? "Period")
            : (readable[entry.kind] ?? entry.kind);

        return {
            entry,
            label,
            isPeriod,
            isGoal: entry.kind === "Goal" || entry.kind === "PenaltyGoal",
            side: entry.teamId === props.homeTeamId ? props.homeShortName : props.awayShortName,
        };
    });
});
</script>

<template>
    <ol class="timeline">
        <li
            v-for="row in rows"
            :key="row.entry.sequence"
            class="entry"
            :class="{ period: row.isPeriod }"
        >
            <span class="minute tabular">{{ row.isPeriod ? "" : `${row.entry.minute}'` }}</span>

            <span class="kind" :class="{ goal: row.isGoal, own: row.entry.kind === 'OwnGoal' }">
                {{ row.label }}
            </span>

            <span class="who">
                {{ row.entry.player ?? "" }}
                <span v-if="row.entry.replaced" class="second">for {{ row.entry.replaced }}</span>
                <span v-else-if="row.entry.assist" class="second"
                    >assist {{ row.entry.assist }}</span
                >
            </span>

            <span class="side">{{ row.isPeriod ? "" : row.side }}</span>
        </li>
    </ol>
</template>

<style scoped>
.timeline {
    list-style: none;
    margin: 0;
    padding: 0;
}

.entry {
    display: grid;
    grid-template-columns: 2.5rem 7rem 1fr auto;
    gap: var(--gap);
    align-items: baseline;
    padding: 8px 12px;
    border-bottom: 1px solid var(--line);
}

.entry.period {
    background: var(--surface);
}

.entry.period .kind {
    color: var(--muted);
    font-size: var(--text-xs);
    text-transform: uppercase;
    letter-spacing: 0.06em;
}

.minute {
    color: var(--muted);
}

.kind.goal {
    color: var(--accent);
    font-weight: 600;
}

/* An own goal counts, but it is nobody's good news. It gets the weight without the colour. */
.kind.own {
    font-weight: 600;
}

.second {
    color: var(--muted);
    font-size: var(--text-sm);
    margin-left: 6px;
}

.side {
    color: var(--muted);
    font-size: var(--text-sm);
}
</style>
