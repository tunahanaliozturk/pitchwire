import { render, screen } from "@testing-library/vue";
import { describe, expect, it } from "vitest";

import type { MatchSummary } from "@/api/contracts";
import MatchRow from "@/components/MatchRow.vue";

/**
 * A match row is the smallest thing this product sells, and the part a reader spends the most time
 * looking at. What it has to get right is which number is the clock and which is the score.
 */
const base: MatchSummary = {
    id: "11111111-1111-1111-1111-111111111111",
    round: 3,
    kickoffUtc: "2026-09-20T18:00:00+00:00",
    status: "Scheduled",
    minute: 0,
    homeScore: 0,
    awayScore: 0,
    isDegraded: false,
    home: { id: "h", name: "Harbour Rovers", shortName: "HAR", slug: "harbour-rovers" },
    away: { id: "a", name: "Kingsway United", shortName: "KIN", slug: "kingsway-united" },
};

const renderRow = (match: Partial<MatchSummary>, justScored = false) =>
    render(MatchRow, {
        props: { match: { ...base, ...match }, justScored },
        global: { stubs: { RouterLink: { template: "<a><slot /></a>" } } },
    });

describe("MatchRow", () => {
    it("shows the minute while a match is being played", () => {
        renderRow({ status: "Live", minute: 37, homeScore: 2, awayScore: 1 });

        expect(screen.getByText("37'")).toBeTruthy();
    });

    it("says half time and full time rather than a minute", () => {
        renderRow({ status: "Halftime", minute: 45 });
        expect(screen.getByText("HT")).toBeTruthy();

        renderRow({ status: "Finished", minute: 94 });
        expect(screen.getByText("FT")).toBeTruthy();
    });

    it("names both teams and both scores in one label a screen reader can read", () => {
        // The score is two separate numbers on screen, which reads as nonsense out of context. The label
        // is what makes the row mean something to somebody who is not looking at it.
        renderRow({ status: "Live", minute: 50, homeScore: 3, awayScore: 1 });

        const label = screen.getByLabelText(/Harbour Rovers 3, Kingsway United 1, 50'/);
        expect(label).toBeTruthy();
    });

    it("says so when a match is known to be missing events", () => {
        // Showing an incomplete score without saying it is incomplete is the dishonest half of deciding
        // to show it at all.
        renderRow({ status: "Live", isDegraded: true });

        expect(screen.getByText("incomplete")).toBeTruthy();
    });

    it("does not claim a match is incomplete when it is not", () => {
        renderRow({ status: "Live", isDegraded: false });

        expect(screen.queryByText("incomplete")).toBeNull();
    });

    it("marks a row that has just been scored in so the flash can be styled", () => {
        const { container } = renderRow({ status: "Live" }, true);

        expect(container.querySelector(".just-scored")).not.toBeNull();
    });
});
