<script setup lang="ts">
import { useQuery } from "@tanstack/vue-query";
import { computed } from "vue";

import { api } from "@/api/client";
import MatchRow from "@/components/MatchRow.vue";
import { useLiveFeed } from "@/composables/useLiveFeed";

const { data, isLoading, error } = useQuery({
    queryKey: ["live"],
    queryFn: () => api.live(),
    // The socket keeps this fresh. The interval is a safety net for a browser whose connection went
    // away without saying so, not the way the list is meant to move.
    refetchInterval: 60_000,
});

const { connected, lastGoalAt } = useLiveFeed();

const matches = computed(() => data.value?.value ?? []);

const scoreline = computed(() =>
    matches.value
        .map(
            (match) =>
                `${match.home.shortName} ${match.homeScore} ${match.away.shortName} ${match.awayScore}`,
        )
        .join(". "),
);

const recentlyScored = (matchId: string) => {
    const at = lastGoalAt.value[matchId];
    return at !== undefined && Date.now() - at < 5_000;
};
</script>

<template>
    <section>
        <header class="head">
            <h1>Live</h1>
            <p class="status" :class="{ on: connected }">
                {{ connected ? "Updating live" : "Not connected, refreshing on a timer" }}
            </p>
        </header>

        <p v-if="isLoading">Loading.</p>
        <p v-else-if="error" role="alert">The live list could not be loaded.</p>
        <p v-else-if="matches.length === 0">Nothing is being played right now.</p>

        <div v-else>
            <MatchRow
                v-for="match in matches"
                :key="match.id"
                :match="match"
                :just-scored="recentlyScored(match.id)"
            />
        </div>

        <!--
      A screen reader user learns about a goal from here. Without it the score changes silently and
      the only people who notice are the ones looking at the screen.
    -->
        <p aria-live="polite" class="visually-hidden">{{ scoreline }}</p>
    </section>
</template>

<style scoped>
.head {
    display: flex;
    align-items: baseline;
    justify-content: space-between;
    gap: var(--gap);
    padding: 0 12px;
}

h1 {
    font-size: var(--text-xl);
    margin: 16px 0 12px;
}

.status {
    font-size: var(--text-sm);
    color: var(--muted);
}

.status.on {
    color: var(--accent);
}
</style>
