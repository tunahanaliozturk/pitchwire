import { defineStore } from "pinia";
import { computed, ref } from "vue";

import { api } from "@/api/client";
import type { DeviceSettings } from "@/api/contracts";

/**
 * Whether this browser can be reached when nobody is looking at it.
 *
 * Push is not available everywhere, and the honest answers are worth telling apart. A browser that
 * cannot do it at all needs a different sentence from one whose owner said no, and both need a
 * different sentence from one that simply has not been asked yet.
 */
export type PushState = "unsupported" | "denied" | "off" | "on";

export const useDeviceStore = defineStore("device", () => {
    const settings = ref<DeviceSettings | null>(null);
    const pushState = ref<PushState>("off");
    const busy = ref(false);

    const favourites = computed(() => settings.value?.favourites ?? []);
    const follows = (teamId: string) => favourites.value.includes(teamId);

    const supported = () =>
        typeof window !== "undefined" &&
        "serviceWorker" in navigator &&
        "PushManager" in window &&
        "Notification" in window;

    /**
     * Finds out who this browser is, asking for an identity only if it has none.
     *
     * Identity is handed out on request rather than on every visit, so a crawler reading the live list
     * does not leave a row behind.
     */
    const load = async () => {
        settings.value = await api.device();

        if (!supported()) {
            pushState.value = "unsupported";
            return;
        }

        if (Notification.permission === "denied") {
            pushState.value = "denied";
            return;
        }

        const registration = await navigator.serviceWorker.getRegistration();
        const existing = await registration?.pushManager.getSubscription();
        pushState.value = existing ? "on" : "off";
    };

    const ensureIdentity = async (): Promise<DeviceSettings> => {
        const known = settings.value;

        if (known !== null) {
            return known;
        }

        const issued = await api.identify();
        settings.value = issued;

        return issued;
    };

    const setFavourite = async (teamId: string, wanted: boolean) => {
        await ensureIdentity();

        const next = wanted
            ? [...favourites.value, teamId]
            : favourites.value.filter((id) => id !== teamId);

        settings.value = await api.setFavourites(next);
    };

    const savePreferences = async (preferences: Omit<DeviceSettings, "id" | "favourites">) => {
        await ensureIdentity();
        settings.value = await api.setPreferences(preferences);
    };

    const enablePush = async () => {
        if (!supported()) {
            pushState.value = "unsupported";
            return;
        }

        busy.value = true;

        try {
            await ensureIdentity();

            const permission = await Notification.requestPermission();

            if (permission !== "granted") {
                pushState.value = permission === "denied" ? "denied" : "off";
                return;
            }

            const registration = await navigator.serviceWorker.ready;

            const subscription = await registration.pushManager.subscribe({
                // Web Push requires it, and a subscription without it is refused by every browser. Nothing
                // here sends a silent message anyway: a notification the reader never sees is a goal they
                // never heard about.
                userVisibleOnly: true,
                applicationServerKey: await api.pushKey(),
            });

            await api.subscribe(subscription.toJSON());
            pushState.value = "on";
        } finally {
            busy.value = false;
        }
    };

    const disablePush = async () => {
        busy.value = true;

        try {
            const registration = await navigator.serviceWorker.getRegistration();
            const subscription = await registration?.pushManager.getSubscription();

            if (subscription) {
                // Told first, then cancelled. The other way round leaves the service sending to an endpoint
                // that has already gone, which it answers by deleting a subscription nobody owns any more.
                await api.unsubscribe(subscription.endpoint);
                await subscription.unsubscribe();
            }

            pushState.value = "off";
        } finally {
            busy.value = false;
        }
    };

    return {
        settings,
        favourites,
        follows,
        pushState,
        busy,
        load,
        ensureIdentity,
        setFavourite,
        savePreferences,
        enablePush,
        disablePush,
    };
});
