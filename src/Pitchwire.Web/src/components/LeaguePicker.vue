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

const retry = async () => {
    if (leagues.countries.length === 0) {
        await leagues.load();
    } else {
        await leagues.chooseCountry(leagues.countrySlug ?? leagues.countries[0]!.slug);
    }
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
        <p v-if="leagues.error" class="error" role="alert">
            {{ leagues.error }}
            <button type="button" @click="retry">Retry</button>
        </p>
    </div>
</template>

<style scoped>
.picker {
    display: flex;
    flex-wrap: wrap;
    gap: var(--gap);
    padding: 12px;
    border-bottom: 1px solid var(--line);
    background: var(--surface);
}

.error {
    flex-basis: 100%;
    margin: 0;
    color: var(--warn);
    font-size: var(--text-sm);
}

.error button {
    margin-left: 8px;
    font: inherit;
    color: var(--ink);
    background: none;
    border: 0;
    text-decoration: underline;
    cursor: pointer;
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
