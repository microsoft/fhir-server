# Semantic Search Demo Verification

Use this checklist during the final rehearsal and keep a completed copy available
as a backup during the demo. Ranking among paraphrased passages can vary
with the deployed embedding-model version; deterministic scope, exact calibration,
and provenance checks must not vary.

## 1. Configure

- [ ] The server reports SQL schema version 118.
- [ ] Vector search is enabled with 1,536 dimensions and synchronous indexing.
- [ ] The registry contains the three active, searchable Microsoft semantic
      SearchParameters.
- [ ] Each `SearchParameter/$status` request returns its requested canonical URL
      with a searchable status.
- [ ] The CapabilityStatement advertises `semantic-text` for Observation,
      DiagnosticReport, and DocumentReference.

## 2. Ingest And Index

- [ ] All 27 resource PUTs complete without an `OperationOutcome` error.
- [ ] Primary-patient counts are exactly three Observations, three
      DiagnosticReports, and five DocumentReferences.
- [ ] `Binary/demo-binary-prior-ed-long` decodes as UTF-8 text and is 3,438
      characters long.
- [ ] The long text Binary is 5,928 bytes and its DocumentReference attachment
      size and SHA-1 hash match the decoded bytes.
- [ ] The PDF Binary opens as a two-page text PDF, is 8,497 bytes, and its
      DocumentReference attachment size and SHA-1 hash match the decoded bytes.
- [ ] No embedding endpoint, dimension, PDF extraction, or SQL vector persistence
      error appears in server logs during the 12 vectorized owner writes.

## 3. Standard Search

### Exact Observation calibration

- [ ] `Observation/demo-obs-exact-presyncope` is first.
- [ ] Its `Bundle.entry.search.score` is `1` or differs only by serialization
      precision.
- [ ] `Observation/demo-control-obs-exact-presyncope` is absent even though its
      note is identical, because it belongs to another patient.
- [ ] The evidence SearchParameter is the Observation canonical.
- [ ] The evidence source path is `Observation.note.text`.

### DiagnosticReport paraphrase

- [ ] `DiagnosticReport/demo-report-cardiology` is in the high-relevance tier.
- [ ] `DiagnosticReport/demo-report-mammography` ranks below the clinically
      related cardiology and head-CT conclusions.
- [ ] The winning evidence text is an exact substring of `conclusion`.
- [ ] The evidence source path is `DiagnosticReport.conclusion`.

### Binary-backed DocumentReference

- [ ] `DocumentReference/demo-doc-prior-ed` is in the high-relevance tier and
      ranks above `demo-doc-dermatology`.
- [ ] The evidence is one passage, not the full 3,438-character note.
- [ ] The evidence `chunkOrdinal` is greater than zero for the prior ED note.
- [ ] The source is
      `Binary/demo-binary-prior-ed-long/_history/{version}`.
- [ ] The source path is `Binary.data`.
- [ ] The SearchParameter canonical is the DocumentReference canonical, showing
      that the DocumentReference owns the index while Binary owns the text.

## 4. Patient-Wide Search

### Clinical query, all supported types

- [ ] The response is a FHIR `searchset` Bundle.
- [ ] Up to ten entries are returned from the primary patient only.
- [ ] Observation, DiagnosticReport, and DocumentReference entries are mixed in
      one score-descending order.
- [ ] The clinically direct group includes the exact symptom Observation,
      orthostatic-vitals Observation, cardiology conclusion, and prior ED note.
- [ ] Glucose, mammography, and dermatology controls remain below clinically
      direct records.
- [ ] Every match has a score and one or more semantic-evidence extensions.

Do not require one rigid order among paraphrased high-relevance records. Model
versions may exchange adjacent results. Treat patient leakage, missing provenance,
irrelevant records above all direct records, or absent direct records as failures.

### Exact calibration, all supported types

- [ ] `Observation/demo-obs-exact-presyncope` is first with score approximately
      `1`.
- [ ] The other patient's identical Observation is absent.
- [ ] Scores are monotonically non-increasing.

### Type filters

- [ ] The DocumentReference-only request returns exactly five
      DocumentReferences and no other resource type.
- [ ] The repeated Observation/DiagnosticReport filter returns no
      DocumentReference.
- [ ] Repeated type parameters produce one globally ranked subset, not separate
      per-type Bundles.

## 5. Long Text And PDF Search

- [ ] The positional-spinning query ranks `demo-doc-long-vestibular-text` first
      with evidence mentioning the right Dix-Hallpike or Epley maneuver.
- [ ] The active-stand query ranks `demo-doc-long-autonomic-pdf` first and reports
      `Binary.data#page=1`.
- [ ] The hot-shower and stopped-medication query ranks the PDF first and reports
      `Binary.data#page=2`.
- [ ] The cross-format query returns both long documents in the top relevance tier.
- [ ] The skin-treatment control ranks `demo-doc-dermatology` above both long
      documents, showing that document length alone does not drive relevance.

## 6. Vector Reindex Backfill

- [ ] The proof Observation is written before its unique vector SearchParameter
      is created.
- [ ] Before reindex, the response has no semantic score/evidence for the unique
      canonical. The independent `_id` predicate may still return the resource.
- [ ] The resource-scoped system `$reindex` reaches `Completed`.
- [ ] The same semantic query returns the preexisting Observation with score and
      evidence after reindex.
- [ ] The Observation `meta.versionId` is unchanged, proving that search storage
      was rebuilt without rewriting the FHIR resource.

## Evidence Shape

Inspect `Bundle.entry.search.extension` for:

```text
url = http://microsoft.com/fhir/StructureDefinition/semantic-search-evidence
  text             exact winning passage
  chunkOrdinal     zero-based passage number
      rank             one-based relevance rank across evidence on this response page
  searchParameter  canonical URL that selected the text
  source           version-specific FHIR source reference
  sourcePath       Observation.note.text, DiagnosticReport.conclusion, or Binary.data
```

Evidence ranks are dense across the current page (`1` through the number of
evidence extensions), do not restart per resource, and restart at `1` on each
Bundle page. No `globalRank` field is returned.

The score is a cosine-derived relevance value used for ordering. It is not a
probability, diagnosis, confidence estimate, or authorization decision.

## Backup Capture

After a successful rehearsal, retain redacted response captures for:

1. Exact Observation calibration.
2. Binary-backed DocumentReference search with expanded evidence.
3. Mixed-resource patient search.
4. DocumentReference-only type filter.

Use captures only if the live environment fails; state clearly that they are from
the completed rehearsal.