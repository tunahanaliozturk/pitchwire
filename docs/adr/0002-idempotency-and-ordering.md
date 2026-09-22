# ADR 0002: Idempotency by database constraint, and the ordering policy

- Status: accepted
- Date: 2026-09-20

## Context

A provider retries. A retry that crosses with an acknowledgement arrives as a duplicate, and a
duplicate goal is a wrong score on a screen somebody is watching. Events also arrive out of order,
because two workers racing produce exactly that, and sometimes they do not arrive at all.

Deduplicating in application code means reading before writing, and two requests that read before
either writes both decide the event is new.

## Decision

Idempotency is a unique index on `(provider, provider_event_id)`. The insert is attempted and the
constraint violation, SQLSTATE 23505, is the answer. Two concurrent requests cannot both win, because
the database decides rather than the application.

Ordering is per match and by provider sequence. An event whose sequence is lower than the match's
last is not patched into the running total: the match is rebuilt from its whole log. A gap in the
sequence marks the match degraded and queues a repair pull from the provider, and the match stops
being degraded when the hole is filled.

## Alternatives

**A read then an insert.** Loses the race described above, and the failure only appears under load.

**Trusting the arrival order.** Works until it does not, and the symptom is a score that is briefly
wrong for one reader and right for another.

**Patching a late event into the totals.** Cheaper than a rebuild, and wrong whenever the late event
is one whose effect depends on what came before it, such as a second yellow.

## Consequences

Rebuilding costs a pass over the log rather than an increment, which is why the pass is measured
rather than assumed: a 200 event match rebuilds in about 470 nanoseconds and allocates 176 bytes on
the machine quoted in the README, which is less than the call that fetched the events. The provider
must supply a stable event id, which any real one does.
