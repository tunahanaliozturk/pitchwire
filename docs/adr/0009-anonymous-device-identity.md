# ADR 0009: Anonymous device identity rather than accounts

- Status: accepted
- Date: 2026-09-20

## Context

Following a team and receiving a notification needs something to attach the following to. The usual
answer is an account, which means a password or an identity provider, a verification flow, a recovery
flow, and personal data to hold and protect.

## Decision

A device identity: an opaque id issued on first use and kept in a cookie that is `HttpOnly`, `Secure`
and `SameSite=Lax`. Favourites and notification preferences hang off it. Asking who the reader is
returns nothing until they have been here before, which is a normal state rather than an error.

No email address, no name, nothing that identifies a person. A push subscription is the only stored
thing that could be called contactable, and deleting the device deletes it.

## Alternatives

**Accounts with an identity provider.** The right answer when a user has something to lose by losing
access. Here they would lose a list of followed teams, which takes ten seconds to rebuild.

**Local storage only, with no server identity.** Then the server cannot push, because it has nothing
to push to.

## Consequences

Favourites live on one device, and clearing site data loses them with no recovery path. That is a
stated limitation rather than an oversight. In exchange there is no personal data in the database and
no login screen between a reader and a score.
