# ADR 0003: Derived score columns with synchronous projections

- Status: accepted
- Date: 2026-09-20

## Context

The score is the most read number in the product. It is also derivable: it is a function of the event
log. Storing it means storing something that can disagree with its source; not storing it means
computing it on every read of every list.

## Decision

The score, minute, status and last sequence are columns on the match row, written in the same
transaction as the events that produced them, by a pure reducer that takes the whole log. Season
level projections, the table and the scorers list, are updated in that same transaction.

Nothing is computed in the background. A projection that lags is a projection that is wrong for a
while, and "for a while" on a live score service means during the only minutes anybody is looking.

## Alternatives

**Replay on every request.** Correct by construction, and it makes a list of fifty matches fifty
replays. Rejected on cost rather than on principle.

**A background projector.** The usual answer at scale, and it buys throughput by giving up freshness.
Rejected because freshness is the product.

## Consequences

Writes do more work: an ingest transaction touches the events, the match and the season projections.
That is acceptable because ingestion is a handful of requests a second and reads are everything else.
The derived columns can only disagree with the log through a bug in one pure function, which is the
most heavily tested function in the codebase.
