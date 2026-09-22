# ADR 0013: Team sheets and statistics as snapshots in the signed batch

- Status: accepted
- Date: 2026-09-23

## Context

Team sheets and match statistics are not events. A sheet is the state of a team's selection at a
moment; a statistics line is a cumulative total as of a minute. Neither has a sequence and neither
makes sense to replay.

They still arrive from the same provider, over the same connection, and they still have to be
authentic and idempotent.

## Decision

They travel in the same signed batch as events, in their own arrays, and they are applied with their
own ordering rule. A team sheet replaces the previous sheet for that team entirely: a player dropped
from a corrected sheet has to disappear rather than linger, or the page shows twelve players
starting. A statistics snapshot is kept only if its minute is not earlier than the one already
stored, because cumulative numbers that walk backwards are worse than no numbers at all.

## Alternatives

**Model them as events with sequences.** Forces a replay semantic onto something with no history
worth replaying, and turns a corrected sheet into a diff nobody sent.

**A separate endpoint for each.** Two more signed endpoints, two more replay windows, and a batch
that can now be half applied.

## Consequences

Two application paths with different rules, stated rather than hidden: events are ordered and
replayed, snapshots are replaced. The provider can publish a sheet before kick off and again at half
time at no cost, which is what they do, and a lost first copy needs nobody to ask for it.
