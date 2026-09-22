# ADR 0001: A separate feed process and a real ingestion boundary

- Status: accepted
- Date: 2026-09-20

## Context

Live scores come from somewhere. In production that is a paid provider; here it is a simulator. The
question is not where the data comes from but whether the seam it arrives through is real.

A simulator running inside the API as a background task can hand a domain object straight to the
ingestion code. No serialisation, no signature, no retry, no network. Everything the seam exists to
handle is skipped, and the day a real provider replaces it, none of that work has been done and none
of the failures it causes have ever been seen.

## Decision

The feed is its own process, in its own project, with its own Dockerfile and its own container. It
talks to the API over HTTP with a signed body, the way a third party would, and it knows nothing
about the API beyond the wire contract in `Pitchwire.Contracts`.

It is also deliberately badly behaved: it drops events, duplicates them, reorders them and pauses
mid match, at rates set in configuration. A provider that never misbehaves proves nothing.

## Alternatives

**A background service in the API.** Cheapest to write and the usual choice for a demo. Rejected
because it deletes the boundary: no signature to verify, no batch to be idempotent about, no gap to
detect, and no way to test any of it.

**A message broker between the two.** Closer to some production setups, and it would give retries and
durability for free. Rejected because it moves the problem into infrastructure nobody in this project
operates, and because a signed HTTP endpoint is what sports data providers actually publish to.

## Consequences

Two processes to run, which compose handles. The API has to verify signatures, deduplicate, order and
repair, which is most of the interesting code in the repository. The feed can be pointed at a running
API from anywhere, and it can be replaced by a real provider without touching the API.
