<script setup lang="ts">
import { useQuery } from "@tanstack/vue-query";
import { computed, toRef } from "vue";

import { api } from "@/api/client";
import MatchTimeline from "@/components/MatchTimeline.vue";

const props = defineProps<{ matchId: string }>();

// A getter rather than the value. Watching the value would pin this query to whatever the parameter
// was when the component was created, and moving between two matches would show the first one twice.
const matchId = toRef(props, "matchId");

const { data, isLoading, error } = useQuery({
    queryKey: computed(() => ["match", matchId.value]),
    queryFn: () => api.match(matchId.value),
    refetchInterval: 30_000,
});

const match = computed(() => data.value?.match ?? null);
</script>

<template>
    <section>
        <p v-if="isLoading">Loading.</p>
        <p v-else-if="error" role="alert">That match could not be loaded.</p>

        <template v-else-if="match && data">
            <header class="head">
                <h1>
                    {{ match.home.name }}
                    <span class="tabular">{{ match.homeScore }} - {{ match.awayScore }}</span>
                    {{ match.away.name }}
                </h1>
                <p class="meta">
                    Round {{ match.round }} ·
                    {{ match.status === "Live" ? `${match.minute}'` : match.status }}
                    <span v-if="match.isDegraded" class="degraded">· some events are missing</span>
                </p>
            </header>

            <MatchTimeline :timeline="data.timeline" :home-team-id="match.home.id" />
        </template>
    </section>
</template>

<style scoped>
.head {
    padding: 16px 12px 12px;
    border-bottom: 1px solid var(--line);
}

h1 {
    font-size: var(--text-lg);
    margin: 0 0 4px;
    font-weight: 600;
}

.meta {
    margin: 0;
    color: var(--muted);
    font-size: var(--text-sm);
}

.degraded {
    color: var(--warn);
}
</style>
