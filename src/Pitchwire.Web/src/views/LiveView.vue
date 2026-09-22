<script setup lang="ts">
import { useQuery } from "@tanstack/vue-query";
import { computed } from "vue";

import { api } from "@/api/client";
import type { MatchSummary } from "@/api/contracts";
import LeaguePicker from "@/components/LeaguePicker.vue";
import MatchRow from "@/components/MatchRow.vue";
import { useLiveFeed } from "@/composables/useLiveFeed";
import { useLeagueStore } from "@/stores/league";

const { data, isLoading, error } = useQuery({
    queryKey: ["live"],
    queryFn: () => api.live(),
    // The socket keeps this fresh. The interval is a safety net for a browser whose connection went
    // away without saying so, not the way the list is meant to move.
    refetchInterval: 60_000,
});

const { connected, lastGoalAt } = useLiveFeed();
const chosen = useLeagueStore();

const matches = computed(() => data.value?.value ?? []);

/**
 * Grouped by competition, the way a results page has always done it.
 *
 * Six rows in a row with no heading could be one league or six, and on an evening when three
 * countries are playing at once that is the difference between a list and a jumble.
 *
 * The chosen league comes first. Nothing is hidden: a live board that leaves out a match is not a
 * live board, so the rest of the evening follows underneath in alphabetical order.
 */
const byLeague = computed(() => {
    const groups = new Map<
        string,
        { id: string; name: string; country: string; matches: MatchSummary[] }
    >();

    for (const match of matches.value) {
        const group = groups.get(match.league.id) ?? {
            id: match.league.id,
            name: match.league.name,
            country: match.league.country,
            matches: [],
        };

        group.matches.push(match);
        groups.set(match.league.id, group);
    }

    return [...groups.values()].sort((left, right) => {
        const mine = (group: { id: string }) => (group.id === chosen.leagueId ? 0 : 1);

        return mine(left) - mine(right) || left.name.localeCompare(right.name);
    });
});

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
                <span class="dot" aria-hidden="true"></span>
                {{ connected ? "Updating live" : "Not connected, refreshing on a timer" }}
            </p>
        </header>

        <LeaguePicker />

        <p v-if="isLoading" class="state">Loading.</p>
        <p v-else-if="error" class="state" role="alert">The live list could not be loaded.</p>
        <p v-else-if="matches.length === 0" class="state">Nothing is being played right now.</p>

        <template v-else>
            <div v-for="group in byLeague" :key="group.id" class="group">
                <h2>
                    {{ group.name }}
                    <span class="country">{{ group.country }}</span>
                </h2>

                <MatchRow
                    v-for="match in group.matches"
                    :key="match.id"
                    :match="match"
                    :just-scored="recentlyScored(match.id)"
                />
            </div>
        </template>

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

h2 {
    display: flex;
    align-items: baseline;
    justify-content: space-between;
    gap: var(--gap);
    margin: 0;
    padding: 10px 12px 8px;
    font-size: var(--text-xs);
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.06em;
    background: var(--surface);
    border-top: 1px solid var(--line);
    border-bottom: 1px solid var(--line);
}

.country {
    color: var(--muted);
    font-weight: 500;
    text-transform: none;
    letter-spacing: 0;
}

.status {
    display: flex;
    align-items: center;
    gap: 6px;
    font-size: var(--text-sm);
    color: var(--muted);
}

.dot {
    width: 7px;
    height: 7px;
    border-radius: 50%;
    background: var(--muted);
}

.status.on {
    color: var(--accent);
}

.status.on .dot {
    background: var(--accent);
}

.state {
    color: var(--muted);
    font-size: var(--text-sm);
    padding: 16px 12px;
    margin: 0;
}
</style>
