/// <reference lib="webworker" />
import { cleanupOutdatedCaches, precacheAndRoute } from "workbox-precaching";

import { fallback, readNotification, type GoalNotification } from "./push/payload";

/**
 * The part of the application that runs when nobody is looking at it.
 *
 * Written by hand rather than generated, because the two things it does are the two things that
 * matter: showing a goal that arrived while the tab was closed, and opening the right match when
 * somebody taps it. A generated worker would precache the build and do neither.
 */
declare const self: ServiceWorkerGlobalScope;

// Injected by the build. Precaching the shell is what lets the application open at all on a train.
precacheAndRoute(self.__WB_MANIFEST);
cleanupOutdatedCaches();

function parse(data: PushMessageData | null): GoalNotification {
    if (data === null) {
        return fallback;
    }

    try {
        return readNotification(data.json());
    } catch {
        // Not JSON at all, which is still no reason to show the browser's own update notice.
        return fallback;
    }
}

self.addEventListener("push", (event) => {
    const notification = parse(event.data);

    // renotify is in the specification and not in the DOM typings, so the shape is widened here rather
    // than the behaviour dropped. Without it a second goal replaces the first silently, and the phone
    // that buzzed for one nil says nothing at two nil.
    const options: NotificationOptions & { renotify?: boolean } = {
        body: notification.body,
        // Grouped per match, so a match that scores three times replaces its own notification rather
        // than stacking three of them on a lock screen.
        tag: notification.url,
        renotify: true,
        data: { url: notification.url },
        icon: "/icon.svg",
        badge: "/icon.svg",
    };

    event.waitUntil(self.registration.showNotification(notification.title, options));
});

self.addEventListener("notificationclick", (event) => {
    event.notification.close();

    const target = (event.notification.data as { url?: string } | undefined)?.url ?? "/";

    event.waitUntil(
        (async () => {
            const windows = await self.clients.matchAll({
                type: "window",
                includeUncontrolled: true,
            });

            for (const client of windows) {
                if ("focus" in client) {
                    // Focus what is already open and take it to the match, rather than leaving somebody with
                    // four tabs of the same application by the end of an afternoon.
                    await client.focus();

                    if ("navigate" in client) {
                        await client.navigate(target);
                    }

                    return;
                }
            }

            await self.clients.openWindow(target);
        })(),
    );
});

self.addEventListener("install", () => {
    // A goal is worth interrupting for. Waiting for every tab to close before a fix reaches the worker
    // that shows notifications is not.
    void self.skipWaiting();
});

self.addEventListener("activate", (event) => {
    event.waitUntil(self.clients.claim());
});
