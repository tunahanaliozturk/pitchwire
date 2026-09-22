# Architecture decision records

One file per decision that would be expensive to reverse. Each says what was decided, what it was
decided instead of, and what it costs. A record is written when the decision is made and it is not
edited afterwards: a record that quietly changes to match the code is a record of nothing. When a
decision is replaced, the new record supersedes the old one and the old one stays where it is.

| ADR | Decision |
|---|---|
| [0001](0001-separate-feed-process.md) | A separate feed process and a real ingestion boundary |
| [0002](0002-idempotency-and-ordering.md) | Idempotency by database constraint, and the ordering policy |
| [0003](0003-derived-columns-and-projections.md) | Derived score columns with synchronous projections |
| [0004](0004-hybrid-cache.md) | What HybridCache covers, and when it is invalidated |
| [0005](0005-transactional-outbox.md) | Notification delivery through a transactional outbox |
| [0006](0006-keyset-pagination.md) | Keyset continuation tokens, and why `$skip` is refused |
| [0007](0007-four-layers-and-a-context-port.md) | Four layers, and an EF shaped context port rather than a repository |
| [0008](0008-single-instance.md) | Single instance by design, and what a second would cost |
| [0009](0009-anonymous-device-identity.md) | Anonymous device identity rather than accounts |
| [0010](0010-no-component-library.md) | No component library in the bundle |
| [0011](0011-wire-model-translated-at-the-boundary.md) | A wire model, translated at the boundary |
| [0012](0012-player-ratings-from-the-log.md) | Player ratings derived from the event log |
| [0013](0013-snapshots-in-the-signed-batch.md) | Team sheets and statistics as snapshots in the signed batch |
