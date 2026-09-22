import { render } from "@testing-library/vue";
import { describe, expect, it } from "vitest";

import type { LineupPlayerView, TeamSheetView } from "@/api/contracts";
import PitchFormation from "@/components/PitchFormation.vue";

/**
 * A pitch is a claim about where people played, so the shape it draws has to be the shape the sheet
 * published. The two ways to get this wrong are silent: a formation that reads 3-5-2 drawn as
 * anything else, and a name that is quietly left off because the rows did not add up.
 */
const player = (shirt: number, starter: boolean, name = `Player ${shirt}`): LineupPlayerView => ({
    playerId: `p${shirt}`,
    player: name,
    shirtNumber: shirt,
    position: "Midfielder",
    starter,
    minutesPlayed: starter ? 90 : 0,
    goals: 0,
    assists: 0,
    yellowCards: 0,
    redCards: 0,
    rating: starter ? 6.5 : null,
});

const sheet = (formation: string, starters: number, bench = 7): TeamSheetView => ({
    teamId: "t",
    formation,
    players: [
        ...Array.from({ length: starters }, (_, index) => player(index + 1, true)),
        ...Array.from({ length: bench }, (_, index) => player(starters + index + 1, false)),
    ],
});

const pitch = (home: TeamSheetView | null, away: TeamSheetView | null) =>
    render(PitchFormation, {
        props: { home, away, homeName: "Harbour Rovers", awayName: "Kingsway United" },
    });

/** The distinct rows the eleven were placed on, nearest the drawn goal line first. */
const rows = (container: Element, half: "home" | "away") => {
    const tops = [...container.querySelectorAll<HTMLElement>(".spot")]
        .map((spot) => Number.parseFloat(spot.style.top))
        .filter((top) => (half === "home" ? top > 50 : top < 50));

    const counted = new Map<number, number>();

    for (const top of tops) {
        counted.set(top, (counted.get(top) ?? 0) + 1);
    }

    return [...counted.entries()]
        .sort((left, right) => (half === "home" ? right[0] - left[0] : left[0] - right[0]))
        .map(([, count]) => count);
};

describe("PitchFormation", () => {
    it("draws the rows the formation names, with the keeper alone in front of the goal", () => {
        const { container } = pitch(sheet("3-5-2", 11), sheet("4-3-3", 11));

        expect(rows(container, "home")).toEqual([1, 3, 5, 2]);
        expect(rows(container, "away")).toEqual([1, 4, 3, 3]);
    });

    it("keeps the two sides in their own halves", () => {
        const { container } = pitch(sheet("4-4-2", 11), sheet("4-4-2", 11));

        const tops = [...container.querySelectorAll<HTMLElement>(".spot")].map((spot) =>
            Number.parseFloat(spot.style.top),
        );

        expect(tops.filter((top) => top < 50)).toHaveLength(11);
        expect(tops.filter((top) => top > 50)).toHaveLength(11);
    });

    it("places everybody a corrected sheet names, even when the formation does not account for them", () => {
        // A sheet that arrives with twelve starters is wrong, but dropping the twelfth without saying
        // so is worse: the page would look complete and be missing somebody.
        const { container } = pitch(sheet("4-3-3", 12), null);

        expect(container.querySelectorAll(".spot")).toHaveLength(12);
    });

    it("shows nothing rather than an empty pitch when no sheet has been published", () => {
        const { container } = pitch(null, null);

        expect(container.querySelectorAll(".spot")).toHaveLength(0);
    });

    it("names players by surname, because a pitch has no room for anything else", () => {
        const withNames: TeamSheetView = {
            teamId: "t",
            formation: "4-4-2",
            players: [player(1, true, "Mateo Lindqvist")],
        };

        const { getByText } = pitch(withNames, null);

        expect(getByText("Lindqvist")).toBeTruthy();
    });
});
