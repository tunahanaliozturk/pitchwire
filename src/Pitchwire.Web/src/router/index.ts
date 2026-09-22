import { createRouter, createWebHistory, type RouteRecordRaw } from "vue-router";

/**
 * Six screens, each one lazily loaded.
 *
 * A single bundle means somebody opening one live match downloads the league table, the fixture list
 * and everything else. Route level splitting is the cheapest performance decision available and it
 * costs one arrow function per route.
 */
const routes: RouteRecordRaw[] = [
    {
        path: "/",
        name: "live",
        component: () => import("@/views/LiveView.vue"),
        meta: { title: "Live" },
    },
    {
        path: "/fixtures",
        name: "fixtures",
        component: () => import("@/views/FixturesView.vue"),
        meta: { title: "Fixtures" },
    },
    {
        path: "/table",
        name: "table",
        component: () => import("@/views/TableView.vue"),
        meta: { title: "Table" },
    },
    {
        path: "/scorers",
        name: "scorers",
        component: () => import("@/views/ScorersView.vue"),
        meta: { title: "Scorers" },
    },
    {
        path: "/settings",
        name: "settings",
        component: () => import("@/views/SettingsView.vue"),
        meta: { title: "Settings" },
    },
    {
        path: "/matches/:matchId",
        name: "match",
        component: () => import("@/views/MatchView.vue"),
        props: true,
        meta: { title: "Match" },
    },
    {
        path: "/:pathMatch(.*)*",
        name: "not-found",
        component: () => import("@/views/NotFoundView.vue"),
        meta: { title: "Not found" },
    },
];

export const router = createRouter({
    history: createWebHistory(),
    routes,
    scrollBehavior: () => ({ top: 0 }),
});

router.afterEach((to) => {
    const title = typeof to.meta.title === "string" ? to.meta.title : "";
    document.title = title ? `${title} · pitchwire` : "pitchwire";
});
