import { describe, expect, it } from "vitest";

import { fallback, readNotification } from "@/push/payload";

/**
 * What arrives on a locked phone.
 *
 * Nothing here is allowed to throw. A push handler that does shows the browser's own "This site has
 * been updated in the background" notice, which says nothing about football and looks like a defect
 * to the person holding the phone.
 */
describe("readNotification", () => {
    it("reads what the service sends", () => {
        const notification = readNotification({
            title: "Goal for Harbour Rovers",
            body: "Harbour Rovers 1-0 Kingsway United · 23'",
            url: "/matches/11111111-1111-1111-1111-111111111111",
        });

        expect(notification.title).toBe("Goal for Harbour Rovers");
        expect(notification.url).toBe("/matches/11111111-1111-1111-1111-111111111111");
    });

    it("falls back rather than failing on anything it cannot read", () => {
        expect(readNotification(null)).toEqual(fallback);
        expect(readNotification("not an object")).toEqual(fallback);
        expect(readNotification(42)).toEqual(fallback);
        expect(readNotification({})).toEqual(fallback);
    });

    it("keeps the parts it understands and replaces the parts it does not", () => {
        const notification = readNotification({ title: "Full time", body: 12, url: null });

        expect(notification.title).toBe("Full time");
        expect(notification.body).toBe(fallback.body);
        expect(notification.url).toBe(fallback.url);
    });

    it("refuses a destination that is not inside this application", () => {
        // A notification that can send somebody anywhere is a notification worth sending for the
        // wrong reasons. A goal has no business opening another site.
        expect(readNotification({ url: "https://example.invalid/anything" }).url).toBe(
            fallback.url,
        );
        expect(readNotification({ url: "//example.invalid" }).url).toBe(fallback.url);
        expect(readNotification({ url: "javascript:alert(1)" }).url).toBe(fallback.url);
    });

    it("treats an empty string as nothing at all", () => {
        expect(readNotification({ title: "", body: "", url: "" })).toEqual(fallback);
    });
});
