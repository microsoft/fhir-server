# SQL Semantic Search: Expert Q&A

Answer each question in two layers:

1. Give the bold sentence first.
2. Add the supporting explanation only if the questioner wants it.

This keeps your answers clear for a mixed audience while preserving technical
depth for FHIR experts.

## Your 60-Second Summary

> I added opt-in SQL semantic search to Microsoft FHIR Server. Active
> vector-enabled SearchParameters configure which narrative fields are indexed.
> During a normal SQL resource write, configured text is resolved, split into
> overlapping passages, embedded through Azure Foundry, and persisted with the
> resource using SQL schema 118. At query time, deterministic FHIR constraints
> select eligible resources and SQL cosine distance ranks their best passages.
> Results remain FHIR resources in a searchset Bundle, with a score and an
> extension containing the exact passage and source provenance. Ordinary
> resource search supports semantic/hybrid queries, while a custom Patient
> operation provides one globally ranked list across Observation,
> DiagnosticReport, and DocumentReference.

## Vocabulary You Should Use

| Prefer | Avoid or qualify |
|---|---|
| semantic search | “AI search” |
| vector similarity ranking | “the model understands the chart” |
| deterministic FHIR constraints | “metadata stuff” |
| eligible candidate resources | “all the records” |
| matched passage or winning chunk | “the answer” |
| relevance score | confidence, probability, correctness score |
| ingest and index | upload |
| custom FHIR operation | standard FHIR semantic search |
| source provenance | citation, unless you explain the custom extension |
| controlled SQL demo | production-ready system |

## Architecture And FHIR Questions

### What did you actually build?

**An opt-in SQL vector index integrated with FHIR SearchParameters, normal
resource writes, normal resource search, and a custom patient-wide operation.**

The feature includes configuration, Azure Foundry embeddings, direct/Binary text
resolution, overlapping passages, schema 118 persistence and vector-aware
reindex, SQL cosine ranking, scores, source evidence, and focused tests.

### Is this vector search, semantic search, or hybrid search?

**Vector similarity is the ranking mechanism; semantic search is the user
capability; hybrid search combines it with deterministic FHIR constraints.**

A query that only uses the vector-enabled parameter is semantic search. A query
that also uses patient, date, code, category, or another normal parameter is
hybrid search. The custom Patient operation is currently hybrid by patient and
optional resource type.

### Is this standard FHIR?

**It preserves standard FHIR resources and search behavior, but the vector
extension and patient-wide semantic operation are custom.**

FHIR supports custom SearchParameters, operations, and extensions. FHIR does not
standardize embedding semantics or global semantic ranking across heterogeneous
resource types. Describe this as a FHIR-native extension pattern, not a standard
FHIR feature.

### Why use SearchParameter resources?

**They provide the existing FHIR-native abstraction for declaring which element
is searchable on which resource type.**

This avoids a hard-coded switch for every resource/field, lets deployments choose
their narrative surfaces, uses canonical URLs and base resource types, and fits
the server's definition registry and SQL SearchParam IDs.

### Why must the SearchParameter use `type=special`?

**Because vector similarity does not follow the matching rules of a standard
FHIR string, token, reference, or URI parameter.**

`special` explicitly signals implementation-defined semantics. The parser then
requires the vector extension and emits a dedicated `VectorSearchExpression`.

### Why are both server configuration and SearchParameter registration needed?

**The SearchParameter defines the FHIR search surface; server configuration
enables and operationally allow-lists that surface.**

The registered resource supplies canonical URL, code, base types, status, and
expression. Configuration supplies model, dimensions, chunking, limits, and the
canonicals an operator permits for vector indexing.

### Why not just use `_text` or `_content`?

**Those parameters provide text matching, not model-based semantic similarity,
passage ranking, or embedding-model identity.**

This work is complementary. It selects specific configured narrative fields and
ranks concepts even when wording differs.

### Why create `POST /Patient/{id}/$semantic-search`?

**Standard search returns one resource type at a time and does not define one
semantic order across heterogeneous resources.**

The operation creates an explicit contract: patient scope, query, count, and
optional supported types, followed by one globally ranked searchset Bundle.

### Why not use `Patient/{id}/$everything`?

**`$everything` is broad patient-compartment retrieval; it is not relevance
ranking over selected narrative passages.**

It can be a source of records for another workflow, but using it directly would
retrieve far more data and still require eligibility rules, indexing, and global
semantic ranking.

### Does ordinary FHIR search still work?

**Yes. Vector processing is conditional and returns null when no vector
expression exists.**

The feature is disabled by default. Existing search follows the existing path
unless a registered vector parameter is present.

### Can semantic search use every normal FHIR filter?

**The resource-level path composes with normal structured search expressions;
the custom Patient operation currently exposes only patient, resource type, and
count.**

Do not claim date, encounter, author, specialty, or category parameters on the
custom operation yet. They are natural future extensions.

## Indexing Questions

### What is the difference between ingestion and indexing?

**Ingestion stores the FHIR resource; semantic indexing derives passages and
vectors that make configured text searchable.**

The client still uses normal create/update interactions. The SQL write pipeline
adds indexing before its merge.

### When are embeddings generated?

**They are generated synchronously during the current SQL write path.**

All pending passages in the write collection are sent in one embedding batch.
Only after embeddings validate are vector entries passed into SQL merge.

### Why synchronous indexing?

**It is the simplest consistency model for the prototype: a successful current
resource write has corresponding current vectors.**

The tradeoff is write latency and dependence on the embedding service. A
production design likely needs asynchronous indexing, status tracking, retries,
and reconciliation.

### Is chunking token-aware?

**No. The current settings are named `*Tokens`, but `TextChunker` implements an
overlapping character window.**

The defaults are window 800 and overlap 100. The name reflects the intended
abstraction; a production implementation should use the deployed model's
tokenizer and reconcile `maxInputTokens` with actual model limits.

### Why overlap passages?

**Overlap reduces the chance that a clinically meaningful statement is split at
a chunk boundary.**

It increases storage and embedding work, so window and overlap require empirical
tuning.

### What do extraction policies do?

**They control how multiple values from one SearchParameter become source
passages.**

- `FirstValue` indexes only the first resolved value.
- `Concatenate` joins values with the same source provenance.
- `PerValueRow` keeps each resolved value separate before chunking.

### What Binary content is supported?

**A local relative `Binary/{id}` containing UTF-8 `text/plain` or a text-based
`application/pdf`.**

Absolute URLs, malformed references, missing/deleted/history resources,
unsupported MIME types or charsets, invalid UTF-8, malformed or encrypted PDFs,
oversized data, and empty content are skipped. PDF text is indexed per page with
page-specific source provenance.

### Are PDF, CDA, Word, image, or audio extraction implemented?

**Text-based PDF extraction is implemented with PdfPig. CDA, Word, image, audio,
and OCR for scanned PDFs are not implemented.**

PDF file size, page count, extracted character count, and elapsed extraction
time are bounded by server configuration. Other formats require specialized
extraction and often OCR or transcription.

### What happens when a resource is updated?

**The resource write regenerates its vector entries and merge replaces the
previous current rows.**

Deleted/history wrappers are not newly indexed. Existing vectors are managed by
the merge procedure's current-resource lifecycle.

### What happens when a referenced Binary changes?

**The current implementation does not automatically reindex all
DocumentReferences that point to that Binary.**

For the static demo, load the Binary before the DocumentReference and do not
modify it. Production needs dependency tracking and owner reindexing on Binary
update/delete.

### Why store a source hash?

**It records the exact indexed passage identity and enables future change or
deduplication logic without reinterpreting text.**

The current indexer computes SHA-256 over UTF-8 passage text. Do not claim a full
cross-resource deduplication feature; the field primarily preserves identity.

## SQL And Ranking Questions

### What did schema 117 add?

**An embedding-model registry, vector passage table, vector TVP, and merge
procedure support.**

Each passage row connects an owner resource and SearchParameter to chunk text,
hash, source provenance, embedding model, and `vector(1536)` value.

### Why store embedding model name and version?

**Vectors from different model versions should not be assumed comparable.**

Index and query paths resolve the same small model ID. SQL filters vector rows by
that ID before calculating distance.

### Why are dimensions fixed at 1536?

**The current SQL column is `vector(1536)`, so startup validation prevents a
model/schema mismatch.**

Supporting another dimension requires schema and configuration evolution, not
only changing the endpoint.

### How does resource-level SQL ranking work?

**Structured expressions identify eligible resources; a correlated `CROSS
APPLY` selects the closest indexed chunk for each resource.**

The apply filters by owner, SearchParam ID, and embedding model ID, calculates
`VECTOR_DISTANCE('cosine', ...)`, selects `TOP (1)` chunk, and orders resources
by distance.

### How does the Patient operation rank across types?

**It embeds once, ranks candidate IDs for each resource type and enabled
SearchParameter, keeps each resource's best hit, then globally sorts scores.**

It uses `(ResourceTypeName, ResourceSurrogateId)` as identity to avoid treating
a surrogate ID alone as globally unique across resource types.

### Is the vector query approximate nearest neighbor search?

**No. The current implementation computes exact distance over an already bounded
candidate set.**

The patient operation bounds candidates with ordinary FHIR searches. This is
appropriate for a prototype but requires performance evaluation and possibly a
vector index strategy at larger scale.

### Is SQL vulnerable to injection from the query or candidate IDs?

**The vector SQL uses bound parameters; user input is not concatenated into the
query text.**

The direct vector store sends candidate surrogate IDs as one bound value and
splits it server-side. Query embeddings and metric are also parameters.

### What does a score of 1 mean?

**It means cosine distance was approximately zero after normalization, usually
because query and indexed passage embeddings were effectively identical.**

Resource-level search calculates:

$$
\operatorname{score}=\operatorname{clamp}\left(1-\frac{d_{\cos}}{2},0,1\right)
$$

The score is for ranking. It is not a probability, diagnosis, confidence level,
or clinical validation.

### Can scores be compared across models?

**Not safely. Score distributions depend on model, data, chunking, and query.**

The registry prevents mixed-model distance calculations, but threshold tuning
still needs evaluation for each deployment.

## Evidence And Explainability Questions

### What evidence is returned?

**The exact winning passage, its ordinal, the selecting SearchParameter
canonical, source reference, and source path.**

The custom extension is attached to `Bundle.entry.search`, beside FHIR's
`search.score`.

### Why return passage text instead of only the source resource?

**A resource may contain many chunks; the passage explains which text produced
the ranking.**

This lets a caller inspect the result without guessing which paragraph matched.

### For DocumentReference, why can evidence point to Binary?

**The DocumentReference is the ranked owner, but the Binary is the resource that
actually contains the indexed text.**

Persisting both owner identity and source provenance preserves that distinction.

### Is this RAG?

**It implements the retrieval foundation, not generation.**

There is no LLM answer synthesis, prompt construction, citation validation, or
clinical response generation in this feature.

## Security And Authorization Questions

### Does vector search bypass FHIR authorization?

**The patient operation checks read access and applies the existing resource data
filter before ranking owner candidates.**

The resource-level path runs inside the existing search pipeline. The design
principle is eligibility before semantic relevance.

### Is linked Binary evidence fully production-secure?

**Not yet. The owner candidates are filtered, but linked Binary source text is
not independently passed through resource-level authorization before evidence is
returned.**

This is acceptable only for a controlled synthetic demo with aligned access. It
is explicit production follow-up work.

### How does the demo prove patient isolation?

**Put an exact query match on Patient B and search Patient A; Patient B must not
appear even though its semantic score would be strongest.**

This demonstrates deterministic patient filtering occurs before relevance can
affect output.

### Are embeddings sensitive data?

**They are derived from clinical text and must be governed as sensitive data,
even though they are not readable narrative by themselves.**

Deployment must apply the same data residency, access, transport, retention, and
monitoring expectations as other clinical data. The production client uses
Azure credentials rather than embedding an API secret in configuration.

## Reliability, Scale, And Scope Questions

### What happens if the embedding service fails during a write?

**In synchronous mode, indexing throws before SQL merge, so the write fails
rather than silently committing a resource with stale or absent current
vectors.**

That consistency is useful for the prototype but motivates asynchronous retry
and reconciliation for production availability.

### Is indexing transactional with the resource?

**Generated vector rows are submitted in the same merge procedure call as the
resource and normal search parameters.**

The external embedding request necessarily happens before that SQL transaction.

### Does the custom operation paginate?

**No. It searches one bounded candidate page per selected type and returns up to
the requested count.**

The default candidate count is 100 per type and max result count is 50. A real
continuation contract for stable mixed-resource semantic ranking is deferred.

### Does resource-level vector search pagination work?

**Yes. Relevance-ordered resource search uses a keyset cursor containing cosine
distance, resource type ID, and resource surrogate ID.**

The next page applies the matching SQL predicate, so default relevance order and
explicit `_sort=_score` remain stable across pages. SQL unit coverage verifies
the token and predicate contract; the final release rehearsal should still
exercise a live multi-page request on the exact build being presented.

### Why SQL only?

**SQL Server has the native vector type and distance function used by this
implementation, and the demo was deliberately scoped to one backend.**

Core abstractions reduce coupling, but there is no Cosmos persistence/query
implementation yet.

### Is this production-ready?

**It is a substantial SQL implementation suitable for a controlled demo, not a
production-complete feature.**

Production work includes linked-source authorization, Binary dependency
reindexing, asynchronous indexing, operational telemetry/retries, patient-operation
pagination, performance evaluation, broader format extraction, and Cosmos only
if required.

## Demo And Validation Questions

### What should the demo prove?

**Configuration controls indexing; normal FHIR writes create vectors; FHIR scope
limits eligibility; semantic similarity changes order; evidence remains
traceable.**

Use five deliberate checks:

1. exact same-patient text scores near 1;
2. a clinical paraphrase ranks strongly;
3. a long Binary note returns its relevant middle passage;
4. irrelevant same-patient content ranks lower;
5. exact text for another patient is excluded.

### What has been tested?

**Automated coverage and live local validation exercise indexing, retrieval,
evidence, patient isolation, PDF provenance, and vector-aware reindexing.**

Focused Core, handler, controller, SQL, and serialization tests pass. Live
schema 118 rehearsals have proven resource ingestion, direct and Binary-backed
search, mixed patient results, control-patient exclusion, and `$reindex`. Repeat
the focused build and live requests on the exact release commit before a demo.

### Why use deterministic embeddings in tests?

**They make tests offline, repeatable, and independent of model/network changes.**

They validate orchestration and persistence, not clinical semantic quality. The
demo with Azure Foundry is what shows realistic paraphrase ranking.

## Questions You Should Redirect Carefully

Use these patterns when a question exceeds the implemented scope.

> That is not validated in the current SQL implementation. The current behavior is
> X, and the production follow-up would need Y.

> I do not want to guess. The owning code is in X; I have captured Y as an open
> design decision.

> The demo proves retrieval and ranking, not clinical correctness or answer
> generation.

Never invent an answer to preserve momentum. Precise scope is a stronger
technical response than unsupported certainty.

## Rehearsal Drill

Practice in four rounds:

1. Explain the entire feature in 60 seconds without code.
2. Draw write and query flows from memory in five minutes.
3. Answer every bold sentence in this document without reading the details.
4. Open the relevant file from the 20-file list for ten randomly selected
   questions.

You are ready when you can move from **claim -> design reason -> owning file ->
known limitation** without narrating individual lines of code.