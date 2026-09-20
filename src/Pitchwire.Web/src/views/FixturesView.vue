<script setup lang="ts">
import { useQuery } from "@tanstack/vue-query";
import { computed, ref } from "vue";

import { api } from "@/api/client";
import type { MatchSummary } from "@/api/contracts";
import MatchRow from "@/components/MatchRow.vue";
import { useSeason } from "@/composables/useSeason";

const { current } = useSeason();
const pages = ref<MatchSummary[]>([]);
const nextLink = ref<string | null>(null);
const loadingMore = ref(false);

const { isLoading, error } = useQuery({
    queryKey: computed(() => ["fixtures", current.value?.id]),
    enabled: computed(() => current.value !== null),
    queryFn: async () => {
        const page = await api.fixtures(current.value!.id, undefined, 20);
        pages.value = page.value;
        nextLink.value = page.nextLink;
        return page;
    },
});

// Follows the link the server gave, rather than building the next request here. The token is opaque
// on purpose: what is inside it is the server's business and it has changed once already.
const more = async () => {
    if (nextLink.value === null || loadingMore.value) {
        return;
    }

    loadingMore.value = true;

    try {
        const page = await api.page(nextLink.value);
        pages.value = [...pages.value, ...page.value];
        nextLink.value = page.nextLink;
    } finally {
        loadingMore.value = false;
    }
};
</script>

<template>
    <section>
        <h1>Fixtures</h1>

        <p v-if="isLoading">Loading.</p>
        <p v-else-if="error" role="alert">The fixture list could not be loaded.</p>

        <template v-else>
            <MatchRow v-for="match in pages" :key="match.id" :match="match" />

            <div class="more">
                <button v-if="nextLink" type="button" :disabled="loadingMore" @click="more">
                    {{ loadingMore ? "Loading" : "Show more" }}
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
