# ADR-2608: Incrementally adopt the Ignixa SDK

**Status**: Accepted
**Date**: 2026-08-31
**Feature**: Ignixa SDK migration

## Context

The server is deeply coupled to the Firely SDK across parsing, serialization, validation, FHIRPath evaluation, and resource processing. Production-shaped measurements show that Ignixa can materially reduce FHIRPath and element-model costs, while its modular design provides greater control over capabilities that are currently supplied by one broad dependency. Retaining Firely indefinitely would preserve the status quo but would also forgo those benefits and provide no path to reduce that coupling.

Replacing the SDK across the entire server in one change would create an unreviewable blast radius and make behavioral regressions, index drift, and rollback difficult to manage.

FHIR behavior and persisted data must remain compatible throughout the migration. Individual capabilities have different correctness risks, performance characteristics, and dependencies, so they cannot all be enabled safely at the same time.

## Options Considered

1. **Continue using Firely as the only SDK** - This minimizes near-term change but retains the current coupling and forgoes measured performance opportunities. *(rejected)*
2. **Replace Firely with Ignixa in one release** - A single cutover avoids temporary adapters but creates excessive implementation and rollback risk. *(rejected)*
3. **Run both SDKs indefinitely and compare every operation** - Continuous shadow execution provides evidence but permanently doubles complexity and can conceal which implementation is authoritative. *(rejected)*
4. **Adopt Ignixa incrementally at explicit feature seams** - Migrate independently selectable capabilities while Firely remains the default for unmigrated behavior. *(selected)*

## Decision

We will adopt Ignixa incrementally through narrow, capability-specific seams. Each seam selects exactly one implementation at startup, defaults to Firely until Ignixa is approved for that capability, and supports rollback through configuration without rewriting persisted data. Production requests will not silently fall back from Ignixa to Firely; failures must remain observable.

We will not introduce a single facade for the entire FHIR SDK. Existing focused contracts will be reused, and new abstractions will be limited to capabilities that do not already have an appropriate boundary. A seam must include every path that produces or regenerates the same persisted or externally visible representation; for example, indexing and reindexing cannot select different providers.

A seam may be enabled only after production-shaped tests demonstrate semantic parity, compatibility with supported FHIR versions, and acceptable performance. Firely will be removed only after all supported runtime behavior has migrated and the compatibility layer is no longer required.

## Consequences

- Migration changes remain reviewable, independently deployable, and reversible.
- Firely and Ignixa dependencies, adapters, and configuration coexist temporarily.
- Different capabilities may intentionally use different SDKs during the transition, so startup diagnostics must identify the effective provider for each migrated seam.
- Every migrated seam requires parity tests and operational failure signals; performance-motivated changes also require measurements of the actual server path rather than isolated SDK claims.
- Ignixa performance benefits may be limited while a path still crosses Firely compatibility adapters.
- Removing Firely becomes a deliberate final migration step rather than an incidental consequence of an individual feature change.
