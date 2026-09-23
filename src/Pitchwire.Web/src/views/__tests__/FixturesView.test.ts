import { fireEvent, render, screen, waitFor } from "@testing-library/vue";
import { VueQueryPlugin } from "@tanstack/vue-query";
import { createPinia, setActivePinia } from "pinia";
import { describe, expect, it, vi } from "vitest";

import type { MatchSummary, PageOfMatchSummary } from "@/api/contracts";
import FixturesView from "@/views/FixturesView.vue";

const client = vi.hoisted(() => ({
    countries: vi.fn(),
    leagues: vi.fn(),
    fixtures: vi.fn(),
    page: vi.fn(),
}));
vi.mock("@/api/client", () => ({ api: client }));

const match = (id: string, name: string): MatchSummary => ({
    id,
    round: 1,
    kickoffUtc: "2026-09-23T18:00:00Z",
    status: "Scheduled",
    minute: 0,
    homeScore: 0,
    awayScore: 0,
    isDegraded: false,
    league: { id, name, slug: id, country: name },
    home: { id: `${id}-home`, name: `${name} Home`, shortName: "HOM", slug: `${id}-home` },
    away: { id: `${id}-away`, name: `${name} Away`, shortName: "AWY", slug: `${id}-away` },
});

describe("fixtures by league", () => {
    it("keeps late pages with their original season when switching leagues", async () => {
        localStorage.clear();
        const pinia = createPinia();
        setActivePinia(pinia);
        client.countries.mockResolvedValue([
            { id: "england", name: "England", code: "ENG", slug: "england", leagues: 1 },
            { id: "spain", name: "Spain", code: "ESP", slug: "spain", leagues: 1 },
        ]);
        client.leagues.mockImplementation(async (slug: string) => [
            {
                id: slug,
                name: slug,
                slug,
                tier: 1,
                countryId: slug,
                country: slug,
                currentSeasonId: `${slug}-season`,
            },
        ]);
        client.fixtures.mockImplementation(async (season: string) => ({
            value: [match(season, season === "england-season" ? "England" : "Spain")],
            nextLink: season === "england-season" ? "/api/england/page-2" : null,
        }));

        let resolvePage!: (page: PageOfMatchSummary) => void;
        client.page.mockReturnValue(
            new Promise<PageOfMatchSummary>((resolve) => {
                resolvePage = resolve;
            }),
        );

        render(FixturesView, {
            global: {
                plugins: [
                    pinia,
                    [
                        VueQueryPlugin,
                        {
                            queryClientConfig: {
                                defaultOptions: { queries: { retry: false, staleTime: Infinity } },
                            },
                        },
                    ],
                ],
                stubs: { RouterLink: { template: "<a><slot /></a>" } },
            },
        });

        await screen.findByText("England Home");
        await fireEvent.click(screen.getByRole("button", { name: "Show more" }));
        expect(client.page).toHaveBeenCalledWith("/api/england/page-2");

        await fireEvent.change(screen.getByLabelText("Country"), { target: { value: "spain" } });
        await screen.findByText("Spain Home");
        expect(screen.queryByText("England Home")).toBeNull();

        resolvePage({ value: [match("england-2", "England Two")], nextLink: null });
        await waitFor(() => expect(screen.queryByText("England Two Home")).toBeNull());

        await fireEvent.change(screen.getByLabelText("Country"), { target: { value: "england" } });
        await screen.findByText("England Two Home");
        expect(screen.queryByText("Spain Home")).toBeNull();
        expect(client.fixtures).toHaveBeenCalledTimes(2);
    });
});
