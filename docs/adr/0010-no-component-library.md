# ADR 0010: No component library in the bundle

- Status: accepted
- Date: 2026-09-21

## Context

The plan had been to use PrimeVue. Checking the licence before pinning a version showed that PrimeVue
5 is no longer MIT: it ships under the PrimeUI licence, which attaches conditions to revenue and
headcount and expects a licence key. Version 4.5.5 is still MIT.

That forced the real question. The screens here are a list of matches, a table, a timeline, a team
sheet and a settings form. Between them they used no PrimeVue component at all.

## Decision

No component library. Native elements, a small set of design tokens, and components written in this
repository. The dependency was removed rather than pinned, which took 24 kB gzipped out of the
bundle.

## Alternatives

**Pin 4.5.5.** Keeps the option open, keeps an unused 24 kB in every page load, and leaves an upgrade
path that ends at a licence key.

**Adopt PrimeVue 5 on its own terms.** Fine for a product with a company behind it. Not fine for a
repository anybody is invited to clone.

## Consequences

Dialogs, focus traps and keyboard behaviour are written here, so they have to be tested here. The
bundle is 45 kB gzipped against a 200 kB budget. If a screen ever needs a genuinely hard component, a
virtualised data grid being the usual example, the decision is worth reopening for that component
alone rather than for a whole library.
