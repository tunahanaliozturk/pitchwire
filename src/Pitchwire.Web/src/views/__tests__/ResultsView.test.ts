import { fireEvent, render, screen } from "@testing-library/vue";
import { VueQueryPlugin } from "@tanstack/vue-query";
import { createPinia, setActivePinia } from "pinia";
import { describe, expect, it, vi } from "vitest";

import type { MatchSummary } from "@/api/contracts";
import ResultsView from "@/views/ResultsView.vue";

const client = vi.hoisted(() => ({
    countries: vi.fn(),
    leagues: vi.fn(),
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
        client.results.mockResolvedValue({
            value: [result("one", "First club")],
            nextLink: "/api/seasons/season-1/results?$skiptoken=opaque",
        });
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

        await screen.findByText("First club");
        expect(screen.getByText("FT")).toBeTruthy();
        expect(client.results).toHaveBeenCalledWith("season-1", 20);

        await fireEvent.click(screen.getByRole("button", { name: "Show more" }));
        await screen.findByText("Second club");
        expect(screen.getAllByRole("heading", { level: 2 })).toHaveLength(2);
        expect(client.page).toHaveBeenCalledWith("/api/seasons/season-1/results?$skiptoken=opaque");
        expect(screen.queryByRole("button", { name: "Show more" })).toBeNull();
    });
});
