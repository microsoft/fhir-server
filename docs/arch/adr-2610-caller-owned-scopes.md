# ADR-2610: FHIR-owned caller scopes

**Status**: Proposed
**Date**: 2026-10-30
**Feature**: Caller-owned dependency injection scopes

## Context

Background operations and storage callers need services whose lifetime spans asynchronous work, independently of the request or container that resolves the factory. Registering disposable transient handles with dependency injection lets the resolving root retain them until shutdown, even after callers dispose them. Parallel delegate and provider factories obscure who owns that lifetime. Healthcare shared components 12.0.0 removes its unused generic scoped-handle APIs, so FHIR Server must own this contract without reversing its Core-to-TaskManagement dependency.

This is an internal ownership decision, not a change to the [FHIR asynchronous interaction contract](https://hl7.org/fhir/R4/async.html) or resource authorization. As with the explicit ownership boundaries in [ADR 2603: Atomic SearchParameter CRUD Operations](adr-2603-atomic-searchparameter-crud-and-cache-refresh-ownership.md), lifetime responsibility must have one owner. That ADR remains applicable and is not superseded.

## Options Considered

1. **Keep the external handles and override registrations** - Rejected: the APIs are removed upstream, and disposable registrations retain the root-ownership ambiguity.
2. **Use separate generic and job-specific handles** - Rejected: duplicates the same caller-owned contract and requires parallel caller models.
3. **One local handle with non-disposable providers** - Chosen: preserves layering and makes the caller responsible for each acquired lifetime.

## Decision

Define `IScoped<T>` in the existing TaskManagement assembly under `Microsoft.Health.Fhir.Scoping`, with a value and caller-controlled disposal. Keep `IScopeProvider<T>.Invoke()` and its dependency-injection implementation in Core. Providers are non-disposable and never resolve disposable handles through the container; each invocation creates a distinct child scope. Failed activation disposes that child scope before propagating the error. A disposed ordinary handle rejects further value access; disposal is idempotent.

Callers retain handles through awaited work and dispose them on completion, failure or cancellation. Job discovery and each execution have separate scopes. Cosmos uses the same provider model but creates non-owning Container handles: disposing a handle must not dispose the shared client, whose singleton provider remains owned by the root. Deferred Container lookup in initialization must stay deferred until the Container is assigned. Standalone storage and web hosts register the same open-generic provider, with the Cosmos-specific closed provider taking precedence.

## Consequences

- One factory model replaces disposable transient handles and per-type scoped delegates; wrapper and payload collectability, independent scopes, failure cleanup and asynchronous caller lifetimes become explicit regression contracts.
- The contract requires healthcare shared components 12.0.0. Its new assembly/namespace identity and migrated constructor parameters are public API breaks; downstream consumers must rebuild and remove obsolete scoped-handle registrations.
- TaskManagement gains no dependency on Core and no new assembly is introduced. Existing shared dependency-injection helpers remain in use.
- Non-owning Cosmos handles intentionally do not enforce ordinary child-scope disposal semantics; shared client identity and root disposal are checked separately.
- This does not resolve unrelated shutdown/job scheduling behavior or change FHIR responses, schema, resource-level authorization or storage consistency guarantees.
