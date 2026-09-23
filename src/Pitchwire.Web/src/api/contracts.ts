import { z } from "zod";

import type { components } from "./schema";

// Zod's JIT feature probe calls Function(""), which a strict CSP blocks and reports even though
// Zod catches the error. Parse without that probe; the API payloads are small enough not to need JIT.
z.config({ jitless: true });

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

export const leagueRef = z.object({
    id: z.string(),
    name: z.string(),
    slug: z.string(),
    country: z.string(),
});

export const seasonDetail = z.object({
    id: z.string(),
    year: z.number(),
    league: leagueRef,
    teams: z.array(teamRef),
    rounds: z.array(z.number()),
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
    league: leagueRef,
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
    replaced: z.string().nullable(),
});

export const lineupPlayerView = z.object({
    playerId: z.string(),
    player: z.string(),
    shirtNumber: z.number(),
    position: z.string(),
    starter: z.boolean(),
    minutesPlayed: z.number(),
    goals: z.number(),
    assists: z.number(),
    yellowCards: z.number(),
    redCards: z.number(),
    // Absent for anybody who played too little to judge, which is a different thing from a zero.
    rating: z.number().nullable(),
});

export const teamSheetView = z.object({
    teamId: z.string(),
    formation: z.string(),
    players: z.array(lineupPlayerView),
});

export const teamStatisticsView = z.object({
    teamId: z.string(),
    asOfMinute: z.number(),
    possession: z.number(),
    shots: z.number(),
    shotsOnTarget: z.number(),
    corners: z.number(),
    fouls: z.number(),
    offsides: z.number(),
});

export const matchDetail = z.object({
    match: matchSummary,
    timeline: z.array(matchEventView),
    lineups: z.array(teamSheetView),
    statistics: z.array(teamStatisticsView),
});

export const countrySummary = z.object({
    id: z.string(),
    name: z.string(),
    code: z.string(),
    slug: z.string(),
    leagues: z.number(),
});

export const leagueSummary = z.object({
    id: z.string(),
    name: z.string(),
    slug: z.string(),
    tier: z.number(),
    countryId: z.string(),
    country: z.string(),
    currentSeasonId: z.string().nullable(),
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

export type CountrySummary = z.infer<typeof countrySummary>;
export type LeagueSummary = z.infer<typeof leagueSummary>;
export type LineupPlayerView = z.infer<typeof lineupPlayerView>;
export type TeamSheetView = z.infer<typeof teamSheetView>;
export type TeamStatisticsView = z.infer<typeof teamStatisticsView>;
export type DeviceSettings = z.infer<typeof deviceSettings>;
export type PushKey = z.infer<typeof pushKey>;
export type SeasonSummary = z.infer<typeof seasonSummary>;
export type SeasonDetail = z.infer<typeof seasonDetail>;
export type LeagueRef = z.infer<typeof leagueRef>;
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
    Agrees<Equal<SeasonDetail, components["schemas"]["SeasonDetail"]>>,
    Agrees<Equal<LeagueRef, components["schemas"]["LeagueRef"]>>,
    Agrees<Equal<TeamRef, components["schemas"]["TeamRef"]>>,
    Agrees<Equal<MatchSummary, components["schemas"]["MatchSummary"]>>,
    Agrees<Equal<MatchEventView, components["schemas"]["MatchEventView"]>>,
    Agrees<Equal<CountrySummary, components["schemas"]["CountrySummary"]>>,
    Agrees<Equal<LeagueSummary, components["schemas"]["LeagueSummary"]>>,
    Agrees<Equal<LineupPlayerView, components["schemas"]["LineupPlayerView"]>>,
    Agrees<Equal<TeamSheetView, components["schemas"]["TeamSheetView"]>>,
    Agrees<Equal<TeamStatisticsView, components["schemas"]["TeamStatisticsView"]>>,
    Agrees<Equal<MatchDetail, components["schemas"]["MatchDetail"]>>,
    Agrees<Equal<PageOfMatchSummary, components["schemas"]["PageOfMatchSummary"]>>,
    Agrees<Equal<TableRow, components["schemas"]["TableRow"]>>,
    Agrees<Equal<ScorerRow, components["schemas"]["ScorerRow"]>>,
    Agrees<Equal<FormEntry, components["schemas"]["FormEntry"]>>,
];
