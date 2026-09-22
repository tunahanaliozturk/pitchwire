# ADR 0007: Four layers, and an EF shaped context port rather than a repository

- Status: accepted
- Date: 2026-09-20

## Context

The service started as one project. Four layers were introduced deliberately rather than by habit,
and the question was what the boundary between application and infrastructure should look like.

A repository per aggregate is the usual answer. In practice it becomes a method per query, each one a
thin wrapper that hides which columns are loaded and makes it awkward to write a projection without
adding another method.

## Decision

Four projects, with the dependency arrow pointing inward: `Domain` knows nothing, `Application`
depends on `Domain`, `Infrastructure` depends on both, and `Api` composes them. The direction is
enforced by a test that reads each assembly's references, so a wrong `using` fails the build rather
than a review.

The persistence port is `IPitchwireDbContext`, which exposes `DbSet<T>` and `SaveChangesAsync`. The
application writes LINQ against it. The other ports are narrow and behavioural: `IStoreFailures`,
`IMatchFeed`, `ILiveUpdates`, `IPushSender`.

## Alternatives

**A repository per aggregate.** Hides the query shape, multiplies methods, and makes projections
awkward enough that people load whole entities instead.

**No port at all, EF everywhere.** Honest about the coupling, and it puts provider specific behaviour
into the application. `IStoreFailures` exists precisely because EF Core has no provider neutral way
to say "that was a unique constraint".

## Consequences

The application is coupled to EF's shape, and that is stated plainly rather than pretended away. In
exchange, a read is one expression that says exactly what it loads, and the tests run against a real
PostgreSQL rather than an in memory imitation of one.
