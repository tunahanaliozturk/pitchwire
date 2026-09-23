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
    const loadingCountries = ref(false);
    const loadingLeagues = ref(false);
    const loading = computed(() => loadingCountries.value || loadingLeagues.value);
    const error = ref<string | null>(null);
    let countryRequest = 0;
    let pendingLoad: Promise<void> | null = null;

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
        const request = ++countryRequest;
        countrySlug.value = slug;
        leagueId.value = null;
        leagues.value = [];
        error.value = null;
        loadingLeagues.value = true;

        try {
            const available = await api.leagues(slug);
            if (request !== countryRequest) {
                return;
            }

            leagues.value = available;
            // The top flight is what somebody means by a country's football until they say otherwise.
            leagueId.value = available[0]?.id ?? null;
            remember();
        } catch {
            if (request === countryRequest) {
                error.value = "The leagues could not be loaded.";
            }
        } finally {
            if (request === countryRequest) {
                loadingLeagues.value = false;
            }
        }
    };

    const chooseLeague = (id: string) => {
        leagueId.value = id;
        remember();
    };

    const load = async () => {
        if (seasonId.value !== null) {
            return;
        }

        if (pendingLoad !== null) {
            return pendingLoad;
        }

        pendingLoad = (async () => {
            loadingCountries.value = true;
            error.value = null;

            try {
                if (countries.value.length === 0) {
                    countries.value = await api.countries();
                }

                const previous = recall();
                const known = previous && countries.value.some((c) => c.slug === previous.country);
                const slug = known ? previous.country : countries.value[0]?.slug;

                if (slug === undefined) {
                    error.value = "No countries are available.";
                    return;
                }

                await chooseCountry(slug);

                if (
                    known &&
                    countrySlug.value === slug &&
                    leagues.value.some((l) => l.id === previous.league)
                ) {
                    leagueId.value = previous.league;
                }
            } catch {
                error.value = "The countries could not be loaded.";
            } finally {
                loadingCountries.value = false;
            }
        })();

        try {
            await pendingLoad;
        } finally {
            pendingLoad = null;
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
        error,
        load,
        chooseCountry,
        chooseLeague,
    };
});
