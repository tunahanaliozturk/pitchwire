import { describe, expect, it } from "vitest";

// The shell as text rather than as a component. Rendering it would need a router, a query client and
// a service worker, none of which have anything to do with the question being asked.
import shell from "@/App.vue?raw";
import { router } from "@/router";

/**
 * Every link in the navigation has to land somewhere.
 *
 * A link whose route was never registered still renders, still looks enabled, and quietly shows the
 * not found page. Nothing in a type checker or a linter notices, because both halves are valid on
 * their own. So the links are read out of the shell and put to the router, which is the only place
 * the two are ever compared.
 */
const links = [...shell.matchAll(/<RouterLink\s+to="([^"]+)"/g)]
    .map((match) => match[1])
    .filter((path): path is string => path !== undefined);

describe("navigation", () => {
    it("has links to check", () => {
        expect(links.length).toBeGreaterThan(0);
    });

    it.each(links)("resolves %s to a screen of its own", (path) => {
        expect(router.resolve(path).name).not.toBe("not-found");
    });
});
