# Architecture Decision Records

We will maintain Architecture Decision Records (ADRs) in our FHIR Server repository, placing each one under `doc/arch/adr-<yymm>-<short-title>.md`. When in a proposed design state, place the ADR under the folder `doc/arch/Proposals`. We have chosen to write these records using Markdown.

Each ADR will be assigned a unique, sequential number (date based as above) that will never be reused. If a decision is later reversed, changed or evolved, the original record will remain in place but will be marked as superseded. Even though it’s no longer valid, it is still historically important.

We will keep the ADR structure simple, with just a few sections in this example template:

```

# Title
   Each ADR should have a short, descriptive phrase, such as “ADR 001: Database Schema Changes for Version 1.5.0” or “ADR 009: Introducing Polly for Retry Logic”.

## Context 
   Summarize the relevant factors (technological, organizational, or other) that influence this decision. Present these factors neutrally, highlighting any tensions or constraints that shaped the solution.

## Decision
   Clearly state the chosen approach or solution in active voice (for example, “We will…”). Describe the rationale and what will be implemented as a result.

## Status
   Indicate whether this decision is proposed, accepted, deprecated, or superseded (with a reference to the new ADR if applicable).

## Consequences
   Outline the outcomes of applying this decision. Include all effects—beneficial, adverse, or neutral—since these will affect the project over time.

```

This is inspired by [documenting architecture decisions](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions).