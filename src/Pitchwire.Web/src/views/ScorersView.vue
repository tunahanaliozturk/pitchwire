<script setup lang="ts">
import { useQuery } from "@tanstack/vue-query";
import { computed } from "vue";

import { api } from "@/api/client";
import LeaguePicker from "@/components/LeaguePicker.vue";
import { useSeason } from "@/composables/useSeason";

const { current } = useSeason();

const { data, isLoading } = useQuery({
    queryKey: computed(() => ["scorers", current.value?.id]),
    enabled: computed(() => current.value !== null),
    queryFn: () => api.scorers(current.value!.id, 20),
});

const scorers = computed(() => data.value ?? []);
</script>

<template>
    <section>
        <LeaguePicker />
        <h1>Scorers</h1>

        <p v-if="isLoading" class="state">Loading.</p>
        <p v-else-if="scorers.length === 0" class="state">Nobody has scored in this league yet.</p>

        <template v-else>
            <p class="heads">
                <span>Assists</span>
                <span>Goals</span>
            </p>

            <ol class="list">
                <li v-for="(row, index) in scorers" :key="row.playerId">
                    <span class="rank tabular">{{ index + 1 }}</span>
                    <span class="who">
                        <span class="name">{{ row.player }}</span>
                        <span class="team">{{ row.team.name }}</span>
                    </span>
                    <span class="assists tabular" :title="`${row.assists} assists`">{{
                        row.assists
                    }}</span>
                    <span class="goals tabular">{{ row.goals }}</span>
                </li>
            </ol>

            <p class="legend">Own goals credit nobody.</p>
        </template>
    </section>
</template>

<style scoped>
h1 {
    font-size: var(--text-xl);
    margin: 16px 12px 12px;
}

.list {
    list-style: none;
    margin: 0;
    padding: 0;
}

li {
    display: grid;
    grid-template-columns: 2rem 1fr 2.5rem 2.5rem;
    align-items: center;
    gap: var(--gap);
    padding: 9px 12px;
    border-bottom: 1px solid var(--line);
}

.rank {
    color: var(--muted);
    font-size: var(--text-sm);
}

.who {
    display: grid;
    min-width: 0;
}

.name {
    font-weight: 500;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
}

.team {
    color: var(--muted);
    font-size: var(--text-xs);
}

.assists {
    text-align: right;
    color: var(--muted);
    font-size: var(--text-sm);
}

.goals {
    text-align: right;
    font-weight: 700;
    color: var(--accent);
}

.state,
.heads {
    display: flex;
    justify-content: flex-end;
    gap: 24px;
    margin: 0;
    padding: 0 12px 4px;
    color: var(--muted);
    font-size: var(--text-xs);
    text-transform: uppercase;
    letter-spacing: 0.06em;
}

.heads span {
    width: 2.5rem;
    text-align: right;
}

.legend {
    color: var(--muted);
    font-size: var(--text-sm);
    padding: 16px 12px;
    margin: 0;
}
</style>
