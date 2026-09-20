import { useQuery } from "@tanstack/vue-query";
import { computed } from "vue";

import { api } from "@/api/client";

/**
 * The season everything else is about.
 *
 * Asked for rather than configured. A season identifier pasted into an environment variable is a
 * value that is wrong on somebody's machine for a week before anybody notices.
 */
export function useSeason() {
    const query = useQuery({
        queryKey: ["seasons"],
        queryFn: api.seasons,
        staleTime: 60 * 60 * 1000,
    });

    const current = computed(() => query.data.value?.[0] ?? null);

    return { current, isLoading: query.isLoading, error: query.error };
}
