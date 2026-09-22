<script setup lang="ts">
import { computed } from "vue";

import type { LineupPlayerView, TeamSheetView } from "@/api/contracts";

const props = defineProps<{
    home: TeamSheetView | null;
    away: TeamSheetView | null;
    homeName: string;
    awayName: string;
}>();

type Spot = { player: LineupPlayerView; x: number; y: number };

/**
 * The starting eleven, arranged the way the formation says.
 *
 * The rows come from the formation string rather than from the positions, because a team sheet that
 * reads 3-5-2 has to look like 3-5-2 even when the wing back in the middle row is listed as a
 * defender. The sheet arrives keeper first and sorted by position, so filling the rows in order puts
 * everybody where the manager said they would be.
 */
const arrange = (sheet: TeamSheetView | null, mirrored: boolean): Spot[] => {
    if (sheet === null) {
        return [];
    }

    const starters = sheet.players.filter((player) => player.starter);

    if (starters.length === 0) {
        return [];
    }

    const shape = sheet.formation
        .split("-")
        .map((band) => Number.parseInt(band, 10))
        .filter((band) => Number.isInteger(band) && band > 0);

    // A keeper and whatever the formation did not account for. Without the remainder a corrected
    // sheet with twelve names would quietly lose the twelfth.
    const named = shape.reduce((total, band) => total + band, 0);
    const rows = [1, ...shape];

    if (starters.length > named + 1) {
        rows.push(starters.length - named - 1);
    }

    const spots: Spot[] = [];
    let taken = 0;

    rows.forEach((count, index) => {
        const band = starters.slice(taken, taken + count);
        taken += band.length;

        // Half the pitch, from in front of the goal line to just short of halfway. The keeper starts
        // at 7 rather than on the line so the name underneath has somewhere to go.
        const depth = rows.length === 1 ? 0.5 : index / (rows.length - 1);
        const y = 7 + depth * 37;

        band.forEach((player, position) => {
            const across = (position + 1) / (band.length + 1);

            spots.push({
                player,
                x: 8 + across * 84,
                y: mirrored ? 100 - y : y,
            });
        });
    });

    return spots;
};

const homeSpots = computed(() => arrange(props.home, true));
const awaySpots = computed(() => arrange(props.away, false));

/** The name on the back of the shirt. First names do not fit on a pitch and nobody reads them. */
const surname = (name: string) => name.split(" ").at(-1) ?? name;
</script>

<template>
    <div class="wrap">
        <p class="cap">
            {{ awayName }} <span>{{ away?.formation }}</span>
        </p>

        <div class="pitch">
            <svg
                class="markings"
                viewBox="0 0 68 105"
                preserveAspectRatio="none"
                aria-hidden="true"
            >
                <g fill="none" stroke="var(--pitch-line)" stroke-width="0.4">
                    <rect x="1" y="1" width="66" height="103" />
                    <line x1="1" y1="52.5" x2="67" y2="52.5" />
                    <circle cx="34" cy="52.5" r="9.15" />

                    <rect x="13.85" y="1" width="40.3" height="16.5" />
                    <rect x="24.85" y="1" width="18.3" height="5.5" />

                    <rect x="13.85" y="87.5" width="40.3" height="16.5" />
                    <rect x="24.85" y="98.5" width="18.3" height="5.5" />
                </g>
            </svg>

            <div
                v-for="spot in [...awaySpots, ...homeSpots]"
                :key="spot.player.playerId"
                class="spot"
                :style="{ left: `${spot.x}%`, top: `${spot.y}%` }"
            >
                <span class="shirt tabular">
                    {{ spot.player.shirtNumber }}
                    <i v-if="spot.player.goals > 0" class="scored" aria-hidden="true"></i>
                    <i v-if="spot.player.redCards > 0" class="card red" aria-hidden="true"></i>
                    <i
                        v-else-if="spot.player.yellowCards > 0"
                        class="card yellow"
                        aria-hidden="true"
                    ></i>
                </span>

                <span class="name">{{ surname(spot.player.player) }}</span>
            </div>
        </div>

        <p class="cap">
            {{ homeName }} <span>{{ home?.formation }}</span>
        </p>
    </div>
</template>

<style scoped>
.wrap {
    padding: 12px;
}

.pitch {
    position: relative;
    aspect-ratio: 68 / 105;
    max-width: 420px;
    margin: 0 auto;
    background: var(--pitch);
    border-radius: var(--radius);
    overflow: hidden;
}

.markings {
    position: absolute;
    inset: 0;
    width: 100%;
    height: 100%;
}

/* Outside the pitch rather than on it. Inside, a caption long enough to name a club and a formation
   lands on top of whoever is playing wide. */
.cap {
    margin: 0 0 8px;
    text-align: center;
    font-size: var(--text-xs);
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.06em;
}

.cap:last-child {
    margin: 8px 0 0;
}

.cap span {
    color: var(--muted);
    font-weight: 500;
}

.spot {
    position: absolute;
    transform: translate(-50%, -50%);
    display: grid;
    justify-items: center;
    gap: 2px;
    width: 3.6rem;
}

.shirt {
    position: relative;
    display: grid;
    place-items: center;
    width: 1.65rem;
    height: 1.65rem;
    border-radius: 50%;
    background: rgb(255 255 255 / 92%);
    color: #0e1116;
    font-size: var(--text-xs);
    font-weight: 700;
}

.scored,
.card {
    position: absolute;
    top: -2px;
    width: 7px;
    height: 7px;
}

.scored {
    right: -3px;
    border-radius: 50%;
    background: #0e1116;
    box-shadow: 0 0 0 1.5px rgb(255 255 255 / 92%);
}

.card {
    left: -3px;
    height: 9px;
    width: 6px;
    border-radius: 1px;
}

.card.red {
    background: #d64545;
}

.card.yellow {
    background: #e0b13a;
}

.name {
    font-size: 0.625rem;
    line-height: 1.2;
    color: #fff;
    text-align: center;
    text-shadow: 0 1px 2px rgb(0 0 0 / 55%);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    max-width: 100%;
}
</style>
