import { z } from "zod";

import type { components } from "./schema";

/**
 * What the server actually sends, checked at the boundary.
 *
 * Every response is parsed rather than cast. `await response.json() as MatchSummary` is a promise made
 * to the compiler; parsing is a check, and it turns a server change into one clear error at the fetch
 * site instead of `undefined is not an object` three components deep.
 */

export const teamRef = z.object({
    id: z.string(),
    name: z.string(),
    shortName: z.string(),
    slug: z.string(),
});

export const seasonSummary = z.object({
    id: z.string(),
    year: z.number(),
    leagueId: z.string(),
    league: z.string(),
    leagueSlug: z.string(),
});

export const matchSummary = z.object({
    id: z.string(),
    round: z.number(),
    kickoffUtc: z.string(),
    status: z.string(),
    minute: z.number(),
    homeScore: z.number(),
    awayScore: z.number(),
    isDegraded: z.boolean(),
    home: teamRef,
    away: teamRef,
});

export const matchEventView = z.object({
    sequence: z.number(),
    minute: z.number(),
    kind: z.string(),
    teamId: z.string(),
    player: z.string().nullable(),
    assist: z.string().nullable(),
});

export const matchDetail = z.object({
    match: matchSummary,
    timeline: z.array(matchEventView),
});

export const pageOfMatchSummary = z.object({
    value: z.array(matchSummary),
    nextLink: z.string().nullable(),
});

export const tableRow = z.object({
    position: z.number(),
    team: teamRef,
    played: z.number(),
    won: z.number(),
    drawn: z.number(),
    lost: z.number(),
    goalsFor: z.number(),
    goalsAgainst: z.number(),
    goalDifference: z.number(),
    points: z.number(),
});

export const scorerRow = z.object({
    playerId: z.string(),
    player: z.string(),
    team: teamRef,
    goals: z.number(),
    assists: z.number(),
});

export const formEntry = z.object({
    matchId: z.string(),
    kickoffUtc: z.string(),
    opponent: teamRef,
    atHome: z.boolean(),
    goalsFor: z.number(),
    goalsAgainst: z.number(),
    outcome: z.string(),
});

export const deviceSettings = z.object({
    id: z.string(),
    timeZoneId: z.string(),
    quietHoursStart: z.string().nullable(),
    quietHoursEnd: z.string().nullable(),
    notifyOnGoal: z.boolean(),
    notifyOnRedCard: z.boolean(),
    notifyOnKickoff: z.boolean(),
    notifyOnFullTime: z.boolean(),
    favourites: z.array(z.string()),
});

export const pushKey = z.object({ publicKey: z.string() });

export type DeviceSettings = z.infer<typeof deviceSettings>;
export type PushKey = z.infer<typeof pushKey>;
export type SeasonSummary = z.infer<typeof seasonSummary>;
export type TeamRef = z.infer<typeof teamRef>;
export type MatchSummary = z.infer<typeof matchSummary>;
export type MatchEventView = z.infer<typeof matchEventView>;
export type MatchDetail = z.infer<typeof matchDetail>;
export type PageOfMatchSummary = z.infer<typeof pageOfMatchSummary>;
export type TableRow = z.infer<typeof tableRow>;
export type ScorerRow = z.infer<typeof scorerRow>;
export type FormEntry = z.infer<typeof formEntry>;

/**
 * The drift check.
 *
 * These lines do nothing at runtime and are the reason the contract is not decorative. The generated
 * types come from the server's own OpenAPI document; the schemas above are what this application
 * believes. Rename a field on the server, regenerate, and the type check fails here rather than in a
 * browser at half past ten on a Saturday.
 */
type Equal<A, B> =
    (<T>() => T extends A ? 1 : 2) extends <T>() => T extends B ? 1 : 2 ? true : false;
type Agrees<T extends true> = T;

export type ContractChecks = [
    Agrees<Equal<DeviceSettings, components["schemas"]["DeviceSettings"]>>,
    Agrees<Equal<PushKey, components["schemas"]["PushKey"]>>,
    Agrees<Equal<SeasonSummary, components["schemas"]["SeasonSummary"]>>,
    Agrees<Equal<TeamRef, components["schemas"]["TeamRef"]>>,
    Agrees<Equal<MatchSummary, components["schemas"]["MatchSummary"]>>,
    Agrees<Equal<MatchEventView, components["schemas"]["MatchEventView"]>>,
    Agrees<Equal<MatchDetail, components["schemas"]["MatchDetail"]>>,
    Agrees<Equal<PageOfMatchSummary, components["schemas"]["PageOfMatchSummary"]>>,
    Agrees<Equal<TableRow, components["schemas"]["TableRow"]>>,
    Agrees<Equal<ScorerRow, components["schemas"]["ScorerRow"]>>,
    Agrees<Equal<FormEntry, components["schemas"]["FormEntry"]>>,
];
