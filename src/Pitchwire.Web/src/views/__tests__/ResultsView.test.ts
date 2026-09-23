import { fireEvent, render, screen } from "@testing-library/vue";
import { VueQueryPlugin } from "@tanstack/vue-query";
import { createPinia, setActivePinia } from "pinia";
import { describe, expect, it, vi } from "vitest";

import type { MatchSummary } from "@/api/contracts";
import ResultsView from "@/views/ResultsView.vue";

const client = vi.hoisted(() => ({
    countries: vi.fn(),
    leagues: vi.fn(),
    season: vi.fn(),
    results: vi.fn(),
    page: vi.fn(),
}));
vi.mock("@/api/client", () => ({ api: client }));

const result = (id: string, home: string, kickoffUtc = "2026-09-23T18:00:00Z"): MatchSummary => ({
    id,
    round: 1,
    kickoffUtc,
    status: "Finished",
    minute: 90,
    homeScore: 2,
    awayScore: 1,
    isDegraded: false,
    league: { id: "league", name: "League", slug: "league", country: "England" },
    home: { id: `${id}-home`, name: home, shortName: "HOM", slug: `${id}-home` },
    away: { id: `${id}-away`, name: "Visitors", shortName: "VIS", slug: `${id}-away` },
});

describe("results", () => {
    it("shows finished matches and follows the server's nextLink", async () => {
        localStorage.clear();
        const pinia = createPinia();
        setActivePinia(pinia);
        client.countries.mockResolvedValue([
            { id: "england", name: "England", code: "ENG", slug: "england", leagues: 1 },
        ]);
        client.leagues.mockResolvedValue([
            {
                id: "league",
                name: "League",
                slug: "league",
                tier: 1,
                countryId: "england",
                country: "England",
                currentSeasonId: "season-1",
            },
        ]);
        client.season.mockResolvedValue({
            id: "season-1",
            year: 2026,
            league: { id: "league", name: "League", slug: "league", country: "England" },
            teams: [{ id: "one-home", name: "First club", shortName: "FIR", slug: "first-club" }],
            rounds: [1, 2],
        });
        client.results.mockImplementation(
            async (_season: string, filters: { round?: number; teamId?: string }) =>
                filters.round === 2
                    ? {
                          value: filters.teamId ? [] : [result("filtered", "Filtered club")],
                          nextLink: null,
                      }
                    : {
                          value: [result("one", "First club")],
                          nextLink: "/api/seasons/season-1/results?$skiptoken=opaque",
                      },
        );
        client.page.mockResolvedValue({
            value: [result("two", "Second club", "2026-09-22T18:00:00Z")],
            nextLink: null,
        });

        render(ResultsView, {
            global: {
                plugins: [
                    pinia,
                    [
                        VueQueryPlugin,
                        { queryClientConfig: { defaultOptions: { queries: { retry: false } } } },
                    ],
                ],
                stubs: { RouterLink: { template: "<a><slot /></a>" } },
            },
        });

        await screen.findByText("FT");
        expect(client.results).toHaveBeenCalledWith(
            "season-1",
            { round: undefined, teamId: undefined },
            20,
        );

        await fireEvent.click(screen.getByRole("button", { name: "Show more" }));
        await screen.findByText("Second club");
        expect(screen.getAllByRole("heading", { level: 2 })).toHaveLength(2);
        expect(client.page).toHaveBeenCalledWith("/api/seasons/season-1/results?$skiptoken=opaque");
        expect(screen.queryByRole("button", { name: "Show more" })).toBeNull();

        await fireEvent.update(screen.getByLabelText("Round"), "2");
        await screen.findByText("Filtered club");
        expect(screen.queryByLabelText(/First club 2, Visitors 1, FT/)).toBeNull();
        expect(client.results).toHaveBeenCalledWith(
            "season-1",
            { round: 2, teamId: undefined },
            20,
        );

        await fireEvent.update(screen.getByLabelText("Team"), "one-home");
        await screen.findByText("No matches found for these filters.");
        expect(client.results).toHaveBeenCalledWith(
            "season-1",
            { round: 2, teamId: "one-home" },
            20,
        );
    });
});
