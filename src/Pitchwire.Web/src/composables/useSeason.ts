import { computed, onMounted } from "vue";

import { useLeagueStore } from "@/stores/league";

/**
 * The season everything else on a screen is about.
 *
 * A thin reading of the league the reader chose, so a view asks for one thing rather than three. The
 * season is not configured anywhere: a season identifier pasted into an environment variable is a
 * value that is wrong on somebody's machine for a week before anybody notices.
 */
export function useSeason() {
    const leagues = useLeagueStore();

    onMounted(() => {
        void leagues.load();
    });

    return {
        current: computed(() =>
            leagues.seasonId === null || leagues.league === null
                ? null
                : { id: leagues.seasonId, league: leagues.league.name },
        ),
        isLoading: computed(() => leagues.loading),
        error: computed(() => null),
    };
}
