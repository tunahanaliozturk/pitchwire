import { expect, test, type Page } from "@playwright/test";

type Match = { id: string; round: number; home: { id: string }; away: { id: string } };
type MatchPage = { value: Match[] };

type ObservedWindow = Window & { pitchwireCspViolations?: string[] };

const observeCsp = (page: Page) =>
    page.addInitScript(() => {
        const violations: string[] = [];
        (window as ObservedWindow).pitchwireCspViolations = violations;
        window.addEventListener("securitypolicyviolation", (event) => {
            violations.push(`${event.effectiveDirective}: ${event.blockedURI}`);
        });
    });

const cspViolations = (page: Page) =>
    page.evaluate(() => (window as ObservedWindow).pitchwireCspViolations ?? []);

test("a competition change and combined filters reach the real read API", async ({ page }) => {
    const errors: string[] = [];
    page.on("pageerror", (error) => errors.push(error.message));
    await observeCsp(page);

    await page.goto("/fixtures");
    await expect(page.getByRole("heading", { name: "Fixtures" })).toBeVisible();

    const country = page.getByLabel("Country");
    await expect(country.locator("option")).toHaveCount(3);
    await country.selectOption("spain");

    const league = page.getByLabel("League");
    await expect(league.locator("option").first()).toHaveText("Iberia Primera");
    await league.selectOption({ label: "Iberia Segunda" });

    const round = page.getByLabel("Round");
    const team = page.getByLabel("Team");
    await expect(round.locator("option")).toHaveCount(10);
    await expect(team.locator("option", { hasText: "UD Almenara" })).toBeAttached();

    const lastRound = await round.locator("option:last-child").getAttribute("value");
    const firstTeam = await team.locator("option:nth-child(2)").getAttribute("value");
    expect(lastRound).not.toBeNull();
    expect(firstTeam).not.toBeNull();

    await round.selectOption(lastRound!);
    const filteredResponse = page.waitForResponse((response) => {
        const url = new URL(response.url());
        return (
            url.pathname.endsWith("/fixtures") &&
            url.searchParams.get("round") === lastRound &&
            url.searchParams.get("teamId") === firstTeam
        );
    });
    await team.selectOption(firstTeam!);

    const response = await filteredResponse;
    expect(response.ok()).toBe(true);
    const matches = (await response.json()) as MatchPage;
    expect(
        matches.value.every(
            (match) =>
                match.round === Number(lastRound) &&
                (match.home.id === firstTeam || match.away.id === firstTeam),
        ),
    ).toBe(true);
    await expect(page.locator("a.row")).toHaveCount(matches.value.length);
    expect(errors).toEqual([]);
    expect(await cspViolations(page)).toEqual([]);
});

test("a match is readable from a narrow screen, including every detail tab", async ({
    page,
    request,
}) => {
    const errors: string[] = [];
    page.on("pageerror", (error) => errors.push(error.message));
    await observeCsp(page);

    const seasonsResponse = await request.get("/api/seasons");
    expect(seasonsResponse.ok()).toBe(true);
    const seasons = (await seasonsResponse.json()) as { id: string }[];
    expect(seasons.length).toBeGreaterThan(0);

    const seasonId = seasons[0]!.id;
    const fixturesResponse = await request.get(`/api/seasons/${seasonId}/fixtures?$top=1`);
    expect(fixturesResponse.ok()).toBe(true);
    const fixtures = (await fixturesResponse.json()) as MatchPage;
    let match = fixtures.value[0];

    if (!match) {
        const resultsResponse = await request.get(`/api/seasons/${seasonId}/results?$top=1`);
        expect(resultsResponse.ok()).toBe(true);
        const results = (await resultsResponse.json()) as MatchPage;
        match = results.value[0];
    }

    expect(match).toBeDefined();
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto(`/matches/${match!.id}`);
    await expect(page.getByRole("tablist", { name: "Match detail" })).toBeVisible();

    for (const name of ["Summary", "Line-ups", "Statistics", "Ratings"]) {
        await page.getByRole("tab", { name }).click();
        await expect(page.getByRole("tab", { name })).toHaveAttribute("aria-selected", "true");
        await expect(page.getByRole("tabpanel")).toBeVisible();
    }

    await page
        .getByRole("navigation", { name: "Sections" })
        .getByRole("link", { name: "Results" })
        .click();
    await expect(page).toHaveURL(/\/results$/);
    await expect(page.getByRole("heading", { name: "Results" })).toBeVisible();
    expect(errors).toEqual([]);
    expect(await cspViolations(page)).toEqual([]);
});
