# ADR 0008: Single instance by design, and what a second would cost

- Status: accepted
- Date: 2026-09-20

## Context

Two things in this application assume there is one of it. The SignalR hub holds connections in
process, and `HybridCache` tag invalidation clears the local first level of the instance that did the
write and the shared second level, but not another instance's first level. That second point was
measured with two hosts against one Redis, not inferred from the documentation.

## Decision

One API instance. The design says so, the limitation is written down, and the work a second instance
would need is described rather than built.

A second instance would need three things: a SignalR backplane, so a delta reaches clients connected
elsewhere; a way to invalidate another instance's first level cache, which in practice means
publishing invalidations over Redis and subscribing to them; and a lease or a leader for the stale
match sweeper, so two instances do not both decide a match has ended.

## Alternatives

**Build it now.** Three pieces of infrastructure, each with its own failure modes, to solve a problem
this deployment does not have.

**Say nothing and hope.** The usual outcome, and it surfaces as a goal that reaches half the readers.

## Consequences

A deployment drops live connections. Clients reconnect and catch up from the last sequence they saw,
so nothing is lost but a second or two. The ceiling is documented, and the first symptom of hitting
it is known in advance rather than discovered in production.
