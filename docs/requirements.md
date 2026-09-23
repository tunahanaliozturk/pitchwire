# pitchwire requirements

A live football score service. It shows what is happening in a match right now, tells you when your team
scores even if the tab is closed, and keeps the fixtures, tables and season history that make a score
worth reading.

This document is the agreed scope. It states what the system does, what it refuses to do, and the numbers
it has to hit before the work counts as finished.

## 1. The problem worth solving

A score app looks trivial from the outside. Two numbers and a clock. The difficulty is not in showing the
score, it is in everything around the feed that produces it.

Real score providers are unreliable in specific, well known ways. They send the same goal twice because a
retry crossed with an acknowledgement. They deliver the 42nd minute before the 40th because two workers
raced. They go quiet for thirty seconds and then hand over a burst of twelve events at once. They
occasionally skip an event entirely and never mention it. A client that assumes a clean, ordered, exactly
once stream will show a 3-2 that should be 2-2, and it will show it confidently.

So the centre of this project is the ingestion boundary: accept a hostile feed and still be correct. The
score on screen is the visible part. The part worth reviewing is what happens to a duplicate goal.

The second real problem is delivery. A notification promise is only kept if it survives a closed tab, a
restarted server and a subscription that quietly died three weeks ago. Writing the goal to the database
and pushing the notification have to succeed or fail together, otherwise users get notified about goals
that were rolled back, or miss goals that were saved.

## 2. Scope

### In scope

* Live match view with minute by minute events, updated without a page refresh.
* Several leagues across several countries, chosen by country and then by league.
* Match detail with the event timeline, lineups, substitutions and match statistics.
* Fixtures and results, by league, by round and by team, across multiple leagues and past seasons.
* League tables, top scorers and recent form, all derived from match events.
* Favourite teams, stored per device, with no account to create.
* Goal and match notifications delivered by Web Push, including when the browser tab is closed.
* Per event type notification preferences and quiet hours in the device's own timezone.
* A feed simulator that behaves like a real provider, including its failure modes.

### Out of scope

* User accounts, passwords, email delivery and cross device synchronisation. Favourites belong to a
  device. A separate project in this portfolio already covers identity properly, and repeating it here
  would add screens without adding anything to review.
* Betting odds, live streaming and any paid data licence.
* Native mobile applications. The web app is installable and receives push notifications, which covers the
  same need without a second codebase.
* Editorial content, comments and social features.
* Multi region deployment and horizontal scale out. The service runs as a single instance by design. What
  a second instance would require is written down in ADR 0008 rather than built.

## 3. Architecture

Three processes, one solution, one `docker compose up`.

**`pitchwire.feed`** simulates a match data provider. It runs matches on an accelerated match clock and
pushes events over HTTP. It has no privileged access: it cannot see the database and it talks to the API
through exactly the interface a third party provider would use. It also misbehaves on purpose, at
configurable rates, by duplicating events, reordering them, and pausing before sending a burst. Those are
not test tricks, they are what real providers do, and the system has to survive them in the running app
and not only in a test.

**`pitchwire.api`** owns the data. It accepts events at the ingestion boundary, derives match state from
them, serves the read model, and distributes updates over SignalR and Web Push. It is built as four
projects with the dependency arrow pointing inward: `Domain` holds the entities and the rules that act
on them and depends on nothing else, `Application` holds the use cases and the ports they need,
`Infrastructure` holds the database, the provider client and the background workers, and `Api` is the
host that wires them together and exposes the endpoints. A test reads what each assembly actually
compiled against, so a using statement in the wrong direction fails the build rather than being caught
in review.

**`pitchwire.web`** is the Vue application, served as static files.

PostgreSQL is the only data store. Redis backs the second level of the cache. There is no message broker,
because an outbox table and a relay already do the job that a broker would do here, with one less service
to run and one less failure mode to explain.

Swapping the simulator for a real provider means implementing `IMatchFeed` and deleting a container from
the compose file. Nothing in the ingestion pipeline changes. That is the point of running the simulator as
a separate process rather than a background task inside the API.

## 4. Functional requirements

### Ingestion

**FR-1** The API accepts batches of match events at `POST /ingest/events` and answers `202 Accepted` with
a count of accepted, duplicate and rejected events. No event is discarded silently.

**FR-2** Every request carries an HMAC-SHA256 signature over the body and an `X-Pitchwire-Timestamp`
header. Signatures are compared in constant time. Requests older than five minutes are rejected as
replays. An invalid signature returns `401` and writes nothing.

**FR-3** Duplicate events are absorbed without changing the score. Uniqueness is enforced by a database
constraint on `(provider, provider_event_id)`, not by a read before write, because two concurrent requests
can both pass a read check.

**FR-4** Events carry a per match sequence number. An event whose sequence is not the next one is handled
according to its direction:

* Sequence lower than or equal to the last applied sequence: the event is stored, because the event log is
  the source of truth, but the score is never walked backwards. The match's derived state is recomputed
  from its events instead.
* Sequence higher than the next expected one: the match is marked degraded and the API pulls the missing
  range from the provider at `GET /matches/{id}/events?from={n}`. The mark clears once the gap closes.

**FR-5** Event ingestion, match state update, projection update and notification outbox writes happen in
one database transaction. SignalR broadcast and cache invalidation happen after that transaction commits,
never inside it.

**FR-6** Events for one match are applied in series, guarded by a row lock on that match. Events for
different matches are applied concurrently.

**FR-7** The match minute comes from the event. Server time is never used to derive it, because server
time does not know about half time, stoppage time or a suspended match.

### Reading

**FR-8** Clients can list live matches, fixtures by league and round, results, a single match with its
full event timeline, league tables, top scorers and team form.

**FR-9** Collection endpoints are paginated in the style used by Microsoft's REST APIs: the response is an
object with a `value` array and, when more results exist, a `nextLink`. Page size is `$top`, defaulting to
50 and capped at 100. Continuation uses an opaque `$skiptoken`.

**FR-10** `$skip` is not supported. Deep offsets scan linearly and they skip or repeat rows when the
underlying set changes between requests, which a live fixture list does constantly. The continuation token
encodes a keyset instead.

**FR-11** A malformed continuation token returns `400`. It does not silently fall back to the first page,
because a client that pages through a list and starts again from the top will duplicate data without ever
seeing an error.

**FR-12** League tables, top scorers and form are read from projections maintained during ingestion, not
computed by scanning matches at request time. A season's projections are rebuilt from its own finished
matches and event log inside the ingestion transaction, rather than adjusted in place. Rebuilding cannot
double count when a match finishes twice, when a late goal changes a result after the points were
awarded, or when a rebuild moves a score after the fact, and each of those is a way an adjusted table
goes quietly wrong. A test asserts that projecting the same season twice changes nothing.

### Live updates

**FR-13** Connected clients receive score and event updates over SignalR. Subscriptions are grouped by
match for the detail view, by team for favourites, and by a single live group for the list view, so that a
busy evening costs one group membership per client rather than one per match.

**FR-14** Updates are deltas, not full match documents. The event inside a delta carries identifiers
rather than player names: names would mean a lookup on the way out of every goal, and a client showing
a scorer either holds the squad already or asks for the timeline.

**FR-15** After a dropped connection, the client reconnects with the last sequence it saw and receives
what it missed, from `GET /matches/{id}/events?from={n}`. A reconnect does not leave a hole in the
timeline, and it does not refetch a match from the first minute to find three events.

**FR-16** When the browser tab goes to the background the connection closes, and it reopens with a
catch-up when the tab returns. Holding a socket open in a background tab spends battery for updates nobody
is looking at.

### Notifications

**FR-17** A device is identified by a token issued on first visit and stored in an `HttpOnly`, `Secure`,
`SameSite=Lax` cookie. The database stores only a hash of the token. The token itself never appears in a
log.

**FR-18** A device can mark teams as favourites and can subscribe to Web Push.

**FR-19** A notification is sent only when all three of these hold: the device follows the team, the event
type is enabled in that device's preferences, and the current time falls outside the device's quiet hours.

**FR-20** Quiet hours are evaluated in the device's own timezone and must handle a window that crosses
midnight, such as 22:00 to 08:00.

**FR-21** Notifications are queued in an outbox row written in the same transaction as the event, and
delivered by a separate relay. The relay claims rows with `FOR UPDATE SKIP LOCKED`, retries with
exponential backoff, and treats `404` and `410` as permanent: the subscription is deleted rather than
retried forever.

**FR-22** One event produces at most one notification per device, enforced by a unique constraint on
`(device_id, match_event_id)`.

**FR-23** Clicking a notification opens that match. If the app is already open it is focused rather than
duplicated in a new tab. The destination is checked before it is used: only a path inside this
application is accepted, and a protocol relative one such as `//elsewhere` is refused along with an
absolute URL. A notification that can send somebody anywhere is a notification worth sending for the
wrong reasons.

### Squads, statistics and ratings

**FR-29** Leagues are grouped by country, and a country's leagues are listed top flight first. The
countries are real and every club, league and player is invented: real clubs would put somebody else's
trademarks in a demo, and invented ones in a real country let a reader see names that look like home
without anybody's badge on them.

**FR-30** A match carries both team sheets, with a formation, a starting eleven and a bench. Team
sheets are snapshots rather than events, so they have no sequence: a later one replaces the earlier
one entirely, because a player dropped from a corrected sheet has to disappear rather than linger.

**FR-31** A match carries per side statistics: possession, shots, shots on target, corners, fouls and
offsides. They are cumulative snapshots, so only the newest is kept and one that arrives from an
earlier minute is ignored. Writing it would walk the shot count backwards, which is the statistics
version of a score going down.

**FR-32** Every player named on a sheet gets the minutes they played, worked out from the
substitutions and dismissals in the event log rather than reported separately. A reported number could
disagree with the log, and then one of the two would be wrong with nothing to say which.

**FR-33** A player who was on the pitch long enough gets a match rating derived from the log: goals,
penalties, own goals, assists, cards, the result, and for a goalkeeper or defender what got past them.
It is a model and a deliberately simple one, not a scout's opinion, and every point in it comes from
something recorded. A player on for less than twenty minutes is not rated at all, because a number for
three minutes of stoppage time would claim to know something the log does not.

**FR-34** Every league in the catalogue plays, not only the first one. A country and league picker in
front of five competitions that never kick off is a menu of empty rooms, and the grouping on the live
board only earns its keep when more than one league is playing at once.

### Frontend

**FR-35** Team sheets are drawn on a pitch as well as listed, with the home side at the bottom. The
rows come from the formation string rather than from the recorded positions, so a sheet that reads
3-5-2 looks like 3-5-2 even where the wing back in the middle band is listed as a defender. Anybody
the formation does not account for is still placed: a corrected sheet with twelve names must not
quietly lose the twelfth.

**FR-36** The timeline reads as a match report rather than as a log. Periods are named at the ground's
own words, so "Period ended 48'" followed by "Period started 46'" becomes half time and second half
with no minute to argue with. A substitution names both players, and events are attributed to a team
by its short name rather than as home or away.

**FR-24** The application is installable and works as a PWA. The service worker handles push and
notification clicks.

**FR-25** Score changes are announced to assistive technology through a polite live region, so a screen
reader user learns about a goal without polling the page.

**FR-26** The interface is keyboard operable end to end, including dialogs, which trap focus while open and
return it on close.

**FR-27** Scores render with tabular figures so a 0 becoming a 1 does not shift the row.

**FR-28** Motion, including the goal highlight, respects `prefers-reduced-motion`.

**FR-37** Every link in the navigation resolves to a screen of its own. A link whose route was never
registered still renders and still looks enabled, and nothing in a type checker or a linter notices,
so the links are read out of the shell and put to the router in a test.

**FR-38** A reader can open finished results for the selected league. Results are grouped by the
reader's local date, newest first, and additional pages follow the API's `nextLink` without decoding it
in the browser. An empty season says that no results have been played yet.

**FR-39** Fixtures and results can each be narrowed to a round, a team, or both. The season resource
lists the available choices from the stored schedule. A continuation link keeps every filter, and a
cached page for one selection cannot be returned for another.

## 5. Data model

PostgreSQL with EF Core 10. Migrations live in the repository and are applied on startup in development
and by an explicit step in the compose stack.

**Reference data.** `leagues`, `seasons`, `teams`, `players`, and `team_season`, which records which
league a team played in for a given season. A team's league is not a column on the team, because teams get
promoted and relegated and the fixture history has to stay correct.

**`matches`.** Identity, season, round, kickoff time in UTC, the two teams, status
(`scheduled`, `live`, `halftime`, `finished`, `postponed`), score, minute and `last_event_sequence`. The
score columns are derived data kept for read performance. The event log is authoritative, and
`last_event_sequence` is what makes an out of order event detectable.

**`match_events`.** Append only. Provider, provider event id, sequence, minute, type
(`goal`, `own_goal`, `penalty_goal`, `yellow`, `red`, `substitution`, `period_start`, `period_end`), team,
player, assisting player, a JSONB payload for type specific detail, and the time it was received. Unique
on `(provider, provider_event_id)`.

**Projections.** `standings` holds played, won, drawn, lost, goals for, goals against, goal difference and
points per team and season. `player_season_stats` holds goals, assists, cards and minutes. Both are
updated during ingestion.

**Device data.** `devices`, `device_favourites` and `push_subscriptions` (endpoint, keys, failure
count). Notification preferences live on the device row rather than in a table of their own: they are
one to one with a device and always read together with it, so a second table would add a join to every
notification decision and buy nothing.

**`notification_outbox`.** Device, match event, payload, status, attempt count and next attempt time.

Every access path gets a named index, and the query plans for the three hottest reads are committed under
`docs/` so a reviewer can check the claim rather than trust it.

## 6. Implementation constraints

These are decisions already taken. They are recorded here because they shape the code rather than the
behaviour, and because a reviewer should be able to argue with them.

**No repository layer over EF Core.** `DbSet` is already a repository and `SaveChangesAsync` is already a
unit of work. A generic wrapper on top hides `IQueryable`, blocks projection and `ExecuteUpdateAsync`, and
buys nothing back. Shared query logic lives in `IQueryable<T>` extension methods, which compose and are
easy to test.

The layering does require one narrow port, `IPitchwireDbContext`, so that the application layer can reach
the store without referencing a database provider. It exposes the same `DbSet` properties the context
holds, so every capability above survives it. It has exactly one implementation, which is a cost of the
layering rather than a benefit of it, and ADR 0007 says so plainly. A second small port,
`IStoreFailures`, answers whether a failed write was a uniqueness violation, because every database
answers that differently and EF Core does not answer it at all.

**The wire model and the domain model are separate types.** The provider's event kinds and the domain's
line up today and are still translated rather than shared. A provider that invents a kind is refused at
the translation, instead of putting a value in the event log that no reducer can read and every later
rebuild has to guess at.

**Read queries project straight to DTOs** with `AsNoTracking`, so only the needed columns leave the
database and nothing enters the change tracker. Hot paths use compiled queries. Counter updates on
projections use `ExecuteUpdateAsync` rather than a read, modify and write round trip.

**Caching is scoped deliberately.** HybridCache with Redis as the second level covers league tables, top
scorers, form, fixtures, finished matches and reference data. Live matches are not cached: they change by
the second and SignalR already pushes them, so a cache there would only manufacture disagreement between
two screens. Cache tags are invalidated after the transaction commits, never before, because invalidating
first repopulates the cache from the pre commit state if the transaction then rolls back.

Tag invalidation was measured rather than assumed, and the measurement changed what the design can
claim. Against Microsoft.Extensions.Caching.Hybrid 10.10.0 with a Redis second level, dropping a tag
takes effect immediately in the instance that dropped it, including for an entry written in the same
instant. It does not reach another instance: an entry both of them share through Redis stays live for
the one that did not drop the tag. Two integration tests pin both halves of that.

The single instance design in ADR 0008 is what makes this acceptable today. A second replica would need
invalidation to travel between instances, which is one more reason that decision is written down rather
than assumed.

**A component library is used where it earns its place, and it does not yet.** The screens built so far
are a list of matches, a timeline, a league table and a form. Every one of them is better served by
semantic HTML than by a component: a real `table` element is what a screen reader and a keyboard already
understand. The library was in the bundle before anything used it and cost 24 KB gzipped, more than half
the first load, so it came back out. When a screen genuinely needs a date picker, a combo box or a
virtualised list, the library returns for that screen.

That library will be PrimeVue 4.5.5, the last release under MIT. Version 5 moved to a licence with
revenue, headcount and funding conditions, a required licence key, and a notice the software may display
without one. A repository anybody is invited to clone cannot carry that, and the npm licence audit fails
the build on it rather than leaving it to be noticed later.

**Globalization data is required.** Quiet hours are stored against the device's own IANA zone, and
`TimeZoneInfo` cannot resolve one when a build runs with invariant globalization. That setting is
therefore off here, against the usual default, and the runtime image carries ICU. Turning it off also
surfaced cache keys that formatted numbers through the current culture, which would have stopped a key
matching itself under another locale.

**The API contract has one source.** The backend produces an OpenAPI document, the client's types are
generated from it, and hand written Zod schemas parse every response at the boundary. A set of type level
assertions holds the two together, so a schema that drifts from the document fails the type check rather
than at runtime. A CI job regenerates the document and the types and fails if the committed copies differ,
which is what makes a renamed field on the server break the browser build.

The generator needed one correction to be useful. The .NET document described every integer as an integer
or a string, which is a faithful description of what the serialiser can be configured to accept and a poor
description of what this service does. Every generated client would have had to narrow a union that can
never happen, so a schema transformer narrows it at the source.

## 7. Non-functional requirements

Every number here is measured on named hardware and published. Nothing in the README asserts speed without
a measurement behind it.

| Requirement | How it is measured | Target |
|---|---|---|
| Goal to screen | Ingestion accepted to SignalR frame received by a client | p99 under 250 ms |
| Goal to notification | Ingestion accepted to push request dispatched | p99 under 2 s |
| Live list read | k6, 200 matches in progress | p99 under 50 ms |
| Ingestion throughput | Sustained events per second with the full event log written | Measured and published |
| Cache benefit | League table p99 with HybridCache enabled and disabled | Measured. If the benefit is not real, the cache is removed and the finding is published |
| First load | Lighthouse in CI | JS under 200 KB gzipped, LCP under 2.0 s, CLS under 0.05 |

Correctness requirements that are not about speed:

* A duplicated goal never changes the score.
* An out of order event never produces a wrong final score.
* A gap in the feed is detected, marked and repaired.
* A rolled back transaction never produces a broadcast or a notification.
* Incremental projections and a full recompute agree.
* Quiet hours that cross midnight behave correctly in a timezone other than the server's.

Security requirements:

* Ingestion is authenticated by HMAC with a replay window and constant time comparison.
* Device tokens are 256 bits of cryptographic randomness, stored hashed, never logged.
* Rate limits apply per IP on public reads, per device on device writes, and under a separate policy on
  ingestion.
* CORS is restricted to the known origin. CSP, HSTS and `X-Content-Type-Options` are set.
* No secret is committed. Development uses user secrets, containers use environment variables. This
  includes the VAPID private key and the ingestion HMAC secret. When no VAPID pair is configured the
  service generates one for that run and says so, rather than shipping a private key in a compose file
  so that a demo starts one step faster.
* Raw SQL appears only in the recompute path and is parameterised.
* No dependency with a commercial licence, at any depth, in either the .NET or the npm graph. Enforced by
  the licence audit tool in CI.

## 8. Quality gates

A change merges only when all of these pass.

Backend: a build with zero warnings and warnings treated as errors, unit tests, integration tests against
real PostgreSQL and Redis containers, the licence audit, and `dotnet format` verification.

Frontend: `vue-tsc --noEmit`, ESLint with the TypeScript, Vue and accessibility plugins with no warnings
allowed, Vitest, a bundle budget measured from the build manifest rather than guessed at, and a licence
audit over the whole npm tree using the same policy as the NuGet one.

Cross cutting: a `docker compose up` job that runs the README quick start and asserts against the running
stack, Playwright journeys against that stack, and the contract drift job described in section 6.

## 9. Milestones

| Milestone | Content |
|---|---|
| M0 | Skeleton: solution, central package management, editor config, SDK pin, compose with PostgreSQL and Redis, CI pipeline, health endpoints |
| M1 | Domain and ingestion: schema, migrations, feed simulator, HMAC, idempotency, ordering, gap repair, tests |
| M2 | Read API: fixtures, match detail, table projection, pagination, HybridCache, OpenAPI document |
| M3 | Live updates: SignalR groups, delta broadcast, catch-up by sequence |
| M4 | Notifications: devices, favourites, preferences, quiet hours, outbox relay, Web Push |
| M5 | Frontend core: Vue, live list, match detail, fixtures, table, SignalR, generated contract. PrimeVue was dropped here rather than pinned, for the reason in ADR 0010 |
| M6 | PWA and push: service worker, subscription flow, preferences screen, deep links from notifications |
| M7 | Depth: lineups on a pitch, substitutions, match statistics, player ratings, top scorers, form, countries and leagues. The season archive was dropped: with one season per competition it would be a screen listing one row |
| M8 | Finish: benchmarks, load test, ADRs, operations guide, HTTP file, README with measured numbers, changelog, v1.0.0 |

A working product exists at the end of M6. M7 adds depth and M8 finishes the repository.

## 10. Architecture decision records

Each one states the alternative that lost and what was given up.

The records themselves are in [docs/adr](adr/).

| ADR | Decision |
|---|---|
| 0001 | A separate feed process and a real ingestion boundary, rather than a background task inside the API |
| 0002 | Idempotency by database constraint and the ordering policy for late and missing events |
| 0003 | Derived score columns with synchronous projections, rather than replaying events per request or updating projections in the background |
| 0004 | What HybridCache covers, and why invalidation happens after commit |
| 0005 | Notification delivery through a transactional outbox, rather than a message broker |
| 0006 | Keyset continuation tokens, and why `$skip` is refused |
| 0007 | Four layers with the dependency arrow inward, and an EF shaped context port rather than a repository |
| 0008 | Single instance by design, and what a second instance would cost |
| 0009 | Anonymous device identity, and why an identity provider was not used here |
| 0010 | No component library in the bundle, and why PrimeVue was dropped rather than pinned |
| 0011 | A separate wire model, translated at the boundary, rather than one enum shared with the provider |
| 0012 | Player ratings derived from the event log, and why they are a model rather than an opinion |
| 0013 | Team sheets and statistics as snapshots in the same signed batch, with their own ordering rule |

## 11. Acceptance criteria

The project is finished when all of the following are true.

1. A reviewer clones the repository, runs one command, and has the API, the feed, the database, the cache
   and the web application running with matches in progress.
2. Following a team and waiting for it to score produces a notification with the tab closed.
3. Closing the laptop lid mid match and reopening it shows the goals that happened in between, with no gap
   in the timeline.
4. Every CI job is green, including the accessibility, bundle budget and contract drift jobs.
5. Every performance claim in the README has a published measurement and the hardware it was taken on.
6. Every architecture decision listed above has an ADR naming the alternative it beat.
7. There is no placeholder, no TODO and no screen that says it is coming soon.

## 12. Known limitations

Written down here so they are choices rather than surprises.

* Favourites live on one device. Clearing site data loses them. There is no recovery path, by design.
* A single instance means a deployment drops live connections. Clients reconnect and catch up, so no data
  is lost, but there is a visible gap of a second or two.
* The simulator is not a real league. Fixtures, form and scoring patterns are plausible rather than
  faithful, and nothing in the product should be read as a prediction.
* The device cookie is marked secure, so the demo has to be reached over HTTPS or through localhost.
  On a plain HTTP host that is not localhost the browser will not return it, and every device setting
  looks empty. Weakening the cookie to make a demo simpler would be the wrong trade.
* Web Push is unavailable on some browser and platform combinations. The application degrades to in-app
  notifications there and says so rather than failing silently.
* Generated VAPID keys do not survive a restart. A demo started without configured keys can be
  subscribed to, and those subscriptions stop working the next time the process starts. Configure a
  pair through the environment for anything that has to keep working.
* Cache invalidation does not cross instances. A tag dropped by one process leaves the shared entry in
  place for any other process holding it, so a second replica would serve a stale table until the entry
  expires. Measured, not assumed, and the single instance design is what keeps it harmless.
