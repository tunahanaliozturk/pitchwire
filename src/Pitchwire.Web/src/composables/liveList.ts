import type { MatchSummary, PageOfMatchSummary } from "@/api/contracts";
import type { MatchUpdate } from "@/composables/useLiveFeed";

/**
 * A match belongs on the live screen while it is being played, and not a minute longer.
 */
export function isLive(status: string): boolean {
    return status === "Live" || status === "Halftime";
}

export interface PatchResult {
    page: PageOfMatchSummary;

    /**
     * True when the update is about a match the list does not hold yet.
     *
     * A delta carries a score and a minute, not two team names, so there is nothing to build a row
     * out of. The caller asks the server for the list again instead of inventing half of one.
     */
    unknownMatch: boolean;
}

/**
 * Applies one update to the cached live list.
 *
 * Patching rather than refetching is the point of pushing an update at all: a goal in a busy round
 * would otherwise have every watching browser ask for the whole list at the same moment.
 *
 * The first version of this only rewrote rows it already had, which is correct for a score changing
 * and wrong for everything else. A match that finished stayed on the live screen, and the six that
 * kicked off next never appeared, so a browser left open showed the previous round until something
 * else refetched. Both directions are the same defect.
 */
export function applyUpdate(page: PageOfMatchSummary, update: MatchUpdate): PatchResult {
    const held = page.value.some((match) => match.id === update.matchId);

    if (!held) {
        return { page, unknownMatch: isLive(update.status) };
    }

    if (!isLive(update.status)) {
        // Full time. The match has a results screen now, and the live list is about what is being
        // played.
        return {
            page: { ...page, value: page.value.filter((match) => match.id !== update.matchId) },
            unknownMatch: false,
        };
    }

    const patched = (match: MatchSummary): MatchSummary =>
        match.id === update.matchId
            ? {
                  ...match,
                  minute: update.minute,
                  status: update.status,
                  homeScore: update.homeScore,
                  awayScore: update.awayScore,
                  isDegraded: update.isDegraded,
              }
            : match;

    return { page: { ...page, value: page.value.map(patched) }, unknownMatch: false };
}
