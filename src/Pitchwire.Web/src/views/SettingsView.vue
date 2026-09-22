<script setup lang="ts">
import { useQuery } from "@tanstack/vue-query";
import { computed, onMounted, reactive, watch } from "vue";

import { api } from "@/api/client";
import LeaguePicker from "@/components/LeaguePicker.vue";
import { useSeason } from "@/composables/useSeason";
import { useDeviceStore } from "@/stores/device";

const device = useDeviceStore();
const { current } = useSeason();

// The table is also the list of every team in the season, so there is no second endpoint and no
// second thing to keep in step.
const { data: teams } = useQuery({
    queryKey: computed(() => ["table", current.value?.id]),
    enabled: computed(() => current.value !== null),
    queryFn: () => api.table(current.value!.id),
});

const form = reactive({
    timeZoneId: Intl.DateTimeFormat().resolvedOptions().timeZone,
    quiet: false,
    quietHoursStart: "22:00",
    quietHoursEnd: "08:00",
    notifyOnGoal: true,
    notifyOnRedCard: false,
    notifyOnKickoff: false,
    notifyOnFullTime: true,
});

const saving = computed(() => device.busy);
const error = computed(() =>
    device.pushState === "denied"
        ? "Notifications are blocked in this browser. Turn them back on in its site settings."
        : null,
);

onMounted(async () => {
    await device.load();
});

watch(
    () => device.settings,
    (settings) => {
        if (!settings) {
            return;
        }

        form.timeZoneId = settings.timeZoneId;
        form.quiet = settings.quietHoursStart !== null;
        form.quietHoursStart = (settings.quietHoursStart ?? "22:00:00").slice(0, 5);
        form.quietHoursEnd = (settings.quietHoursEnd ?? "08:00:00").slice(0, 5);
        form.notifyOnGoal = settings.notifyOnGoal;
        form.notifyOnRedCard = settings.notifyOnRedCard;
        form.notifyOnKickoff = settings.notifyOnKickoff;
        form.notifyOnFullTime = settings.notifyOnFullTime;
    },
    { immediate: true },
);

const save = async () => {
    await device.savePreferences({
        timeZoneId: form.timeZoneId,
        // Both ends or neither. The server refuses half a window, because half a window is a rule nobody
        // can predict the behaviour of.
        quietHoursStart: form.quiet ? `${form.quietHoursStart}:00` : null,
        quietHoursEnd: form.quiet ? `${form.quietHoursEnd}:00` : null,
        notifyOnGoal: form.notifyOnGoal,
        notifyOnRedCard: form.notifyOnRedCard,
        notifyOnKickoff: form.notifyOnKickoff,
        notifyOnFullTime: form.notifyOnFullTime,
    });
};

const toggleTeam = async (teamId: string, event: Event) => {
    await device.setFavourite(teamId, (event.target as HTMLInputElement).checked);
};
</script>

<template>
    <section>
        <LeaguePicker />
        <h1>Settings</h1>

        <section class="block" aria-labelledby="push-heading">
            <h2 id="push-heading">Notifications</h2>

            <p v-if="device.pushState === 'unsupported'" class="note">
                This browser cannot show notifications when the tab is closed. Everything else still
                works, and scores update while you are looking.
            </p>
            <p v-else-if="error" class="note warn" role="alert">{{ error }}</p>
            <p v-else class="note">
                Goals from the teams you follow, even when this tab is closed.
            </p>

            <button
                v-if="device.pushState === 'off'"
                type="button"
                class="primary"
                :disabled="saving"
                @click="device.enablePush()"
            >
                Turn on notifications
            </button>
            <button
                v-else-if="device.pushState === 'on'"
                type="button"
                :disabled="saving"
                @click="device.disablePush()"
            >
                Turn off notifications
            </button>
        </section>

        <section class="block" aria-labelledby="teams-heading">
            <h2 id="teams-heading">Teams you follow</h2>

            <ul class="teams">
                <li v-for="row in teams ?? []" :key="row.team.id">
                    <input
                        :id="`follow-${row.team.id}`"
                        type="checkbox"
                        :checked="device.follows(row.team.id)"
                        @change="toggleTeam(row.team.id, $event)"
                    />
                    <label :for="`follow-${row.team.id}`">{{ row.team.name }}</label>
                </li>
            </ul>
        </section>

        <section class="block" aria-labelledby="when-heading">
            <h2 id="when-heading">What and when</h2>

            <fieldset>
                <legend>Tell me about</legend>
                <div class="choice">
                    <input id="notify-goal" v-model="form.notifyOnGoal" type="checkbox" />
                    <label for="notify-goal">Goals</label>
                </div>
                <div class="choice">
                    <input id="notify-red" v-model="form.notifyOnRedCard" type="checkbox" />
                    <label for="notify-red">Red cards</label>
                </div>
                <div class="choice">
                    <input id="notify-kickoff" v-model="form.notifyOnKickoff" type="checkbox" />
                    <label for="notify-kickoff">Kick off</label>
                </div>
                <div class="choice">
                    <input id="notify-fulltime" v-model="form.notifyOnFullTime" type="checkbox" />
                    <label for="notify-fulltime">Full time</label>
                </div>
            </fieldset>

            <fieldset>
                <legend>Quiet hours</legend>
                <div class="choice">
                    <input id="quiet" v-model="form.quiet" type="checkbox" />
                    <label for="quiet">Do not disturb between</label>
                </div>

                <div v-if="form.quiet" class="window">
                    <div class="choice">
                        <label for="quiet-from">From</label>
                        <input id="quiet-from" v-model="form.quietHoursStart" type="time" />
                    </div>
                    <div class="choice">
                        <label for="quiet-until">Until</label>
                        <input id="quiet-until" v-model="form.quietHoursEnd" type="time" />
                    </div>
                </div>

                <p class="note">
                    Read in your own time zone, {{ form.timeZoneId }}, so a window that crosses
                    midnight means what you would expect it to.
                </p>
            </fieldset>

            <button type="button" class="primary" :disabled="saving" @click="save">Save</button>
        </section>
    </section>
</template>

<style scoped>
h1 {
    font-size: var(--text-xl);
    margin: 16px 12px 12px;
}

h2 {
    font-size: var(--text-base);
    font-weight: 600;
    margin: 0 0 8px;
}

.block {
    padding: 16px 12px;
    border-bottom: 1px solid var(--line);
}

.note {
    color: var(--muted);
    font-size: var(--text-sm);
    margin: 0 0 12px;
}

.note.warn {
    color: var(--warn);
}

.teams {
    list-style: none;
    margin: 0;
    padding: 0;
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
    gap: 4px;
}

fieldset {
    border: 0;
    margin: 0 0 16px;
    padding: 0;
    display: grid;
    gap: 4px;
}

legend {
    font-size: var(--text-sm);
    color: var(--muted);
    padding: 0 0 6px;
}

.choice,
.teams li {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 4px 0;
}

.window {
    display: flex;
    gap: var(--gap);
    padding: 8px 0;
}

.window .choice {
    gap: 6px;
}

input[type="time"] {
    font: inherit;
    padding: 4px 6px;
    border: 1px solid var(--line);
    border-radius: var(--radius);
    background: var(--surface);
    color: var(--ink);
}

button {
    font: inherit;
    padding: 8px 14px;
    border: 1px solid var(--line);
    border-radius: var(--radius);
    background: var(--surface);
    color: var(--ink);
    cursor: pointer;
}

button.primary {
    background: var(--accent);
    border-color: var(--accent);
    color: #06281f;
    font-weight: 600;
}

button:disabled {
    opacity: 0.6;
    cursor: default;
}
</style>
