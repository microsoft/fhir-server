# FHIR SQL Semantic Search Demo

This demo exercises the semantic-search implementation through the real FHIR
HTTP API. It uses deterministic synthetic FHIR R4 resources and can be rerun
without creating duplicate logical records.

All people and clinical events are fictional. The records are inspired by common
ED documentation patterns; they are not copied from a real chart.

## Story

Elena Marquez, a synthetic 60-year-old patient, presents to the emergency
department with recurrent dizziness. The clinician wants to know whether similar
episodes occurred before and what prior evaluations concluded.

Clinical question:

> Has this patient had previous episodes of dizziness, fainting, or nearly
> passing out?

## What The Demo Proves

| Claim | Deliberate data check |
|---|---|
| Configuration controls indexing | Three built-in Microsoft vector SearchParameters select Observation notes, DiagnosticReport conclusions, and DocumentReference Binary text. |
| Normal FHIR writes trigger indexing | Resources are loaded with idempotent HTTP `PUT` interactions. |
| Exact input calibrates near 1 | One Observation note exactly equals the calibration query. |
| Semantic meaning works across wording | DiagnosticReport and ED-note passages describe orthostatic presyncope without repeating the exact query. |
| Long documents return passages | The prior ED note spans multiple character-window chunks and places the relevant event in the middle. |
| Irrelevant data ranks lower | Same-patient glucose, mammography, and dermatology records are intentionally unrelated. |
| FHIR scope beats semantic similarity | A second patient has the same exact calibration text and must never appear in Elena's result set. |
| Results are traceable | Each result includes score, winning text, SearchParameter canonical, source resource, and source path. |
| Mixed-resource ranking works | Observation, DiagnosticReport, and DocumentReference appear in one globally ranked Bundle. |

## Dataset

The two Patient logical IDs are stable opaque UUIDs. Their typed MRNs are
separate business identifiers under a synthetic `example.org` naming system.
Visit numbers, provider numbers, and document master identifiers follow the
same pattern; none is a real-world identifier. Specialty notes reference the
clinician who plausibly authored them, and each Binary attachment has an exact
byte size and SHA-1 integrity hash in its DocumentReference.

FHIR does not prescribe a narrative length. The chart therefore uses concise
Observation notes (12-27 words), report conclusions (10-38 words), and authored
documents sized for their purpose: 200-word dermatology follow-up, 271-word
physical-therapy evaluation, 495-word emergency note, 862-word vestibular note,
and 1,256-word autonomic consultation PDF. These differences are intentional and
preserve distinct relevance levels for the search demonstration.

### Primary patient: Elena Marquez

| Resource | Narrative signal | Expected behavior |
|---|---|---|
| `Observation/demo-obs-exact-presyncope` | Exact calibration sentence | Score approximately 1 for the calibration query. |
| `Observation/demo-obs-orthostatic-vitals` | Blood-pressure drop with lightheadedness | Strong semantic result. |
| `Observation/demo-obs-glucose` | Stable diabetes monitoring | Low relevance. |
| `DiagnosticReport/demo-report-cardiology` | Orthostatic presyncope favored over arrhythmia | Strong paraphrase result. |
| `DiagnosticReport/demo-report-head-ct` | Negative head CT after dizziness evaluation | Related context, below the direct history. |
| `DiagnosticReport/demo-report-mammography` | Routine breast screening | Low relevance. |
| `DocumentReference/demo-doc-prior-ed` | Long ED note with a near-fainting passage in the middle | Strong result with `Binary.data` provenance. |
| `DocumentReference/demo-doc-physical-therapy` | Balance impairment with no dizziness reproduced during testing | Moderately related result. |
| `DocumentReference/demo-doc-dermatology` | Unrelated skin follow-up | Low relevance. |
| `DocumentReference/demo-doc-long-vestibular-text` | 862-word text note separating positional vertigo from presyncope | Retrieves Dix-Hallpike and Epley evidence from `Binary.data`. |
| `DocumentReference/demo-doc-long-autonomic-pdf` | 1,256-word, two-page PDF autonomic consultation | Retrieves active-stand evidence from page 1 and the hot-shower medication decision from page 2. |

### Isolation control: David Chen

`Observation/demo-control-obs-exact-presyncope` contains the exact calibration
sentence but belongs to `Patient/c27d6972-16be-4a01-8b9c-0d994c58d9bc`. It is a stronger test
than merely checking an unrelated second patient: semantic similarity would
favor it, but deterministic patient scope must exclude it.

## Queries

Calibration query:

```text
Lightheaded when standing with a near-syncopal episode and no loss of consciousness.
```

Clinical semantic query:

```text
Has this patient had previous episodes of dizziness, fainting, or nearly passing out?
```

## Run Order

1. Configure the server process using
   `configuration/vector-search.settings.example.json` as a safe reference.
2. Start SQL FHIR Server with schema 118 and the Azure embedding deployment.
3. Run `requests/00-preflight.http`.
4. Run `requests/01-verify-search-parameters.http`.
5. Run `requests/01-manage-custom-search-parameter.http` when demonstrating an
  API-posted custom vector SearchParameter, including conditional creation,
  activation, verification, disable, and reset behavior.
6. Run `requests/02-ingest-and-index.http` from top to bottom.
7. Open `presentation/intern-demo-design-walkthrough.md` in Markdown preview and present it.
8. Run `requests/03-standard-search.http`.
9. Run `requests/04-semantic-search.http`.
10. Run `requests/05-long-document-search.http`.
11. Run `requests/06-vector-reindex-proof.http` to demonstrate backfill, or run
  the automated proof:

  ```powershell
  .\demo\semantic-search\scripts\Test-VectorReindex.ps1 -SkipCertificateCheck
  ```

12. Compare the responses with `expected/verification-checklist.md`.

## Long-Document Fixtures

The reviewable source narratives live in `resources/source-documents/`. The
checked-in Binary JSON resources contain the exact base64 bytes used by the HTTP
demo. To regenerate the PDF, Binary resources, attachment sizes, and FHIR R4
SHA-1 attachment hashes after editing a source narrative, run:

```powershell
dotnet run --project .\demo\semantic-search\tools\FixtureGenerator\FixtureGenerator.csproj -- .\demo\semantic-search
```

The generator reopens the PDF with PdfPig and fails unless it has exactly two
pages and retains the expected page-specific phrases.

Do not perform schema migration, database creation, or secret configuration live
during the demo. Complete those before the meeting.

## Demo Structure

Use the same four verbs in the introduction, narration, and HTTP files:

> **Configure -> Ingest and Index -> Search -> Verify**

The Markdown introduction provides the technical map, then the HTTP files are
the live execution surface. This README remains the operator runbook and should
not be presented line by line.

See [the verification checklist](expected/verification-checklist.md) for the
pass/fail contract and [the demo design walkthrough](presentation/intern-demo-design-walkthrough.md)
for the content to show before the live requests.

## Important Boundaries

- SQL only; no Cosmos implementation.
- The three demo semantic SearchParameters are built into the R4 registry.
  Eligible custom vector definitions are discovered from the live registry.
- UTF-8 `text/plain` and text-based `application/pdf` local Binary content; scanned PDFs require a future OCR provider.
- The current chunker uses overlapping character windows even though settings
  retain `*Tokens` names.
- The Patient operation supports only Observation, DiagnosticReport, and
  DocumentReference, with patient/type/count constraints.
- The score is relevance for ordering, not probability or clinical confidence.
- Linked Binary authorization and Binary-dependent reindexing are deferred
  production work; this demo uses synthetic data under one access context.
- The HTTP E2E source compiles but its in-process run has not completed; perform
  the manual smoke test before presenting.