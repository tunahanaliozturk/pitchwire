import { createPinia, setActivePinia } from "pinia";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { CountrySummary, LeagueSummary } from "@/api/contracts";
import { useLeagueStore } from "@/stores/league";

const client = vi.hoisted(() => ({ countries: vi.fn(), leagues: vi.fn() }));
vi.mock("@/api/client", () => ({ api: client }));

const countries: CountrySummary[] = [
    { id: "england", name: "England", code: "ENG", slug: "england", leagues: 1 },
    { id: "spain", name: "Spain", code: "ESP", slug: "spain", leagues: 1 },
    { id: "turkiye", name: "Türkiye", code: "TUR", slug: "turkiye", leagues: 1 },
];

const league = (id: string): LeagueSummary => ({
    id,
    name: id,
    slug: id,
    tier: 1,
    countryId: id,
    country: id,
    currentSeasonId: `${id}-season`,
});

function deferred<T>() {
    let resolve!: (value: T) => void;
    let reject!: (reason: Error) => void;
    const promise = new Promise<T>((done, fail) => {
        resolve = done;
        reject = fail;
    });
    return { promise, resolve, reject };
}

describe("league selection", () => {
    beforeEach(() => {
        setActivePinia(createPinia());
        localStorage.clear();
        vi.clearAllMocks();
        client.countries.mockResolvedValue(countries);
        client.leagues.mockImplementation(async (slug: string) => [league(slug)]);
    });

    it("shares the initial load between the screen and picker", async () => {
        const pending = deferred<CountrySummary[]>();
        client.countries.mockReturnValue(pending.promise);
        const store = useLeagueStore();

        const first = store.load();
        const second = store.load();
        expect(client.countries).toHaveBeenCalledOnce();

        pending.resolve(countries);
        await Promise.all([first, second]);
        expect(store.seasonId).toBe("england-season");
    });

    it("keeps the latest country when an earlier request finishes last", async () => {
        const store = useLeagueStore();
        await store.load();

        const oldRequest = deferred<LeagueSummary[]>();
        client.leagues.mockImplementation((slug: string) =>
            slug === "turkiye" ? oldRequest.promise : Promise.resolve([league(slug)]),
        );

        const first = store.chooseCountry("turkiye");
        await store.chooseCountry("spain");
        oldRequest.resolve([league("turkiye")]);
        await first;

        expect(store.countrySlug).toBe("spain");
        expect(store.seasonId).toBe("spain-season");
        expect(JSON.parse(localStorage.getItem("pitchwire-league") ?? "null")).toEqual({
            country: "spain",
            league: "spain",
        });
    });

    it("shows a failed league load and can retry it", async () => {
        const store = useLeagueStore();
        await store.load();
        client.leagues.mockRejectedValueOnce(new Error("offline"));

        await store.chooseCountry("spain");
        expect(store.error).toBe("The leagues could not be loaded.");
        expect(store.seasonId).toBeNull();

        await store.chooseCountry("spain");
        expect(store.error).toBeNull();
        expect(store.seasonId).toBe("spain-season");
    });
});
