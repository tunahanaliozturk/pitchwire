# ADR 0006: Keyset continuation tokens, and why `$skip` is refused

- Status: accepted
- Date: 2026-09-20

## Context

Fixtures, results and the live list are lists that change while they are being read. Offset paging on
a moving list skips rows and repeats rows, and it gets slower the further in the reader goes, because
the database counts everything it steps over.

## Decision

Keyset paging, in the shape Microsoft's own APIs use: a response is `{ "value": [...], "nextLink":
"..." }`, `$top` defaults to 50 and is capped at 100, and the continuation is an opaque `$skiptoken`.

The token carries the key of the last row seen and the kind of list it came from, base64url encoded.
The kind is in there because a token from one list must not decode into another: a fixture cursor
read as a standings cursor would silently answer a question nobody asked.

`$skip` is not implemented. It is not a missing feature; it is a refused one.

## Alternatives

**Page numbers.** What everybody expects, and wrong on a list that changes between two requests.

**A signed token.** Considered and dropped. Nothing in the token grants access to anything: tampering
with it produces a different page of the same public list, not a page somebody was not allowed to
see. A signature would add a key to manage in exchange for nothing.

## Consequences

No page numbers and no jump to the end, which is the honest trade for a list that moves. Every page
is a seek on an index, so the hundredth page costs what the first one did, and encoding or decoding a
token is a few hundred nanoseconds either way.
