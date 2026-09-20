import { VueQueryPlugin } from "@tanstack/vue-query";
import { createPinia } from "pinia";
import { createApp } from "vue";

import App from "./App.vue";
import { router } from "./router";
import "./styles/tokens.css";

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
