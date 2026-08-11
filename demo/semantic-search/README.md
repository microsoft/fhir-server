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
| Longitudinal radiology can be reconstructed | Three chest CTs document an initial finding, completed follow-up, and stability using deliberately varied wording. |
| Irrelevant data ranks lower | Same-patient glucose, mammography, and dermatology records are intentionally unrelated. |
| FHIR scope beats semantic similarity | A second patient has the same exact calibration text and a highly similar pulmonary-nodule report, and must never appear in Elena's result set. |
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

### Radiology timeline

Each chest CT has a searchable DiagnosticReport conclusion and a full
Binary-backed DocumentReference authored by the synthetic radiologist Leah
Morgan, MD. The report wording changes deliberately so retrieval must connect
`pulmonary nodule`, `right apical focal opacity`, and `right apical focal
density` without losing the structured patient, radiology category, chest CT
code, and date constraints.

| Study | Documented result | Follow-up state |
|---|---|---|
| 2024-02-12 baseline | Incidental 7 mm right upper lobe solid nodule | Low-dose chest CT recommended in six months. |
| 2024-08-19 interval | Same 7 mm finding described as a right-apical opacity with no growth | Six-month recommendation completed; repeat in 12 months. |
| 2025-08-25 interval | Right-apical density unchanged over 18 months | Twelve-month recommendation completed; no further dedicated follow-up. |

David Chen has a highly similar 7 mm right upper lobe nodule report and
six-month recommendation. Both its DiagnosticReport and DocumentReference are
negative controls for patient isolation. Elena's existing head CT and
mammography remain same-patient radiology distractors.

### Isolation control: David Chen

`Observation/demo-control-obs-exact-presyncope` contains the exact calibration
sentence, and David's chest CT repeats the nodule size, location, and follow-up
recommendation. Both belong to
`Patient/c27d6972-16be-4a01-8b9c-0d994c58d9bc`. Semantic similarity should favor
these controls, but deterministic patient scope must exclude them.

## Queries

Calibration query:

```text
Lightheaded when standing with a near-syncopal episode and no loss of consciousness.
```

Clinical semantic query:

```text
Has this patient had previous episodes of dizziness, fainting, or nearly passing out?
```

## Choose a Demo Path

`presentation/start-up-guide.md` is the single operator guide for starting SQL,
FHIR, MCP, and the demo agents. The MCP implementation remains in
`tools/FhirMcp`, its VS Code registration remains in `.vscode/mcp.json`, and the
role agents and shared retrieval playbook remain under `.github`. Keeping those
components outside this folder prevents the demo data from becoming an alternate
application layout.

### Clinical MCP demo (default)

Use this path for the physician and radiology presentation:

1. Complete Steps 1 through 7 in `presentation/start-up-guide.md` to build and
  start the FHIR server and MCP.
2. Run `requests/00-preflight.http` and
  `requests/01-verify-search-parameters.http`.
3. Run `requests/02-ingest-and-index.http` when preparing a fresh database or
  when any of the 40 canonical fixtures are missing.
4. Complete the MCP and agent checks in
  `expected/verification-checklist.md`.
5. Run the **Physician Context Demo** and **Radiology Context Demo** agents with
  the patient-session initialization and prompts in the startup guide.

`requests/04-semantic-search.http` and `requests/07-radiology-search.http` are
manual equivalents of the agent's primary semantic routes. Keep them ready for
response inspection and as a transparent backup, but they are not the live agent
execution surface.

### Server implementation deep dive (optional)

Use this path when the audience wants to inspect the underlying FHIR and SQL
behavior:

1. Present `presentation/intern-demo-design-walkthrough.md` as the server
  implementation walkthrough.
2. Run `requests/03-standard-search.http` for structured and hybrid search.
3. Run `requests/04-semantic-search.http` for patient-wide ranking.
4. Run `requests/05-long-document-search.http` for text and PDF provenance.
5. Run `requests/07-radiology-search.http` for the structured and semantic
  longitudinal radiology checks.
6. Optionally run `requests/01-manage-custom-search-parameter.http` to show a
  posted SearchParameter lifecycle.
7. Optionally run `requests/06-vector-reindex-proof.http`, or run the automated
  proof:

  ```powershell
  .\demo\semantic-search\scripts\Test-VectorReindex.ps1 -SkipCertificateCheck
  ```

8. Compare the responses with `expected/verification-checklist.md`.

### Recovery-only reset

`requests/00-reset-vector-resources.http` hard-deletes all 20 vector-bearing
fixture owners so the next ingestion performs real writes. Do not run it during a
normal rehearsal. Use it only when resources were created without vectors, when
an interrupted preparation left stale indexing state, or when a deliberate clean
reindexing demonstration is required. Byte-identical PUT requests are no-ops and
cannot repair missing vector rows by themselves.

## Long-Document Fixtures

The reviewable source narratives live in `resources/source-documents/`. The
checked-in Binary JSON resources contain the exact base64 bytes used by the HTTP
demo. The fixture manifest defines the radiology timeline, expected phrases,
queries, distractors, and cross-patient exclusions. Generate the text-backed
resources and validate all JSON, hashes, references, chronology, and manifest
expectations with:

```powershell
dotnet run --project .\demo\semantic-search\tools\FixtureGenerator\FixtureGenerator.csproj -- .\demo\semantic-search
```

The default command preserves and validates the checked-in PDF. After editing
either PDF source page, explicitly rebuild it with:

```powershell
dotnet run --project .\demo\semantic-search\tools\FixtureGenerator\FixtureGenerator.csproj -- .\demo\semantic-search --regenerate-pdf
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