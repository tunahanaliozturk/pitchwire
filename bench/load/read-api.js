import { check, sleep } from "k6";
import http from "k6/http";
import { Trend } from "k6/metrics";

/**
 * What a matchday evening looks like from the read side.
 *
 * Everybody opens the live list, some of them open a match, and a smaller number look at a table or a
 * scorers list. The mix matters more than the total: a test that only hammers one endpoint measures
 * one cache entry, and the live list is the one request in this application that cannot be cached for
 * long because it is the thing that keeps changing.
 *
 * Run it against a stack that is already up:
 *
 *   docker compose up -d --wait
 *   docker run --rm -i --network host -e BASE_URL=http://127.0.0.1:5080 \
 *     grafana/k6 run - < bench/load/read-api.js
 */
const base = __ENV.BASE_URL || "http://127.0.0.1:5080";

const liveLatency = new Trend("live_latency", true);
const matchLatency = new Trend("match_latency", true);

export const options = {
    scenarios: {
        matchday: {
            executor: "ramping-vus",
            startVUs: 0,
            stages: [
                { duration: "20s", target: 50 },
                { duration: "40s", target: 50 },
                { duration: "10s", target: 0 },
            ],
        },
    },
    thresholds: {
        // A live list that takes longer than this stops being live. The number is deliberately the one
        // quoted in the README, so a regression fails here rather than in somebody's memory.
        live_latency: ["p(95)<150"],
        match_latency: ["p(95)<250"],
        http_req_failed: ["rate<0.01"],
    },
};

export function setup() {
    const live = http.get(`${base}/matches/live?$top=50`);

    if (live.status !== 200) {
        throw new Error(`the stack is not answering: /matches/live returned ${live.status}`);
    }

    const matches = live.json("value") || [];
    const countries = http.get(`${base}/countries`).json() || [];
    const leagues =
        countries.length > 0 ? http.get(`${base}/countries/${countries[0].slug}/leagues`).json() : [];

    return {
        matchIds: matches.map((match) => match.id),
        seasonId: leagues.length > 0 ? leagues[0].currentSeasonId : null,
    };
}

export default function (data) {
    const live = http.get(`${base}/matches/live?$top=50`, { tags: { name: "live" } });
    liveLatency.add(live.timings.duration);
    check(live, { "live list answered": (response) => response.status === 200 });

    // Roughly one reader in two opens a match, and one in five looks at a table or the scorers.
    if (data.matchIds.length > 0 && Math.random() < 0.5) {
        const id = data.matchIds[Math.floor(Math.random() * data.matchIds.length)];
        const match = http.get(`${base}/matches/${id}`, { tags: { name: "match" } });
        matchLatency.add(match.timings.duration);
        check(match, { "match answered": (response) => response.status === 200 });
    }

    if (data.seasonId && Math.random() < 0.2) {
        const table = http.get(`${base}/seasons/${data.seasonId}/table`, { tags: { name: "table" } });
        check(table, { "table answered": (response) => response.status === 200 });
    }

    if (data.seasonId && Math.random() < 0.1) {
        const scorers = http.get(`${base}/seasons/${data.seasonId}/scorers`, {
            tags: { name: "scorers" },
        });
        check(scorers, { "scorers answered": (response) => response.status === 200 });
    }

    sleep(1);
}
