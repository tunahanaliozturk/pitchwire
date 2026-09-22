# ADR 0005: Notification delivery through a transactional outbox

- Status: accepted
- Date: 2026-09-20

## Context

When a followed team scores, a device gets a push. The goal is written to the database; the push goes
to an external service that can be slow, can fail, and can take seconds to answer.

Sending inside the ingest transaction ties the write to somebody else's uptime. Sending after the
commit, in the same request, means a crash between the two loses the notification with nothing left
to say it was owed.

## Decision

The notification is written to an outbox table in the same transaction as the event. A relay claims
rows with `FOR UPDATE SKIP LOCKED`, sends them, and marks them done. A failure is retried with
exponential backoff. A 404 or 410 from the push service deletes the subscription, because both mean
the browser has thrown it away.

## Alternatives

**Send inline after the commit.** One less table and one less loop, and every process restart loses
whatever was in flight.

**A message broker.** The right answer when there are several consumers and real volume. Rejected
here: one producer, one consumer, and a database already in the transaction. A broker would add an
operational dependency that buys nothing this application needs.

## Consequences

A table that grows and has to be pruned, and a loop that has to be running for anything to be
delivered. Delivery is at least once, so the payload carries enough for a client to recognise a
repeat. The claim query means several relay instances could run without sending the same row twice,
which is one of the few things here that is already ready for a second instance.
