import { describe, expect, it } from "vitest";

import type { MatchSummary, PageOfMatchSummary } from "@/api/contracts";
import { applyUpdate } from "@/composables/liveList";
import type { MatchUpdate } from "@/composables/useLiveFeed";

const match: MatchSummary = {
    id: "match-1",
    round: 1,
    kickoffUtc: "2026-09-20T18:00:00+00:00",
    status: "Live",
    minute: 20,
    homeScore: 0,
    awayScore: 0,
    isDegraded: false,
    home: { id: "h", name: "Harbour Rovers", shortName: "HAR", slug: "harbour-rovers" },
    away: { id: "a", name: "Kingsway United", shortName: "KIN", slug: "kingsway-united" },
};

const page: PageOfMatchSummary = { value: [match], nextLink: null };

const update = (over: Partial<MatchUpdate> = {}): MatchUpdate => ({
    matchId: "match-1",
    homeTeamId: "h",
    awayTeamId: "a",
    sequence: 2,
    minute: 23,
    status: "Live",
    homeScore: 1,
    awayScore: 0,
    isDegraded: false,
    event: null,
    ...over,
});

/**
 * What the live screen does with a delta.
 *
 * Patching rather than refetching is the reason for pushing updates at all. The first version only
 * rewrote rows it already held, which is right for a score and wrong for everything else: a finished
 * match stayed on screen and the next round never appeared, so a browser left open kept showing the
 * previous round for as long as anybody watched it. Both directions are the same defect, and these
 * are the tests that would have caught it.
 */
describe("applyUpdate", () => {
    it("moves the score and the minute of a match it holds", () => {
        const result = applyUpdate(page, update());

        expect(result.page.value[0]?.homeScore).toBe(1);
        expect(result.page.value[0]?.minute).toBe(23);
        expect(result.unknownMatch).toBe(false);
    });

    it("takes a finished match off the live list", () => {
        const result = applyUpdate(page, update({ status: "Finished", minute: 94 }));

        expect(result.page.value).toHaveLength(0);
    });

    it("keeps a match that is only at half time", () => {
        // Half time is an interval, not an ending. Dropping it would empty the screen every Saturday
        // at a quarter to four.
        const result = applyUpdate(page, update({ status: "Halftime", minute: 45 }));

        expect(result.page.value).toHaveLength(1);
        expect(result.page.value[0]?.status).toBe("Halftime");
    });

    it("asks for the list again when a match it has never seen kicks off", () => {
        const result = applyUpdate(page, update({ matchId: "match-2", status: "Live" }));

        expect(result.unknownMatch).toBe(true);
        expect(result.page.value).toHaveLength(1);
    });

    it("does not ask for the list again because a match it does not hold has finished", () => {
        // A match that ended somewhere else is not news for this screen, and refetching on every one
        // of them would undo the point of pushing updates.
        const result = applyUpdate(page, update({ matchId: "match-2", status: "Finished" }));

        expect(result.unknownMatch).toBe(false);
    });

    it("leaves the other matches alone", () => {
        const two: PageOfMatchSummary = {
            value: [match, { ...match, id: "match-2" }],
            nextLink: null,
        };

        const result = applyUpdate(two, update());

        expect(result.page.value[1]?.homeScore).toBe(0);
    });
});
