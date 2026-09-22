import type { z } from "zod";

import {
    countrySummary,
    deviceSettings,
    leagueSummary,
    matchDetail,
    matchEventView,
    pageOfMatchSummary,
    pushKey,
    scorerRow,
    seasonSummary,
    tableRow,
    type CountrySummary,
    type DeviceSettings,
    type LeagueSummary,
    type MatchDetail,
    type MatchEventView,
    type PageOfMatchSummary,
    type ScorerRow,
    type SeasonSummary,
    type TableRow,
} from "./contracts";

/**
 * Everything the application asks the server for.
 *
 * One origin, so the device cookie travels without any CORS arrangement. Every response is parsed
 * through a schema rather than cast, and a failure names the request that produced it: a message
 * saying which endpoint disagreed is worth more than a stack trace in a component.
 */
const base = "/api";

export class ApiError extends Error {
    constructor(
        message: string,
        readonly status: number,
    ) {
        super(message);
        this.name = "ApiError";
    }
}

async function send<T>(
    path: string,
    method: string,
    schema: z.ZodType<T> | null,
    body?: unknown,
): Promise<T> {
    const response = await fetch(`${base}${path}`, {
        method,
        // The device cookie is the identity. Without this nothing below belongs to anybody.
        credentials: "same-origin",
        headers:
            body === undefined
                ? { accept: "application/json" }
                : { accept: "application/json", "content-type": "application/json" },
        body: body === undefined ? undefined : JSON.stringify(body),
    });

    if (!response.ok) {
        throw new ApiError(`${path} answered ${response.status}.`, response.status);
    }

    if (schema === null) {
        return undefined as T;
    }

    const parsed = schema.safeParse(await response.json());

    if (!parsed.success) {
        throw new ApiError(
            `${path} sent a body this build cannot read: ${parsed.error.message}`,
            200,
        );
    }

    return parsed.data;
}

async function read<T>(path: string, schema: z.ZodType<T>): Promise<T> {
    const response = await fetch(path.startsWith("http") ? path : `${base}${path}`, {
        credentials: "same-origin",
        headers: { accept: "application/json" },
    });

    if (!response.ok) {
        throw new ApiError(`${path} answered ${response.status}.`, response.status);
    }

    const parsed = schema.safeParse(await response.json());

    if (!parsed.success) {
        // The server sent something this build does not understand. Saying so here, with the path, is
        // what turns a contract change into one clear error rather than a puzzle further down.
        throw new ApiError(
            `${path} sent a body this build cannot read: ${parsed.error.message}`,
            200,
        );
    }

    return parsed.data;
}

export const api = {
    countries: (): Promise<CountrySummary[]> => read("/countries", countrySummary.array()),

    leagues: (countrySlug: string): Promise<LeagueSummary[]> =>
        read(`/countries/${countrySlug}/leagues`, leagueSummary.array()),

    seasons: (leagueId?: string): Promise<SeasonSummary[]> =>
        read(
            leagueId === undefined ? "/seasons" : `/seasons?leagueId=${leagueId}`,
            seasonSummary.array(),
        ),

    live: (top = 50): Promise<PageOfMatchSummary> =>
        read(`/matches/live?$top=${top}`, pageOfMatchSummary),

    /** Follows a nextLink exactly as it was given, which is the only supported way to page. */
    page: (nextLink: string): Promise<PageOfMatchSummary> => read(nextLink, pageOfMatchSummary),

    fixtures: (seasonId: string, round?: number, top = 50): Promise<PageOfMatchSummary> =>
        read(
            `/seasons/${seasonId}/fixtures?$top=${top}${round === undefined ? "" : `&round=${round}`}`,
            pageOfMatchSummary,
        ),

    results: (seasonId: string, top = 20): Promise<PageOfMatchSummary> =>
        read(`/seasons/${seasonId}/results?$top=${top}`, pageOfMatchSummary),

    match: (matchId: string): Promise<MatchDetail> => read(`/matches/${matchId}`, matchDetail),

    timeline: (matchId: string, from: number): Promise<MatchEventView[]> =>
        read(`/matches/${matchId}/events?from=${from}`, matchEventView.array()),

    table: (seasonId: string): Promise<TableRow[]> =>
        read(`/seasons/${seasonId}/table`, tableRow.array()),

    scorers: (seasonId: string, top = 10): Promise<ScorerRow[]> =>
        read(`/seasons/${seasonId}/scorers?$top=${top}`, scorerRow.array()),

    /** The identity this browser already has, or nothing if it has never been here. */
    device: async (): Promise<DeviceSettings | null> => {
        const response = await fetch(`${base}/devices/me`, {
            credentials: "same-origin",
            headers: { accept: "application/json" },
        });

        // Not an error. A first visit has no identity yet, and asking is how it finds out.
        if (response.status === 401) {
            return null;
        }

        if (!response.ok) {
            throw new ApiError(`/devices/me answered ${response.status}.`, response.status);
        }

        return deviceSettings.parse(await response.json());
    },

    identify: (): Promise<DeviceSettings> => send("/devices", "POST", deviceSettings),

    setFavourites: (teamIds: string[]): Promise<DeviceSettings> =>
        send("/devices/me/favourites", "PUT", deviceSettings, { teamIds }),

    setPreferences: (
        preferences: Omit<DeviceSettings, "id" | "favourites">,
    ): Promise<DeviceSettings> =>
        send("/devices/me/preferences", "PUT", deviceSettings, preferences),

    pushKey: (): Promise<string> => read("/push/public-key", pushKey).then((key) => key.publicKey),

    subscribe: (subscription: PushSubscriptionJSON): Promise<void> =>
        send("/devices/me/push-subscriptions", "POST", null, {
            endpoint: subscription.endpoint,
            keys: { p256dh: subscription.keys?.["p256dh"], auth: subscription.keys?.["auth"] },
        }),

    unsubscribe: (endpoint: string): Promise<void> =>
        send(
            `/devices/me/push-subscriptions?endpoint=${encodeURIComponent(endpoint)}`,
            "DELETE",
            null,
        ),
};
