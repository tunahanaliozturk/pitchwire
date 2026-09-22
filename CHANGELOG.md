# Changelog

Notable changes, newest first. Dates are the day the work landed on `main`.

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
