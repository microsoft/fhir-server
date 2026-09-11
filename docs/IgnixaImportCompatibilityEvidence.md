# Ignixa import compatibility evidence

## Purpose and scope

This document records the L2 characterization baseline for the selectable
`IImportResourceParser` seam. It is evidence of the current behavior, not a
claim that the native Ignixa persistence path exists or that imports are faster.
The production default remains Firely. No production serialization, comparison,
storage, import-processing, or provider-default behavior is changed by this
baseline.

The shared corpus and its tests are compiled by the STU3, R4, R4B, and R5 core
unit-test projects. The service-composition test is in the shared API test
project and is currently executed through the R4 API unit-test project.

## Import parser evidence

| Evidence | What it executes | Expected observation |
| --- | --- | --- |
| `ImportResourceParserParityTests` | Real `FirelyImportResourceParser` composed with the production `FhirJsonParser` settings (`PermissiveParsing` and `TruncateDateTimeToDate`), plus real `IgnixaImportResourceParser`, `ResourceWrapperFactory`, and `RawResourceFactory` | Both parsers produce the documented import metadata and raw-resource outputs for valid inputs, or each returns an error for invalid inputs. Same-output cases include ordinal raw-resource string parity; documented divergences are asserted separately. |
| `OperationsModuleTests.GivenConfiguredProvider_WhenResolvedParserParsesContainedConditionalReference_ThenObservableResultProvesProviderPath` | `OperationsModule` registration, DI resolution, then the resolved parser's `Parse` method | Firely rejects an initial-load contained conditional reference; Ignixa accepts it. This result is an executable-path assertion, not an assertion about a registration label or implementation type. |
| `ImportResourceParserParityTests.GivenContainedConditionalReferenceDuringInitialLoad_WhenParsed_ThenFirelyRejectsButIgnixaAllows` | Direct real parser paths | Records the same intentionally scoped divergence for contained resources. Bundle-entry cases and incremental-load behavior are also covered in that suite. |

The import seam always ends in `RawResourceFactory`, which serializes through
Firely. An Ignixa parser test is therefore not evidence of native Ignixa
persistence or rollback behavior. Those capabilities remain pending for the
later native-persistence work.

## Shared corpus and assertions

`ImportResourceParserCharacterizationCorpus` intentionally keeps these inputs
as literal raw JSON, so their lexical details remain visible:

| Corpus input | Assertion type | Current characterization |
| --- | --- | --- |
| Nested properties in two different orders | Semantic | The two input trees are semantically equal even though their input text differs. Both providers retain the named values. This is the only use of structural comparison; it does not define a general fidelity oracle. |
| Ordered `given` array plus tabs, newline, spaces, and escaped quotes in `name.text` | Lexical value and array-order assertions | Array position and the interior tab, newline, and quotes are retained. The outer spaces are trimmed by the current serialization path and are explicitly asserted as absent. |
| Two otherwise identical `given` arrays in different orders | Semantic inequality, lexical raw-payload, and array-order assertions | Array order is semantically meaningful: the two trees and each provider's raw payloads differ, while both providers retain each input ordering. |
| `valueQuantity.value` `1.2300` and Zulu `effectiveDateTime` | Lexical raw-token assertions | The output numeric token remains `1.2300` and the temporal string remains `2024-02-29T10:11:12.120Z` for both paths. |
| Date-typed `birthDate` with a Zulu date-time form | Explicit divergence assertion | With the production Firely parser settings, Firely truncates the date-time to `2024-02-29`; Ignixa retains `2024-02-29T10:11:12.120Z`. This malformed date input is a characterized divergence, not a same-output parity claim. |
| Extension-only `active`, `given`/`_given` aligned arrays, and null placeholders | Shape and lexical-value assertions | The data-absent-reason URLs/codes and both array placeholders survive. No missing primitive value is injected. |
| Duplicate known property and unknown property | Explicit output and error assertions | Firely retains the final duplicate `active` value, while Ignixa rejects the duplicate with `ArgumentException`. An unknown member is rejected by both paths with an error that identifies `unrecognizedProperty`, rather than being silently persisted. |
| Array in scalar `active` position and malformed JSON | Explicit leniency and negative assertions | Both paths use the scalar array value for `active`; malformed JSON produces no import resource. Ignixa malformed JSON is specifically characterized as a `FormatException` with a `System.Text.Json.JsonException` inner exception. |

The corpus also records import metadata behavior for id, version, `lastUpdated`,
`KeepVersion`, `KeepLastUpdated`, soft deletion, and conditional references.
It includes a noncanonical version, missing metadata, future timestamps,
canonical/noncanonical soft-delete extensions, and duplicate soft-delete
extensions. Current tests preserve supplied data-absent-reason extensions;
automatic missing-value injection is deliberately not part of this scope.

## Semantic comparison versus lexical fidelity

Semantic equivalence answers whether parsed FHIR content represents the same
resource. It can legitimately ignore JSON object-member order. It must not be
used to decide raw-resource deduplication, because it hides array order,
significant string whitespace, escaped representation, numeric trailing
precision, temporal spelling, and metadata placement.

Lexical fidelity is asserted only where a current observable raw token or
string is required. The SQL raw-resource dedupe path currently uses exact
string comparison after its metadata substitutions. Cosmos also regenerates
raw JSON after metadata handling. This L2 work characterizes parser/raw output
only; it does **not** claim that semantically equivalent but lexically different
writes are no-ops, nor does it alter either store's comparison behavior.

## Capability matrix

| Capability | Current owner/provider | L2 evidence | Status and follow-up |
| --- | --- | --- | --- |
| Create, update, delete, and history | Existing resource handlers plus SQL/Cosmos stores | Import metadata and raw-resource shape are unit characterized; no store behavior changed | SQL/Cosmos no-op, history, ETag, and import-conflict behavior need isolated-store integration evidence under story 206675. |
| Search and reindex | Existing Firely-backed indexing and reindex pipeline | No import-parser-only claim | Not migrated; retain current behavior. |
| Bundles | Existing bundle orchestration | Parser suite records Bundle-entry conditional-reference divergence | Bundle persistence remains outside the import parser seam. |
| Validation | Existing validation pipeline | Malformed JSON/scalar negative cases only | Full validation parity is not claimed. |
| Metadata | Parser plus `RawResourceFactory` | id/version/lastUpdated, keep flags, soft-delete and meaningful raw metadata shape are covered | Store-assigned ETags and metadata-only history remain isolated-store work. |
| Import | Selectable Firely/Ignixa parser followed by Firely serialization | Real parser, DI-selected provider-path, positive, divergent, and error cases | Native Ignixa persistence and full rollback are pending. |
| Export | Existing Firely export serializer | None | Not migrated or characterized by this task. |
| Patch | Existing Firely patch implementation | None | Not migrated or characterized by this task. |
| Terminology | Existing terminology services | None | Not migrated or characterized by this task. |
| XML | Existing Firely XML pipeline | None | JSON import parser corpus does not establish XML compatibility. |

## Evidence limits

No shared store, cloud endpoint, deployment, benchmark, database reset, or
PerfTester default entry point was executed for this task. Consequently, there
is no L2 claim about throughput, allocations, server memory, SQL/Cosmos
outcomes, ETags, import-job deduplication, or live import conflicts. Those
items require the opt-in tooling and isolated-store execution planned for the
later L2 baseline work and L3/L4 implementation stories.
