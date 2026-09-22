# ADR 0011: A wire model, translated at the boundary

- Status: accepted
- Date: 2026-09-20

## Context

The provider sends event kinds. The domain has event kinds. Sharing one enum between them is one
fewer type to keep in step.

It also means that the day the provider adds a kind, renames one, or sends something unrecognised,
the change lands in the middle of the domain, and an unknown value becomes a valid domain state that
nothing knows how to handle.

## Decision

`Pitchwire.Contracts` holds the wire shapes and is the only thing the feed and the API share.
Translation happens once, at the ingestion boundary. An unrecognised kind is rejected there, with the
rest of the batch unaffected, and never reaches the domain.

## Alternatives

**One shared enum.** Less code, and it couples the rules of football to somebody else's release
notes.

**Accept anything and store it raw.** Defers the problem to every reader of the table, forever.

## Consequences

Two enums and a translation, which is the cost. The domain only ever holds kinds it has rules for,
the API can say precisely which event in a batch it refused and why, and a provider change is a
change in one file.
