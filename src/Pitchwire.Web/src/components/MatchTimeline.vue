<script setup lang="ts">
import type { MatchEventView } from "@/api/contracts";

defineProps<{ timeline: MatchEventView[]; homeTeamId: string }>();

const readable: Record<string, string> = {
    Goal: "Goal",
    OwnGoal: "Own goal",
    PenaltyGoal: "Penalty",
    Yellow: "Yellow card",
    Red: "Red card",
    Substitution: "Substitution",
    PeriodStart: "Period started",
    PeriodEnd: "Period ended",
};
</script>

<template>
    <ol class="timeline">
        <li v-for="entry in timeline" :key="entry.sequence" class="entry">
            <span class="minute tabular">{{ entry.minute }}'</span>
            <span class="kind" :class="{ goal: entry.kind.endsWith('Goal') }">
                {{ readable[entry.kind] ?? entry.kind }}
            </span>
            <span class="who">
                {{ entry.player ?? "" }}
                <span v-if="entry.assist" class="assist">assist {{ entry.assist }}</span>
            </span>
            <span class="side">{{ entry.teamId === homeTeamId ? "home" : "away" }}</span>
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
    grid-template-columns: 3rem 8rem 1fr auto;
    gap: var(--gap);
    align-items: baseline;
    padding: 8px 12px;
    border-bottom: 1px solid var(--line);
}

.minute {
    color: var(--muted);
}

.kind.goal {
    color: var(--accent);
    font-weight: 600;
}

.assist {
    color: var(--muted);
    font-size: var(--text-sm);
    margin-left: 6px;
}

.side {
    color: var(--muted);
    font-size: var(--text-sm);
}
</style>
