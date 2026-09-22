# ADR 0012: Player ratings derived from the event log

- Status: accepted
- Date: 2026-09-23

## Context

A match page that shows a rating is claiming to know how well somebody played. The honest options are
to take somebody's opinion, to buy a provider's number, or to compute something from what is actually
recorded and be clear about what that is.

Only the third is available here, and pretending otherwise would be the worst of the three.

## Decision

The rating is a function of the event log and nothing else: a baseline of 6.0, adjusted for goals,
penalties, own goals, assists, cards, the result, and for a goalkeeper or a defender what got past
them. It is clamped between 3.0 and 10.0. A player on for fewer than twenty minutes is not rated at
all, because a number for three minutes of stoppage time claims to know something the log does not.

Minutes played come from the substitutions and dismissals in the same log rather than being reported
separately, so a rating and a timeline cannot tell different stories. The page says where the number
comes from, in a sentence, under the ratings.

## Alternatives

**A provider's rating.** What a real product would buy. Not available here, and inventing a number
that looks like one would be dishonest.

**Touches, passes, duels, expected goals.** A better model, and it needs data this feed does not
carry. A model built on fields nobody publishes is a model built on made up numbers.

## Consequences

A goalkeeper who conceded three in a win is rated below a striker who did nothing, which is a real
limitation of a model this simple. In exchange it is deterministic, it is testable, and every point
in it can be traced to something that was recorded.
