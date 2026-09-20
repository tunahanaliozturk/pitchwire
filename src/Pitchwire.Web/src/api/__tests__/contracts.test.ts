import { describe, expect, it } from "vitest";

import { matchSummary, pageOfMatchSummary } from "@/api/contracts";

/**
 * The boundary, checked.
 *
 * Parsing rather than casting is the whole point: a server that changes shape has to fail here, with
 * a message naming the field, rather than three components deep as an undefined property.
 */
const valid = {
    id: "11111111-1111-1111-1111-111111111111",
    round: 1,
    kickoffUtc: "2026-09-20T18:00:00+00:00",
    status: "Live",
    minute: 12,
    homeScore: 1,
    awayScore: 0,
    isDegraded: false,
    home: { id: "h", name: "Home", shortName: "HOM", slug: "home" },
    away: { id: "a", name: "Away", shortName: "AWY", slug: "away" },
};

describe("response contracts", () => {
    it("accepts what the server sends", () => {
        expect(matchSummary.parse(valid).minute).toBe(12);
    });

    it("refuses a score that arrived as text", () => {
        // The document used to describe every integer as an integer or a string. Narrowing it was worth
        // doing, and this is what notices if that ever comes back.
        const parsed = matchSummary.safeParse({ ...valid, homeScore: "1" });

        expect(parsed.success).toBe(false);
    });

    it("refuses a match with a team missing", () => {
        const { away, ...withoutAway } = valid;
        void away;

        expect(matchSummary.safeParse(withoutAway).success).toBe(false);
    });

    it("accepts a last page, which carries no link", () => {
        const page = pageOfMatchSummary.parse({ value: [valid], nextLink: null });

        expect(page.nextLink).toBeNull();
    });
});
