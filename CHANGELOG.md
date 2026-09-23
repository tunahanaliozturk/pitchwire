# Changelog

Notable changes, newest first. Dates are the day the work landed on `main`.

## Unreleased

- A device can renew its own push subscription, but another device can no longer claim its endpoint
  and redirect notifications. Conflicts answer 409 without changing the original subscription.
- Chromium now exercises competition switching, combined match filters and all match-detail tabs
  against the running compose stack in CI. A failed journey keeps its trace for diagnosis.
- Fixtures and results can now be filtered by round and team together. The season endpoint supplies
  the choices, and continuation links and cache keys keep each filtered list separate.
- Finished matches now have a Results screen with the same league picker and continuation paging as
  fixtures. Both lists group matches by local date. The navigation scrolls on narrow screens so every
  section remains reachable.
- Switching countries now keeps the latest selection when league requests finish out of order. A
  failed lookup is visible in the picker and can be retried.
- Fixture pages now belong to the selected season in the query cache. Returning to a league restores
  its own pages, including ones that arrived while another league was on screen.

## 1.0.0 — 2026-09-23

The first version that does everything it set out to do.

### Ingestion

- A separate feed process that signs every batch with HMAC-SHA256 over a length prefixed timestamp
  and body, and a five minute replay window.
- Idempotency by unique index on `(provider, provider_event_id)`, decided by the database rather than
  by a read before a write.
- Per match ordering by provider sequence. A late event rebuilds the match from its whole log rather
  than being patched into the totals.
- Gap detection, a repair pull from the provider, and a degraded flag that the match page shows and
  that clears itself when the hole is filled.
- A sweeper that finishes matches whose final whistle was dropped, because there is no later event to
  reveal that hole.
- Team sheets and statistics as snapshots in the same signed batch, with their own ordering rule: a
  sheet replaces the one before it, and a statistics snapshot from an earlier minute is ignored.

### Read API

- Keyset pagination in the shape Microsoft's APIs use: `value`, `nextLink`, `$top` capped at 100 and
  an opaque `$skiptoken` that carries the kind of list it came from. `$skip` is refused.
- HybridCache with Redis behind it for tables, scorers, fixtures, results and form, tagged and
  invalidated after the commit rather than before it.
- Countries and leagues, three countries with two competitions each, every club and player invented.
- Player ratings and minutes played, both derived from the event log and neither stored.
- An OpenAPI document, generated from the code, that the browser types are generated from. A contract
  drift job fails the build if the two disagree.

### Live and notifications

- SignalR deltas, not documents, over three group kinds: everything live, one match, one team.
- Web push through a transactional outbox with `FOR UPDATE SKIP LOCKED`, exponential backoff, and a
  subscription that deletes itself on a 404 or 410.
- Quiet hours in the device's own time zone, and per kind preferences for goals, red cards, kick off
  and full time.

### Web

- Vue 3.5, TypeScript in strict mode, Pinia and TanStack Query, with every response parsed by a Zod
  schema at the boundary and type level assertions that those schemas match the generated types.
- A country and league picker, a live board grouped by competition, and a match page with a timeline,
  team sheets drawn on a pitch, statistics and ratings.
- Installable as a PWA with a hand written service worker that handles push and notification clicks.
- No component library: 45 kB gzipped against a 200 kB budget.

### Operations and evidence

- One `docker compose up` brings up the database, the cache, the API, the feed and the web
  application, and `--wait` returns when they are all actually ready.
- Five CI jobs: build and unit tests, integration tests against a real PostgreSQL, the browser suite,
  contract drift, and a compose run that asserts a match finished with a real event log.
- Benchmarks for the work a match page does, and a k6 load test for the read API. The numbers in the
  README came from those two and are quoted with the machine that produced them.
- Thirteen architecture decision records, each naming the alternative it beat.
