<script setup lang="ts">
import { useInfiniteQuery } from "@tanstack/vue-query";
import { computed } from "vue";

import { api } from "@/api/client";
import LeaguePicker from "@/components/LeaguePicker.vue";
import MatchRow from "@/components/MatchRow.vue";
import { useSeason } from "@/composables/useSeason";

const { current } = useSeason();
const { data, isLoading, error, hasNextPage, isFetchingNextPage, fetchNextPage } = useInfiniteQuery(
    {
        queryKey: computed(() => ["fixtures", current.value?.id] as const),
        enabled: computed(() => current.value !== null),
        initialPageParam: null as string | null,
        queryFn: ({ pageParam, queryKey }) =>
            pageParam === null ? api.fixtures(queryKey[1]!, undefined, 20) : api.page(pageParam),
        getNextPageParam: (lastPage) => lastPage.nextLink ?? undefined,
    },
);

// Query owns all pages under the season key. Returning to a league restores its own pages; a late
// response from the previous league cannot append matches to the one currently on screen.
const matches = computed(() => data.value?.pages.flatMap((page) => page.value) ?? []);
</script>

<template>
    <section>
        <LeaguePicker />
        <h1>Fixtures</h1>

        <p v-if="isLoading">Loading.</p>
        <p v-else-if="error" role="alert">The fixture list could not be loaded.</p>

        <template v-else>
            <MatchRow v-for="match in matches" :key="match.id" :match="match" />

            <div class="more">
                <button
                    v-if="hasNextPage"
                    type="button"
                    :disabled="isFetchingNextPage"
                    @click="fetchNextPage()"
                >
                    {{ isFetchingNextPage ? "Loading" : "Show more" }}
                </button>
                <p v-else class="end">That is the whole list.</p>
            </div>
        </template>
    </section>
</template>

<style scoped>
h1 {
    font-size: var(--text-xl);
    margin: 16px 12px 12px;
}

.more {
    padding: 16px 12px;
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

.end {
    margin: 0;
    color: var(--muted);
    font-size: var(--text-sm);
}
</style>
