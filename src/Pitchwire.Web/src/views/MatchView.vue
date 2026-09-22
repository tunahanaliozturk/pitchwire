<script setup lang="ts">
import { useQuery } from "@tanstack/vue-query";
import { computed, ref, toRef } from "vue";

import { api } from "@/api/client";
import type { LineupPlayerView, TeamSheetView } from "@/api/contracts";
import MatchTimeline from "@/components/MatchTimeline.vue";
import PitchFormation from "@/components/PitchFormation.vue";
import RatingChip from "@/components/RatingChip.vue";
import StatBar from "@/components/StatBar.vue";

const props = defineProps<{ matchId: string }>();

// A getter rather than the value. Watching the value would pin this query to whatever the parameter
// was when the component was created, and moving between two matches would show the first one twice.
const matchId = toRef(props, "matchId");

const { data, isLoading, error } = useQuery({
    queryKey: computed(() => ["match", matchId.value]),
    queryFn: () => api.match(matchId.value),
    refetchInterval: 30_000,
});

const tabs = ["Summary", "Line-ups", "Statistics", "Ratings"] as const;
type Tab = (typeof tabs)[number];
const tab = ref<Tab>("Summary");

const match = computed(() => data.value?.match ?? null);

const side = (teamId: string | undefined) =>
    data.value?.lineups.find((lineup) => lineup.teamId === teamId) ?? null;

const home = computed(() => side(match.value?.home.id));
const away = computed(() => side(match.value?.away.id));

const statsFor = (teamId: string | undefined) =>
    data.value?.statistics.find((stats) => stats.teamId === teamId) ?? null;

const homeStats = computed(() => statsFor(match.value?.home.id));
const awayStats = computed(() => statsFor(match.value?.away.id));
const hasStats = computed(() => homeStats.value !== null && awayStats.value !== null);

const hasLineups = computed(() => (data.value?.lineups.length ?? 0) > 0);

// Positions as a team sheet prints them. The first three letters of the word give GOA, which is not
// what anybody calls a goalkeeper.
const short: Record<string, string> = {
    Goalkeeper: "GK",
    Defender: "DF",
    Midfielder: "MF",
    Forward: "FW",
};

/** Where the substitutes start, so the eleven and the bench are not one list of eighteen. */
const firstOnTheBench = (sheet: TeamSheetView | null) =>
    sheet?.players.find((player) => !player.starter)?.playerId ?? null;

/** Highest rated first, and anybody who played too little to judge last rather than at the top. */
const rated = (players: LineupPlayerView[] | undefined) =>
    [...(players ?? [])].sort((left, right) => (right.rating ?? -1) - (left.rating ?? -1));

const kickoff = computed(() =>
    match.value === null
        ? ""
        : new Date(match.value.kickoffUtc).toLocaleString(undefined, {
              weekday: "short",
              day: "numeric",
              month: "short",
              hour: "2-digit",
              minute: "2-digit",
          }),
);
</script>

<template>
    <section>
        <p v-if="isLoading" class="state">Loading.</p>
        <p v-else-if="error" class="state" role="alert">That match could not be loaded.</p>

        <template v-else-if="match && data">
            <header class="head">
                <p class="when">Round {{ match.round }} · {{ kickoff }}</p>

                <div class="score">
                    <span class="team home">{{ match.home.name }}</span>
                    <span class="numbers tabular"
                        >{{ match.homeScore }}<i>-</i>{{ match.awayScore }}</span
                    >
                    <span class="team away">{{ match.away.name }}</span>
                </div>

                <p class="status" :class="{ live: match.status === 'Live' }">
                    {{ match.status === "Live" ? `${match.minute}'` : match.status }}
                    <span v-if="match.isDegraded" class="degraded">· some events are missing</span>
                </p>
            </header>

            <div class="tabs" role="tablist" aria-label="Match detail">
                <button
                    v-for="name in tabs"
                    :key="name"
                    type="button"
                    role="tab"
                    :aria-selected="tab === name"
                    :class="{ on: tab === name }"
                    @click="tab = name"
                >
                    {{ name }}
                </button>
            </div>

            <div v-if="tab === 'Summary'" role="tabpanel">
                <p v-if="data.timeline.length === 0" class="state">Nothing has happened yet.</p>
                <MatchTimeline
                    v-else
                    :timeline="data.timeline"
                    :home-team-id="match.home.id"
                    :home-short-name="match.home.shortName"
                    :away-short-name="match.away.shortName"
                />
            </div>

            <div v-else-if="tab === 'Line-ups'" role="tabpanel">
                <p v-if="!hasLineups" class="state">The team sheets have not been published yet.</p>

                <template v-else>
                    <PitchFormation
                        :home="home"
                        :away="away"
                        :home-name="match.home.name"
                        :away-name="match.away.name"
                    />

                    <div class="sheets">
                        <div v-for="sheet in [home, away]" :key="sheet?.teamId ?? ''" class="sheet">
                            <h2>
                                {{
                                    sheet?.teamId === match.home.id
                                        ? match.home.name
                                        : match.away.name
                                }}
                                <span class="formation">{{ sheet?.formation }}</span>
                            </h2>

                            <ul>
                                <template
                                    v-for="player in sheet?.players ?? []"
                                    :key="player.playerId"
                                >
                                    <li
                                        v-if="firstOnTheBench(sheet) === player.playerId"
                                        class="divide"
                                    >
                                        Substitutes
                                    </li>

                                    <li :class="{ bench: !player.starter }">
                                        <span class="shirt tabular">{{ player.shirtNumber }}</span>
                                        <span class="name">{{ player.player }}</span>
                                        <span class="position">{{ short[player.position] }}</span>

                                        <span
                                            v-if="player.goals > 0"
                                            class="marks scored"
                                            :aria-label="`${player.goals} scored`"
                                        >
                                            <i v-for="goal in player.goals" :key="goal"></i>
                                        </span>

                                        <span v-if="player.redCards > 0" class="marks red">■</span>
                                        <span
                                            v-else-if="player.yellowCards > 0"
                                            class="marks yellow"
                                            >■</span
                                        >
                                    </li>
                                </template>
                            </ul>
                        </div>
                    </div>
                </template>
            </div>

            <div v-else-if="tab === 'Statistics'" role="tabpanel" class="stats">
                <p v-if="!hasStats" class="state">
                    No statistics have been published for this match.
                </p>

                <template v-else-if="homeStats && awayStats">
                    <p class="asof">As of minute {{ homeStats.asOfMinute }}</p>
                    <StatBar
                        label="Possession %"
                        :home="homeStats.possession"
                        :away="awayStats.possession"
                    />
                    <StatBar label="Shots" :home="homeStats.shots" :away="awayStats.shots" />
                    <StatBar
                        label="On target"
                        :home="homeStats.shotsOnTarget"
                        :away="awayStats.shotsOnTarget"
                    />
                    <StatBar label="Corners" :home="homeStats.corners" :away="awayStats.corners" />
                    <StatBar label="Fouls" :home="homeStats.fouls" :away="awayStats.fouls" />
                    <StatBar
                        label="Offsides"
                        :home="homeStats.offsides"
                        :away="awayStats.offsides"
                    />
                </template>
            </div>

            <div v-else role="tabpanel">
                <p v-if="!hasLineups" class="state">
                    Ratings need a team sheet, and none has arrived yet.
                </p>

                <div v-else class="sheets">
                    <div
                        v-for="sheet in [home, away]"
                        :key="`${sheet?.teamId}-ratings`"
                        class="sheet"
                    >
                        <h2>
                            {{
                                sheet?.teamId === match.home.id ? match.home.name : match.away.name
                            }}
                        </h2>

                        <ul class="ratings">
                            <li v-for="player in rated(sheet?.players)" :key="player.playerId">
                                <RatingChip :rating="player.rating" />
                                <span class="name">{{ player.player }}</span>
                                <span class="minutes tabular">{{ player.minutesPlayed }}'</span>
                            </li>
                        </ul>
                    </div>
                </div>

                <p class="note">
                    Ratings are worked out from what the match log records: goals, assists, cards,
                    the result, and what got past a goalkeeper or defender. Anybody on for less than
                    twenty minutes is not rated.
                </p>
            </div>
        </template>
    </section>
</template>

<style scoped>
.head {
    padding: 18px 12px 14px;
    border-bottom: 1px solid var(--line);
    text-align: center;
}

.when {
    margin: 0 0 10px;
    color: var(--muted);
    font-size: var(--text-xs);
    text-transform: uppercase;
    letter-spacing: 0.06em;
}

.score {
    display: grid;
    grid-template-columns: 1fr auto 1fr;
    align-items: center;
    gap: var(--gap);
}

.team {
    font-size: var(--text-lg);
    font-weight: 600;
    line-height: 1.2;
}

.team.home {
    text-align: right;
}

.team.away {
    text-align: left;
}

.numbers {
    font-size: 2rem;
    font-weight: 700;
    letter-spacing: -0.02em;
}

.numbers i {
    font-style: normal;
    color: var(--muted);
    margin: 0 6px;
}

.status {
    margin: 10px 0 0;
    color: var(--muted);
    font-size: var(--text-sm);
}

.status.live {
    color: var(--accent);
    font-weight: 600;
}

.degraded {
    color: var(--warn);
}

.tabs {
    display: flex;
    gap: 2px;
    padding: 8px 12px 0;
    border-bottom: 1px solid var(--line);
}

.tabs button {
    font: inherit;
    font-size: var(--text-sm);
    padding: 8px 12px;
    border: 0;
    border-bottom: 2px solid transparent;
    background: none;
    color: var(--muted);
    cursor: pointer;
}

.tabs button.on {
    color: var(--ink);
    border-bottom-color: var(--accent);
    font-weight: 600;
}

.state,
.note {
    color: var(--muted);
    font-size: var(--text-sm);
    padding: 16px 12px;
    margin: 0;
}

.sheets {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
}

.sheet {
    padding: 12px;
    border-right: 1px solid var(--line);
}

.sheet:last-child {
    border-right: 0;
}

h2 {
    display: flex;
    justify-content: space-between;
    align-items: baseline;
    gap: var(--gap);
    font-size: var(--text-sm);
    font-weight: 700;
    margin: 0 0 8px;
}

.formation {
    color: var(--muted);
    font-weight: 500;
}

ul {
    list-style: none;
    margin: 0;
    padding: 0;
}

li {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 5px 0;
    border-bottom: 1px solid var(--line);
    font-size: var(--text-sm);
}

li.bench {
    color: var(--muted);
}

.shirt {
    width: 1.5rem;
    color: var(--muted);
    text-align: right;
}

.name {
    flex: 1;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
}

.position,
.minutes {
    color: var(--muted);
    font-size: var(--text-xs);
}

/* One dot per goal. A colour emoji in the middle of a team sheet is the only thing on the page that
   comes from somewhere else. */
.marks.scored {
    display: inline-flex;
    gap: 3px;
}

.marks.scored i {
    width: 6px;
    height: 6px;
    border-radius: 50%;
    background: var(--accent);
}

.divide {
    color: var(--muted);
    font-size: var(--text-xs);
    text-transform: uppercase;
    letter-spacing: 0.06em;
    padding-top: 12px;
    border-bottom: 0;
}

.marks.red {
    color: #d64545;
}

.marks.yellow {
    color: #d9a441;
}

.ratings li {
    gap: 10px;
}

.stats {
    padding: 8px 12px 16px;
}

.asof {
    margin: 4px 0 8px;
    color: var(--muted);
    font-size: var(--text-xs);
    text-align: center;
}
</style>
