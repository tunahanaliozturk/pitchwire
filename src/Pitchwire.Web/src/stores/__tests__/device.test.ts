import { createPinia, setActivePinia } from "pinia";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { DeviceSettings } from "@/api/contracts";
import { useDeviceStore } from "@/stores/device";

const settings: DeviceSettings = {
    id: "device-1",
    timeZoneId: "Europe/Istanbul",
    quietHoursStart: null,
    quietHoursEnd: null,
    notifyOnGoal: true,
    notifyOnRedCard: false,
    notifyOnKickoff: false,
    notifyOnFullTime: true,
    favourites: [],
};

const client = vi.hoisted(() => ({
    device: vi.fn(),
    identify: vi.fn(),
    setFavourites: vi.fn(),
    setPreferences: vi.fn(),
    pushKey: vi.fn(),
    subscribe: vi.fn(),
    unsubscribe: vi.fn(),
}));

vi.mock("@/api/client", () => ({ api: client }));

/**
 * What this browser is, and whether it can be reached when nobody is looking.
 *
 * The three failures worth separating are a browser that cannot do push at all, one whose owner said
 * no, and one that has simply not been asked. Collapsing them produces a settings screen that tells
 * somebody to enable something their browser has never supported.
 */
describe("the device store", () => {
    beforeEach(() => {
        setActivePinia(createPinia());
        vi.clearAllMocks();
        client.device.mockResolvedValue(null);
        client.identify.mockResolvedValue(settings);
    });

    it("says push is unsupported when the browser has no push manager", async () => {
        // jsdom has neither, which is exactly the browser this branch exists for.
        const store = useDeviceStore();

        await store.load();

        expect(store.pushState).toBe("unsupported");
    });

    it("asks for an identity only when one is needed", async () => {
        // A crawler reading the live list should not leave a row behind, so nothing is issued until
        // somebody follows a team or turns notifications on.
        const store = useDeviceStore();

        await store.load();
        expect(client.identify).not.toHaveBeenCalled();

        client.setFavourites.mockResolvedValue({ ...settings, favourites: ["team-1"] });
        await store.setFavourite("team-1", true);

        expect(client.identify).toHaveBeenCalledOnce();
        expect(store.follows("team-1")).toBe(true);
    });

    it("keeps one identity rather than asking again on every change", async () => {
        const store = useDeviceStore();
        client.setFavourites.mockResolvedValue({ ...settings, favourites: ["team-1"] });

        await store.setFavourite("team-1", true);
        await store.setFavourite("team-2", true);

        expect(client.identify).toHaveBeenCalledOnce();
    });

    it("removes a team by sending the list without it", async () => {
        const store = useDeviceStore();
        client.device.mockResolvedValue({ ...settings, favourites: ["team-1", "team-2"] });
        client.setFavourites.mockResolvedValue({ ...settings, favourites: ["team-2"] });

        await store.load();
        await store.setFavourite("team-1", false);

        expect(client.setFavourites).toHaveBeenCalledWith(["team-2"]);
        expect(store.follows("team-1")).toBe(false);
    });

    it("does not try to subscribe in a browser that cannot", async () => {
        const store = useDeviceStore();

        await store.enablePush();

        expect(store.pushState).toBe("unsupported");
        expect(client.subscribe).not.toHaveBeenCalled();
    });
});
