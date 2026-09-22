# ADR 0004: What HybridCache covers, and when it is invalidated

- Status: accepted
- Date: 2026-09-20

## Context

Tables, scorers, fixtures and form are read far more often than they change, and each is a query that
joins and aggregates. The live list is read constantly and changes constantly. The two need opposite
treatment.

## Decision

`HybridCache`, with an in memory first level and Redis behind it. Cached: the league table, the
scorers list, fixtures, results and team form, each tagged by season or by team. Not cached: the live
list and the match detail, because the whole point of both is that they are current.

Invalidation happens after the transaction commits, never before. Removing an entry before the commit
opens a window where a reader repopulates the cache from the old state and the new state is then
written underneath it, leaving a stale entry with no event left to clear it.

## Alternatives

**Time based expiry alone.** Simple, and it means a table that is wrong for the length of the window
every time a match finishes.

**Caching the live list for a second or two.** Tempting under load. Rejected: a second of staleness on
the live list is a goal that has not appeared yet, which is the one thing this product must not do.

## Consequences

A cache to operate and a Redis to run. Tag invalidation is in process only: it clears the local first
level of the instance that did the write and the shared second level, but it does not reach another
instance's first level. That was measured with two hosts against one Redis rather than assumed, and
it is the main reason for ADR 0008.
