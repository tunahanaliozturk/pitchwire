<script setup lang="ts">
import { onMounted } from "vue";

import { useLeagueStore } from "@/stores/league";

const leagues = useLeagueStore();

onMounted(() => {
    void leagues.load();
});

const onCountry = async (event: Event) => {
    await leagues.chooseCountry((event.target as HTMLSelectElement).value);
};
</script>

<template>
    <div class="picker">
        <div class="field">
            <label for="country">Country</label>
            <select id="country" :value="leagues.countrySlug ?? ''" @change="onCountry">
                <option
                    v-for="country in leagues.countries"
                    :key="country.id"
                    :value="country.slug"
                >
                    {{ country.name }}
                </option>
            </select>
        </div>

        <div class="field">
            <label for="league">League</label>
            <select
                id="league"
                :value="leagues.leagueId ?? ''"
                @change="leagues.chooseLeague(($event.target as HTMLSelectElement).value)"
            >
                <option v-for="league in leagues.leagues" :key="league.id" :value="league.id">
                    {{ league.name }}
                </option>
            </select>
        </div>
    </div>
</template>

<style scoped>
.picker {
    display: flex;
    gap: var(--gap);
    padding: 12px;
    border-bottom: 1px solid var(--line);
    background: var(--surface);
}

.field {
    display: grid;
    gap: 4px;
    min-width: 0;
    flex: 1;
}

label {
    font-size: var(--text-xs);
    text-transform: uppercase;
    letter-spacing: 0.06em;
    color: var(--muted);
}

select {
    font: inherit;
    font-size: var(--text-sm);
    padding: 7px 8px;
    border: 1px solid var(--line);
    border-radius: var(--radius);
    background: var(--paper);
    color: var(--ink);
    min-width: 0;
}
</style>
