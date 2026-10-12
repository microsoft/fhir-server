# Design: FHIR NPM Package Reader and SearchParameter Installation Handler

**Status**: Proposed (working design for a draft PR; handler, installer and validator refactor implemented, reader pending)
**Work item**: AB#206131
**Parent feature**: install SearchParameter resources from FHIR NPM packages when a new service is provisioned

## Context

The service will install `SearchParameter` resources from a versioned [FHIR NPM package](https://hl7.org/fhir/packages.html) while a new service is being provisioned. Four stories deliver it:

| Story | Responsibility |
|---|---|
| **AB#206131 (this design)** | Read a package tarball safely; validate the complete SearchParameter set; report installed and ignored content |
| AB#206132 | Durable install job, applied-package record (SQL schema), empty-store bootstrap (parameters created `Enabled`), ownership rules |
| AB#206133 | Desired-state reconciler, deployment-owned source allow-list, npm registry acquisition (dist-tag resolution, authentication) |
| AB#206134 | Applied-package and job status, `CapabilityStatement.implementationGuide` |

Design constraints from the feature:

- Only `SearchParameter` resources are installed. Profiles, extensions, terminology, examples and executable content are ignored and reported. Non-core package dependencies are never resolved.
- The reader is separate from resource-type installation handlers, so later conformance types are added without changing the artifact format or the acquisition boundary.
- Safety checks (integrity, size limits, decompression bombs, path traversal) happen before anything is extracted to disk or written to the store.
- The complete SearchParameter set is validated as one unit. One invalid parameter fails the whole package.

### Acceptance criteria (AB#206131)

1. A valid SearchParameter-only package for the server's FHIR version reads and validates end to end.
2. A package that also contains `StructureDefinition`, `ValueSet` and so on installs only the SearchParameters and names the ignored types in the result.
3. Integrity-hash mismatch, oversized archive, zip-bomb expansion ratio, and path-traversal or absolute-path entries are each rejected before extraction to disk or any write, with an actionable `OperationOutcome`.
4. A package declaring a mismatched FHIR core dependency is rejected.
5. A package with one invalid SearchParameter fails the whole set; nothing is persisted.
6. A composite parameter whose components can't be resolved is rejected (see decision 5).
7. Unit tests cover every rejection case and the ignored-resource-type reporting.

## Where this sits in the flow

```mermaid
flowchart LR
    R[206133 reconciler + acquisition] -->|StrictPackageExtractionResult + descriptor| J[206132 durable install job]
    J --> I[PackageInstaller]
    I --> PR[FhirPackageReader]
    PR --> H[Handler registry]
    H --> SP[SearchParameterPackageHandler]
    SP -->|validated plan| W[ISearchParameterPackageWriter, implemented in 206132]
    I -->|PackageInstallResult / OperationOutcome| J
```

This story covers everything from "a tarball and its expected integrity are available" through "a validated, all-or-nothing SearchParameter set, plus a result naming what was installed and what was ignored". It has no network calls, job, schema, ownership records or CapabilityStatement changes.

## Decision

### Build the reader on Ignixa.PackageManagement strict extraction

The server is migrating to the Ignixa SDK (ADR-2607). `Ignixa.PackageManagement` 0.6.82 adds `PackageExtractor.ExtractStrictAsync` and `NpmPackageAcquirer`, which already provide most of the archive safety this story needs:

- In-memory extraction only: nothing on disk, no scripts.
- Configurable compressed, expanded, per-entry, entry-count and JSON-depth limits, counted on the bytes actually streamed.
- Exactly one `package/package.json`; relative POSIX paths under `package/` only. Traversal, absolute paths, backslashes, links, special entries, case-insensitive collisions, malformed gzip/tar, trailing data and duplicate JSON properties are rejected.
- Errors carry no package content: `PackageExtractionException.Diagnostic` has a code, the physical entry index and the configured limit.
- The acquirer (used by AB#206133) checks a source allow-list, requires SHA-512 SRI integrity over the compressed bytes before extraction, confirms name/version identity, and provides an authentication callback and bounded retry. It does not resolve dist-tags.

`Firely.Fhir.Packages` was considered and rejected: it extracts to disk, does no SRI verification, has no size, entry-count or path limits, and goes against the migration direction.

**Placement.** Interfaces and models (`IFhirPackageReader`, `FhirPackage`, `PackageReaderOptions`, handler contracts) go in `Microsoft.Health.Fhir.Core/Features/Packages/`. The Ignixa-backed reader goes in `Microsoft.Health.Fhir.Ignixa/Features/Packages/`. It is registered unconditionally, not behind `CoreFeatures.FhirSdkProvider`: package reading has no Firely equivalent, so it is a new capability rather than a migrated seam. ADR-2607 gets a note saying so. The SearchParameter handler stays on Firely POCOs, because search indexing and `SearchParameterInfo` are still Firely-based (ADR-2607 Phase 3).

### Reader: `FhirPackageReader`

Two entry points share one interpretation path:

- `ReadAsync(StrictPackageExtractionResult, PackageDescriptor)` is the production feed path. AB#206133's acquirer call has already enforced the allow-list, SHA-512 and name/version identity.
- `ReadAsync(Stream, PackageDescriptor { PackageId, Version, Integrity })` buffers up to the compressed cap, verifies SHA-512 (`NpmPackageIntegrity`, `SHA512.HashData`, fixed-time compare), then calls `ExtractStrictAsync`. Tests use it, and so would any future non-feed source.

Checks run in this order. Each failure produces an actionable `OperationOutcome` before any handler runs:

1. Stream path only: the compressed-size cap, then SHA-512 integrity.
2. Ignixa strict extraction, with our limits mapped to `PackageExtractionLimits` using checked `int` conversion.
3. Expansion ratio: `Statistics.ExpandedBytes / Statistics.CompressedBytes` must not exceed the configured ratio. Ignixa has no ratio limit.
4. Manifest:
   - On the stream path, name and version must equal the descriptor.
   - The FHIR core dependency (`dependencies["hl7.fhir.<r3|r4|r4b|r5>.core"]`, and `fhirVersion`/`fhirVersions` if present) must exactly equal `IModelInfoProvider.SupportedVersion.VersionString`.
   - Non-core dependencies are reported as information and never resolved.
5. Classification:
   - Top-level `package/*.json` entries with a `resourceType` go to handlers.
   - Everything else is ignored and reported: sub-folders (`example/`, `other/`, …), metadata JSON (`.index.json`, null `ResourceType`) and resource types without a handler.
   - Metadata JSON such as `.index.json` is expected package content and isn't reported. Non-JSON files are dropped by Ignixa and aren't reported either, because its statistics don't separate them from directories.

`PackageReaderOptions` defaults (configurable; the section name will be agreed with AB#206133):

| Limit | Default |
|---|---|
| Compressed size | 10 MB |
| Expanded size | 50 MB |
| Per entry | 5 MB |
| Entries | 1,000 |
| JSON depth | 64 |
| Expansion ratio | 100:1 |

### Handlers: `IFhirPackageResourceHandler`

Handlers work in two phases: every handler validates before anything is written.

```csharp
string ResourceType { get; }
Task<IPackageInstallPlan> ValidateAsync(IReadOnlyList<PackageEntry> entries, FhirPackageContext context, CancellationToken cancellationToken);
```

The handler registry is built from DI. This release registers only `SearchParameter`.

### `SearchParameterPackageHandler` (Shared.Core)

It validates the complete set as one unit and collects every failure into one `OperationOutcome`:

- **Parsing:** each entry is parsed with a strict `FhirJsonParser` into a `SearchParameter` and passes model validation.
- **Per parameter:** the URL is present, absolute and at most 128 characters; code, base and type are present; the expression parses.
- **Within the package:** URLs are unique, and no code is repeated on the same base.
- **Against the server's reference set**, refreshed first with `GetAndApplySearchParameterUpdates`:
  - no URL collides with a system-defined parameter;
  - code conflicts per base type follow the same `SearchParameterComparer` rules as `SearchParameterValidator`, including `Resource`/`DomainResource` expansion.
- **Composites:** each `component.definition` must resolve to a core parameter, a parameter in the same package, or an existing parameter in the reference set (decision 5).
- **Data-store support:** `SearchParameterInfo`s are built for the set, with an overlay so same-package composite components resolve, and run through `ISearchParameterSupportResolver.IsSearchParameterSupported`. Unsupported or partially supported parameters are rejected, and the data store's own rules (`IDataStoreSearchParameterValidator`) run last. Rejecting partial support is stricter than the single-resource create path, which accepts it: a provisioned parameter that silently misses some target types is worse than a failed install.

Reuse: the code-conflict and comparison logic in `SearchParameterValidator` moves unchanged into the internal static `SearchParameterConflictRules`, which takes its dependencies as arguments. The per-request validator and the package handler then share the rules. No lookup abstraction is needed: package members are checked against each other separately (duplicate URL and code), and against the current definitions through the existing definition manager. The per-request validator keeps its `DataActions.Reindex` authorization check, which the package path must not hit because there is no user context.

### Installer and result

`PackageInstaller.InstallAsync` reads the package, sends entries to handlers, validates everything, then hands the validated plans to `ISearchParameterPackageWriter` (implemented in AB#206132).

`PackageInstallResult` reports:

- package id, version and integrity;
- installed SearchParameter canonical URLs;
- ignored resource types with counts;
- ignored non-resource entries and non-core dependencies;
- `ToOperationOutcome()`: information issues for ignored content, error issues on failure.

Error mapping:

| Failure | `OperationOutcome` issue code |
|---|---|
| Integrity, unsafe path | `security` |
| Size, count, ratio or depth limit | `too-costly` |
| FHIR version mismatch | `not-supported` |
| Invalid manifest, archive or SearchParameter | `invalid` |
| URL or code collision | `conflict` |

Messages go in `Resources.resx`.

## Decision log

| # | Topic | Decision |
|---|---|---|
| 1 | Story boundary | Stop at a validated plan plus `ISearchParameterPackageWriter`. AB#206132 implements the write. "Nothing persisted" is tested with a substitute writer. |
| 2 | Integrity | SHA-512 SRI only (npm `dist.integrity`), matching Ignixa. SHA-1 `shasum` is not accepted. |
| 3 | Non-core dependencies | Never resolved. Ignored and reported as information. |
| 4 | FHIR core version | Must match `SupportedVersion.VersionString` exactly, e.g. `4.0.1`. |
| 5 | Composite components | Allowed when they resolve to a core parameter, a same-package parameter, **or any existing parameter**; otherwise rejected. This relaxes the work item's original AC6, which allowed only core or same-package, and the work item will be updated. On an empty store the difference never applies. |
| 6 | SearchParameter `status` | Not an install filter. `draft`, `active`, `retired` and `unknown` all install. |
| 7 | Limits | Configurable `PackageReaderOptions` with the defaults above. |
| 8 | SDK | Ignixa.PackageManagement strict extraction, registered unconditionally. Firely.Fhir.Packages rejected. |
| 9 | Empty packages | A package with no installable resources at its root is rejected as `invalid`; it almost certainly means a packaging mistake. |
| 10 | DI registration | Deferred to the PR that provides `ISearchParameterPackageWriter` (AB#206132). Registering the handler earlier would leave a service whose dependency can't be resolved. |

## Tests

xUnit and NSubstitute, Arrange-Act-Assert. Test tarballs are built in memory with `TarWriter` and `GZipStream`. One small checked-in package produced by a real `npm pack` proves that partner-pipeline output passes strict extraction. Ignixa's internal checks are not re-tested. For each rejection, the tests assert the `OperationOutcome` and that no handler or writer was called.

| AC | Tests |
|---|---|
| 1 | A valid SearchParameter-only package reads and validates end to end (per FHIR version, through Shared tests) |
| 2 | StructureDefinition, ValueSet and examples are ignored and named in the result and `OperationOutcome` |
| 3 | Integrity mismatch; malformed or non-SHA-512 integrity; compressed cap; expanded cap; ratio bomb; entry-count cap; `../x`, `/abs`, `C:\x`, backslash paths; symlink and hardlink entries |
| 4 | Core dependency mismatch or missing; `package.json` missing or invalid; name or version differs from the descriptor |
| 5 | One invalid SearchParameter among valid ones: writer never called, all issues reported |
| 6 | Composite resolves to core, same-package and existing custom parameters; an unresolvable component is rejected |
| 7 | Duplicate URL or code within the package; collision with a system-defined URL; expression the data store doesn't support |

## Delivery

0. **Dependency bump:** `IgnixaPackageVersion` 0.6.74 → 0.6.82 (the release that includes brendankowitz/ignixa-fhir#464). Polly 8.5.2 → 8.6.6. Add `Semver` 3.0.0, a new transitive dependency. `Ignixa.Extensions.FirelySdk5` 0.6.82 still pins Hl7.Fhir 5.11.4, which matches this repo. Re-run the `$import` parity tests, because the shared version property also moves the `$import` packages.
1. Reader on Ignixa (both entry points, ratio, manifest and core-dependency checks, classification, error mapping), `PackageReaderOptions`, tests, and the ADR-2607 note.
2. Handler contracts, SearchParameter set validator, `SearchParameterValidator` refactor, installer and result, tests.

Nothing is wired to an entry point, so none of it runs until AB#206132 and AB#206133 land.

### Implementation status

| Piece | Status |
|---|---|
| Ignixa 0.6.82 and Polly 8.6.6 bump | Done; `$import` tests pass on all four FHIR versions |
| Contracts: `FhirPackage`, `IFhirPackageResourceHandler`, `IFhirPackageInstallPlan`, `ISearchParameterPackageWriter`, `FhirPackageException` | Done |
| `FhirPackageInstaller` and `FhirPackageInstallResult` | Done, with unit tests |
| `SearchParameterConflictRules` extracted from `SearchParameterValidator` | Done; existing validator tests pass |
| `SearchParameterPackageHandler` | Done, with unit tests on STU3, R4, R4B and R5 |
| Ignixa-backed reader | Not started |

## Consequences

- **Benefits:**
  - Archive safety comes from a library with extensive boundary tests rather than new tar-handling code.
  - The handler split lets later conformance types reuse the reader unchanged.
  - The rules for single-resource and package validation can't drift apart.
- **Costs:**
  - A new dependency closure: `Ignixa.Validation`, `SharpZipLib`, `Superpower`, `Fhir.Metrics`, `Semver`, and the Polly bump.
  - The first unconditional Ignixa code path, which differs from ADR-2607's per-seam toggle.
  - Ignixa's strictness may reject unusual but valid tarballs. The `npm pack` fixture covers the expected producer.
- **Neutral:**
  - The expansion ratio is checked after a bounded in-memory extraction rather than while streaming. Memory is still bounded by the expanded-size cap.
