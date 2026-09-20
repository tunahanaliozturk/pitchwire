import { VueQueryPlugin } from "@tanstack/vue-query";
import { createPinia } from "pinia";
import { createApp } from "vue";

import { registerSW } from "virtual:pwa-register";

import App from "./App.vue";
import { router } from "./router";
import "./styles/tokens.css";

// Registered immediately and updated without asking. There is nothing to lose by taking the newest
// build: this application holds no unsaved work, and a stale worker is what serves a goal from
// yesterday's code.
registerSW({ immediate: true });

createApp(App)
    .use(createPinia())
    .use(router)
    .use(VueQueryPlugin, {
        queryClientConfig: {
            defaultOptions: {
                queries: {
                    // A live score list that refetches every time a tab regains focus is a live score list
                    // that hammers the service on a Saturday. The socket is what keeps it fresh.
                    refetchOnWindowFocus: false,
                    retry: 1,
                    staleTime: 10_000,
                },
            },
        },
    })
    .mount("#app");
