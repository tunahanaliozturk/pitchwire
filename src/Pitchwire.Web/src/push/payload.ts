/**
 * What the service sent, read safely.
 *
 * Kept out of the worker so it can be tested by calling it. A push handler that throws shows the
 * browser's own "This site has been updated in the background" notice, which tells the reader
 * nothing and looks like a defect, so nothing in here is allowed to throw.
 */
export interface GoalNotification {
    title: string;
    body: string;
    url: string;
}

export const fallback: GoalNotification = {
    title: "pitchwire",
    body: "Something happened in a match you follow.",
    url: "/",
};

function isLocalPath(url: unknown): url is string {
    return typeof url === "string" && url.startsWith("/") && !url.startsWith("//");
}

export function readNotification(raw: unknown): GoalNotification {
    if (raw === null || typeof raw !== "object") {
        return fallback;
    }

    const payload = raw as Partial<GoalNotification>;

    return {
        title:
            typeof payload.title === "string" && payload.title !== ""
                ? payload.title
                : fallback.title,
        body:
            typeof payload.body === "string" && payload.body !== "" ? payload.body : fallback.body,
        // Only paths this application owns. A url from somewhere else would turn a notification into
        // a way of sending somebody anywhere, which is not something a goal should be able to do.
        //
        // The second condition is the one that matters: "//example.invalid" starts with a slash and
        // navigates straight off the site, so checking for a leading slash alone is not a check.
        url: isLocalPath(payload.url) ? payload.url : fallback.url,
    };
}
