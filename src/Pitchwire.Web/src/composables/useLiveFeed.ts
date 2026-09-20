import {
    HubConnectionBuilder,
    HubConnectionState,
    LogLevel,
    type HubConnection,
} from "@microsoft/signalr";
import { useQueryClient } from "@tanstack/vue-query";
import { onScopeDispose, ref } from "vue";

import type { MatchSummary, PageOfMatchSummary } from "@/api/contracts";

/**
 * What the server pushes while a match is being played.
 *
 * The shape is the server's MatchUpdate. It is not in the OpenAPI document, because a hub is not
 * described by one, so this is the one contract in the application that is written out by hand.
 */
export interface MatchUpdate {
    matchId: string;
    homeTeamId: string;
    awayTeamId: string;
    sequence: number;
    minute: number;
    status: string;
    homeScore: number;
    awayScore: number;
    isDegraded: boolean;
    event: { sequence: number; minute: number; kind: string; teamId: string } | null;
}

let shared: HubConnection | null = null;

function connection(): HubConnection {
    // One connection for the whole application. A second socket would double the server's group
    // bookkeeping to deliver the same bytes twice to the same browser.
    shared ??= new HubConnectionBuilder()
        .withUrl("/api/hub/matches")
        .withAutomaticReconnect()
        .configureLogging(LogLevel.Warning)
        .build();

    return shared;
}

/**
 * Keeps the cached lists in step with what is happening on the pitch.
 *
 * Updates patch the cache rather than triggering a refetch. A goal in a busy round would otherwise
 * mean every watching browser asking for the whole live list at the same moment, which is the
 * opposite of what pushing an update is for.
 */
export function useLiveFeed() {
    const client = useQueryClient();
    const connected = ref(false);
    const lastGoalAt = ref<Record<string, number>>({});

    const apply = (update: MatchUpdate) => {
        const patch = (match: MatchSummary): MatchSummary =>
            match.id === update.matchId
                ? {
                      ...match,
                      minute: update.minute,
                      status: update.status,
                      homeScore: update.homeScore,
                      awayScore: update.awayScore,
                      isDegraded: update.isDegraded,
                  }
                : match;

        client.setQueriesData<PageOfMatchSummary>({ queryKey: ["live"] }, (page) =>
            page ? { ...page, value: page.value.map(patch) } : page,
        );

        if (
            update.event?.kind === "Goal" ||
            update.event?.kind === "PenaltyGoal" ||
            update.event?.kind === "OwnGoal"
        ) {
            lastGoalAt.value = { ...lastGoalAt.value, [update.matchId]: Date.now() };
        }

        // The detail screen owns the timeline, and a delta carries identifiers rather than names, so the
        // one screen that shows names asks for them again instead of being fed half a row.
        void client.invalidateQueries({ queryKey: ["match", update.matchId] });
    };

    const hub = connection();
    hub.on("matchUpdated", apply);

    const start = async () => {
        if (hub.state === HubConnectionState.Disconnected) {
            await hub.start();
        }

        await hub.invoke("WatchLive");
        connected.value = true;
    };

    void start().catch(() => {
        // A browser with no socket still sees the list, it just stops moving on its own. Saying so is the
        // screen's job rather than this one's.
        connected.value = false;
    });

    onScopeDispose(() => {
        hub.off("matchUpdated", apply);

        if (hub.state === HubConnectionState.Connected) {
            void hub.invoke("StopWatchingLive");
        }
    });

    return { connected, lastGoalAt };
}
