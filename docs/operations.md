# Running pitchwire

What somebody needs to know to run this, look after it, and work out what is wrong when it misbehaves.

## Starting it

```
docker compose up -d --build --wait
```

Five services come up in order: PostgreSQL and Redis, then the API, then the feed and the web
application.
`--wait` returns when every health check has passed, so the command finishing means the stack is
actually ready rather than merely started.

| Service | Local port | What it is |
|---|---|---|
| web | 5082 | The Vue application, served by nginx |
| api | 5080 | The read and ingest API |
| feed | 5081 | The provider simulator |
| postgres | 5432 | The database |
| redis | 6379 | The cache's second level |

Compose binds every published port to `127.0.0.1`. It is a local demo with known database
credentials, not a remote deployment template. The browser goes through the web port, which adds
security headers and limits `/api` to five requests per second per client IP with a burst of 25.
Direct requests to the local API port do not pass through that limit. For remote use, put the web
service behind TLS and keep the API, feed, Redis and PostgreSQL private; see [security.md](security.md).

The API applies its migrations and seeds the catalogue on start, because `Seed__Enabled` is `true` in
the compose file. It is off by default everywhere else.

To repeat the browser journeys locally while the stack is running, use the web project:

```
cd src/Pitchwire.Web
npm ci
npx playwright install chromium
npm run test:e2e
```

The compose CI job runs the same tests in Chromium. It installs the browser's Linux dependencies first
and keeps a trace and screenshot if a journey fails. The tests need the stack, not just Vite: they
follow the real season and match responses through nginx.

Stopping with `docker compose down` keeps the data. `docker compose down -v` removes the volumes and
the next start is a fresh season.

## What the feed does

It plays one season: every league in the catalogue, one round at a time, at 120 match minutes per
real minute. A round of 31 matches takes about 45 seconds and the whole season finishes in ten to
fifteen minutes. After that nothing is live until the volumes are removed and it starts again. That
is a simulator playing a fixture list to the end, not a fault.

It misbehaves on purpose: it drops, duplicates and reorders events, and pauses mid match, at the
rates in `Feed__DropRate`, `Feed__DuplicateRate`, `Feed__ReorderRate` and `Feed__BurstPauseRate`. Set
them to zero for a quiet run; raise them to see the repair path work.

## Configuration

Everything is configuration in the usual .NET sense, so any key can be set as an environment variable
with `__` between the sections.

### API

| Key | Default | What it does |
|---|---|---|
| `ConnectionStrings__Postgres` | — | Required. |
| `ConnectionStrings__Redis` | none | Without it the cache is first level only, which is fine for one instance. |

Compose sets the Redis connection to `redis:6379`; the CI quick-start test checks that a table read
actually puts an entry in Redis. Omitting the connection when running the API separately leaves only
the local first level.
| `Ingest__Secret` | — | The shared secret the feed signs with. Required. |
| `Ingest__Provider` | `simulator` | The provider name stored against each event. |
| `Ingest__ReplayTolerance` | 5 minutes | How far a signed timestamp may be from now. |
| `Ingest__FeedBaseAddress` | — | Where to pull from when repairing a gap. |
| `Ingest__StaleAfter` | 30 seconds | Silence after which a live match is swept to finished. |
| `Ingest__SweepInterval` | 10 seconds | How often the sweeper looks. |
| `Ingest__RelayInterval` | 2 seconds | How often the notification outbox is drained. |
| `Ingest__MaxBodyBytes` | 1 MB | Requests larger than this are refused before they are read. |
| `WebPush__PublicKey`, `WebPush__PrivateKey` | generated | See below. |
| `WebPush__Subject` | a mailto | Contact address sent to the push service. |
| `Seed__Enabled` | `false` | Apply migrations and seed the catalogue on start. |

If no VAPID keys are configured the API generates a pair at start and logs a warning. That keeps a
demo working out of the box and it has a cost worth knowing: the keys change on every restart, and a
browser that subscribed under the old pair can no longer be reached. Set both keys for anything that
is supposed to survive a deployment.

### Feed

| Key | Default | What it does |
|---|---|---|
| `Feed__Secret` | — | Must match `Ingest__Secret`. |
| `Feed__ApiBaseAddress` | — | Where to post batches. |
| `Feed__Seed` | 1337 | The random seed. The same seed plays the same season. |
| `Feed__ClockFactor` | 60, and 120 in compose | Match minutes per real minute. |
| `Feed__RoundIntervalSeconds` | 30, and 15 in compose | The gap between rounds. |

## Health and telemetry

`/health/live` answers as soon as the process is up. `/health/ready` answers whether the service can
do its job, which here means reaching PostgreSQL, and it is what a load balancer should use. A
readiness probe that always says yes is worse than none.

Metrics are published on the meter `Pitchwire.Ingestion` and exported over OTLP when
`OTEL_EXPORTER_OTLP_ENDPOINT` is set:

| Metric | What a change in it means |
|---|---|
| `pitchwire.ingest.accepted` | Events applied. Flat during a round means the feed has stopped. |
| `pitchwire.ingest.duplicate` | Retries that crossed with an acknowledgement. Some is normal. |
| `pitchwire.ingest.rejected` | A bad signature, a stale timestamp, or an unrecognised kind. Sustained is a problem. |
| `pitchwire.gap.repaired` | Events pulled back after a hole was spotted. |
| `pitchwire.ingest.delivery_lag` | Milliseconds between the provider's timestamp and the write. |

Traces use the activity source `Pitchwire.Ingestion`, one span per batch.

## When something looks wrong

**A match says some events are missing.** It is degraded: a gap was found in the sequence and a
repair pull is queued. It clears itself when the hole is filled. If it does not, the feed is not
answering `Ingest__FeedBaseAddress`.

**A match is stuck at 90 minutes.** The final whistle was dropped and the sweeper has not run yet. It
runs every `Ingest__SweepInterval` and finishes anything silent for longer than `Ingest__StaleAfter`.

**Nothing is live.** Either the round has not started, or the season has finished. `docker compose
logs feed | grep Round` says which.

**Every event is rejected.** The two secrets disagree, or the clocks do. The rejection reason is in
the API log with the batch.

**Notifications stop arriving.** Check the outbox depth:

```
docker compose exec -T postgres psql -U pitchwire -d pitchwire \
  -c "select count(*) from notification_outbox where sent_at is null and abandoned_reason is null;"
```

A number that only grows means the relay is not running or the push service is refusing. A 404 or 410
deletes the subscription on purpose, so a browser that has been reinstalled quietly disappears.

**The web application loads but has no data.** nginx proxies `/api` to the API container. If the API
is unhealthy the page renders and every request fails, which is what the error states on each screen
are for.

## Things to know before running more than one of these

One API instance is the design, and ADR 0008 says why. Briefly: the SignalR hub holds its connections
in process, and cache tag invalidation does not reach another instance's first level cache. A second
instance needs a backplane, cross instance invalidation, and a lease for the sweeper. Until then, a
deployment drops live connections, clients reconnect and catch up from the last sequence they saw,
and nothing is lost but a second or two.
