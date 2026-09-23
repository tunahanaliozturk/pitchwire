<script setup lang="ts">
import { useInfiniteQuery } from "@tanstack/vue-query";
import { computed } from "vue";

import { api } from "@/api/client";
import type { MatchSummary } from "@/api/contracts";
import LeaguePicker from "@/components/LeaguePicker.vue";
import MatchRow from "@/components/MatchRow.vue";
import { useSeason } from "@/composables/useSeason";

const props = defineProps<{ kind: "fixtures" | "results" }>();
const { current, isLoading: seasonLoading } = useSeason();

const {
    data,
    isLoading,
    isLoadingError,
    isFetchNextPageError,
    hasNextPage,
    isFetchingNextPage,
    fetchNextPage,
} = useInfiniteQuery({
    queryKey: computed(() => [props.kind, current.value?.id] as const),
    enabled: computed(() => current.value !== null),
    initialPageParam: null as string | null,
    queryFn: ({ pageParam, queryKey }) =>
        pageParam !== null
            ? api.page(pageParam)
            : queryKey[0] === "fixtures"
              ? api.fixtures(queryKey[1]!, undefined, 20)
              : api.results(queryKey[1]!, 20),
    getNextPageParam: (lastPage) => lastPage.nextLink ?? undefined,
});

// The key owns every page for one kind and one season. A late result cannot appear in another league.
const matches = computed(() => data.value?.pages.flatMap((page) => page.value) ?? []);

const days = computed(() => {
    const groups: { key: string; label: string; matches: MatchSummary[] }[] = [];

    for (const match of matches.value) {
        const kickoff = new Date(match.kickoffUtc);
        const key = kickoff.toDateString();
        let group = groups[groups.length - 1];

        if (group?.key !== key) {
            group = {
                key,
                label: kickoff.toLocaleDateString(undefined, {
                    weekday: "long",
                    day: "numeric",
                    month: "long",
                    year: "numeric",
                }),
                matches: [],
            };
            groups.push(group);
        }

        group.matches.push(match);
    }

    return groups;
});
</script>

<template>
    <section>
        <LeaguePicker />
        <h1>{{ kind === "fixtures" ? "Fixtures" : "Results" }}</h1>

        <p v-if="seasonLoading || isLoading" class="state">Loading.</p>
        <p v-else-if="current === null" class="state">Choose a league to see matches.</p>
        <p v-else-if="isLoadingError" class="state" role="alert">
            The {{ kind === "fixtures" ? "fixture" : "results" }} list could not be loaded.
        </p>

        <template v-else>
            <p v-if="matches.length === 0" class="state">
                {{
                    kind === "fixtures"
                        ? "No upcoming fixtures in this league."
                        : "No results yet for this league."
                }}
            </p>
            <div v-for="day in days" :key="day.key" class="day">
                <h2>{{ day.label }}</h2>
                <MatchRow v-for="match in day.matches" :key="match.id" :match="match" />
            </div>

            <div v-if="hasNextPage || isFetchNextPageError" class="more">
                <p v-if="isFetchNextPageError" role="alert">The next page could not be loaded.</p>
                <button type="button" :disabled="isFetchingNextPage" @click="fetchNextPage()">
                    {{
                        isFetchingNextPage
                            ? "Loading"
                            : isFetchNextPageError
                              ? "Retry"
                              : "Show more"
                    }}
                </button>
            </div>
        </template>
    </section>
</template>

<style scoped>
h1 {
    font-size: var(--text-xl);
    margin: 16px 12px 12px;
}

h2 {
    margin: 0;
    padding: 10px 12px 8px;
    border-top: 1px solid var(--line);
    border-bottom: 1px solid var(--line);
    background: var(--surface);
    color: var(--ink-soft);
    font-size: var(--text-xs);
    font-weight: 700;
}

.state {
    margin: 0;
    padding: 16px 12px;
    color: var(--muted);
    font-size: var(--text-sm);
}

.more {
    padding: 16px 12px;
}

.more p {
    margin: 0 0 8px;
    color: var(--warn);
    font-size: var(--text-sm);
}

button {
    font: inherit;
    padding: 8px 14px;
    border: 1px solid var(--line);
    border-radius: var(--radius);
    background: var(--surface);
    color: var(--ink);
    cursor: pointer;
}

button:disabled {
    color: var(--muted);
    cursor: default;
}
</style>
