import type { z } from "zod";

import {
    seasonSummary,
    matchDetail,
    matchEventView,
    pageOfMatchSummary,
    scorerRow,
    tableRow,
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
    seasons: (): Promise<SeasonSummary[]> => read("/seasons", seasonSummary.array()),

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
};
