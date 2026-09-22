import { defineStore } from "pinia";
import { computed, ref, watch } from "vue";

import { api } from "@/api/client";
import type { CountrySummary, LeagueSummary } from "@/api/contracts";

const remembered = "pitchwire-league";

/**
 * Which league the reader is looking at.
 *
 * Remembered in this browser, because somebody who follows one league should not have to find it
 * again on every visit. It is a convenience and nothing depends on it surviving, so a private window
 * or blocked site data costs nothing but the first click.
 */
export const useLeagueStore = defineStore("league", () => {
    const countries = ref<CountrySummary[]>([]);
    const leagues = ref<LeagueSummary[]>([]);
    const countrySlug = ref<string | null>(null);
    const leagueId = ref<string | null>(null);
    const loading = ref(false);

    const country = computed(
        () => countries.value.find((c) => c.slug === countrySlug.value) ?? null,
    );
    const league = computed(() => leagues.value.find((l) => l.id === leagueId.value) ?? null);
    const seasonId = computed(() => league.value?.currentSeasonId ?? null);

    const recall = (): { country: string; league: string } | null => {
        try {
            const stored = localStorage.getItem(remembered);
            return stored === null
                ? null
                : (JSON.parse(stored) as { country: string; league: string });
        } catch {
            return null;
        }
    };

    const remember = () => {
        try {
            if (countrySlug.value !== null && leagueId.value !== null) {
                localStorage.setItem(
                    remembered,
                    JSON.stringify({ country: countrySlug.value, league: leagueId.value }),
                );
            }
        } catch {
            // Nothing is lost that matters: the page still shows what was just chosen.
        }
    };

    const chooseCountry = async (slug: string) => {
        countrySlug.value = slug;
        leagues.value = await api.leagues(slug);
        // The top flight is what somebody means by a country's football until they say otherwise.
        leagueId.value = leagues.value[0]?.id ?? null;
        remember();
    };

    const chooseLeague = (id: string) => {
        leagueId.value = id;
        remember();
    };

    const load = async () => {
        if (countries.value.length > 0) {
            return;
        }

        loading.value = true;

        try {
            countries.value = await api.countries();

            const previous = recall();
            const known = previous && countries.value.some((c) => c.slug === previous.country);

            await chooseCountry(known ? previous.country : (countries.value[0]?.slug ?? ""));

            if (known && leagues.value.some((l) => l.id === previous.league)) {
                leagueId.value = previous.league;
            }
        } finally {
            loading.value = false;
        }
    };

    watch(leagueId, remember);

    return {
        countries,
        leagues,
        countrySlug,
        leagueId,
        country,
        league,
        seasonId,
        loading,
        load,
        chooseCountry,
        chooseLeague,
    };
});
