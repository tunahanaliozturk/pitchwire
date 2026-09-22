<script setup lang="ts">
import { useQuery } from "@tanstack/vue-query";
import { computed } from "vue";

import { api } from "@/api/client";
import LeaguePicker from "@/components/LeaguePicker.vue";
import { useSeason } from "@/composables/useSeason";

const { current } = useSeason();

const { data, isLoading, error } = useQuery({
    queryKey: computed(() => ["table", current.value?.id]),
    enabled: computed(() => current.value !== null),
    queryFn: () => api.table(current.value!.id),
});

const rows = computed(() => data.value ?? []);
</script>

<template>
    <section>
        <LeaguePicker />
        <h1>{{ current?.league ?? "Table" }}</h1>

        <p v-if="isLoading">Loading.</p>
        <p v-else-if="error" role="alert">The table could not be loaded.</p>

        <table v-else class="table">
            <caption class="visually-hidden">
                League table, ordered by points and then goal difference
            </caption>
            <thead>
                <tr>
                    <th scope="col" class="narrow">#</th>
                    <th scope="col" class="left">Team</th>
                    <th scope="col" class="narrow">P</th>
                    <th scope="col" class="narrow">W</th>
                    <th scope="col" class="narrow">D</th>
                    <th scope="col" class="narrow">L</th>
                    <th scope="col" class="narrow">GD</th>
                    <th scope="col" class="narrow">Pts</th>
                </tr>
            </thead>
            <tbody>
                <tr v-for="row in rows" :key="row.team.id">
                    <td class="narrow tabular">{{ row.position }}</td>
                    <th scope="row" class="team">{{ row.team.name }}</th>
                    <td class="narrow tabular">{{ row.played }}</td>
                    <td class="narrow tabular">{{ row.won }}</td>
                    <td class="narrow tabular">{{ row.drawn }}</td>
                    <td class="narrow tabular">{{ row.lost }}</td>
                    <td class="narrow tabular">{{ row.goalDifference }}</td>
                    <td class="narrow tabular points">{{ row.points }}</td>
                </tr>
            </tbody>
        </table>
    </section>
</template>

<style scoped>
h1 {
    font-size: var(--text-xl);
    margin: 16px 12px 12px;
}

.table {
    width: 100%;
    border-collapse: collapse;
    font-size: var(--text-sm);
}

th,
td {
    padding: 8px 10px;
    border-bottom: 1px solid var(--line);
    text-align: right;
}

thead th {
    color: var(--muted);
    font-weight: 500;
}

.team {
    text-align: left;
    font-weight: 500;
}

.left {
    text-align: left;
}

.narrow {
    width: 2.75rem;
}

.points {
    font-weight: 700;
}
</style>
