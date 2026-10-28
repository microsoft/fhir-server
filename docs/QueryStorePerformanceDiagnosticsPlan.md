# Implement hourly query-performance evidence

Give responders and SRE agents an hourly record of expensive queries, complete compiled plans, and the statistics that informed those plans.
Use the collector in #5723 as the starting point.
Deliver five changes in order, QS-01 through QS-05.
Keep optimization of the existing request-triggered Query Store lookup separate.
This document is a proposed implementation plan, not authorization to run it or a claim of measured CPU savings.

## How to read this

One box is one unit of work. Every box names the evidence that checks it.
Check a box only when its evidence exists. Attach the command, result, and exact base and head revisions.
Keep implementation, verification, and rollout receipts with the change that produces them.

For agent execution, use `skills/poteto-mode/playbooks/autopilot-stack.md` from the installed plugin.
The operator reviews and merges every PR. This request does not authorize implementation, a scheduled workflow, new PRs, deployment, or customer-database access.
The identifiers QS-01 through QS-05 name planned changes, not existing GitHub PR numbers.

Tests alone are not sufficient verification. A PR is verified only when its unit, live, and perf boxes are all checked.

## Program checklist

### Arm the program

- [ ] Obtain the operator's explicit go before implementation. Record the approval with the execution receipts.
- [ ] Write the standing orders with this document's path, the QS-01 through QS-05 order, the verification rule, and operator-only merge authority.
- [ ] Read `AGENTS.md`, the current baseline specification, ADR-2608, and the execution playbook from the installed plugin.
- [ ] Apply Sequence Work into Verifiable Units. Finish each change's evidence before advancing its dependent change.
- [ ] If the operator starts an autonomous program, configure its hourly audit under the execution playbook. Report a status message only for a changed finding or decision.
- [ ] Settle the proposed limits and unresolved decisions in Appendix E before enabling the new mode. Record the accepted values, not guessed benchmark results.

### Spawn owners

- [ ] Assign an owner to each change. Use separate worktrees if owners run concurrently. Do not start agents merely to read this plan.
- [ ] Follow `QS-01 -> QS-02 -> QS-03 -> QS-04 -> QS-05`. Stack dependent work only under the execution playbook.
- [ ] Reconcile #5723 with current `main` before QS-01. Record the integrated base and retain its existing behavior behind the existing disabled-by-default feature.
- [ ] Include #5795's FHIR-shape annotations in the common baseline. Do not treat #5895's composite-value hashing fix as a Query Store lookup optimization.
- [ ] Keep SQL readers and SQL identities in `Microsoft.Health.Fhir.SqlServer`. Keep Core contracts SQL-agnostic and Azure storage implementation outside the SQL project.

### PR mechanics

- [ ] Use `gh` for GitHub operations. Inspect the repository PR template before writing each body.
- [ ] Attach the base SHA, head SHA, exact verification commands, and their results to each PR.
- [ ] Build before adding tests. Use the repository SDK and test runner described in Appendix D.
- [ ] Preserve existing configuration semantics. Introduce the richer selection as an explicit opt-in mode instead of changing what the current duration threshold means.
- [ ] Update the baseline specification when its behavior changes. Use `create-adr` before changing ADR-2608 or proposing an artifact-storage decision.
- [ ] Run `deslop` before commits and `no-comments` before review. Do not include unrelated cleanup.

### Verdict and merge

- [ ] Review each implementation and its receipts independently at the exact code-ready SHA. Re-run affected evidence after changes.
- [ ] Keep merge blocked when live SQL, artifact access, or performance evidence is unavailable. Passing unit tests alone do not clear the block.
- [ ] Obtain privacy and storage-access approval before exporting query text or plan artifacts outside the approved log destination.
- [ ] Stop each owner at merge-ready. Only the operator merges. Never deploy, force-push a shared branch, or query a customer database without separate approval.

### Boot recipe

- [ ] Use an isolated SQL test database, synthetic FHIR requests, and the existing integration-test fixture. Never enable Query Store or create probe tables in a customer database.
- [ ] Use Appendix D's build and test commands. Add repeatable SQL fixtures and replay commands alongside the existing integration tests as each change needs them.
- [ ] Run each live scenario against its PR head. Run the regression lane against the pinned integrated baseline with the same data and configuration.
- [ ] Save headless evidence as JSON, XML, and text under `artifacts/query-store/<change-id>/lane-<n>/`. Record the command and SHA beside each result. Do not fabricate screenshots for SQL tests.
- [ ] If an agent runs the verification matrix, use the configured swarm model only after execution approval. A lane below is a scenario, not authorization to launch a workflow now.

## Bound collection and identify its evidence (QS-01)

**Depends on.** None. Continue from the collector in #5723 after integration with current `main`.

**Files.**

- [ ] Edit `src/Microsoft.Health.Fhir.Core/Configs/QueryStoreDiagnosticsConfiguration.cs`.
- [ ] Edit `src/Microsoft.Health.Fhir.SqlServer/Features/Watchdogs/QueryStoreDiagnostics/` and its existing unit tests.
- [ ] Edit `test/Microsoft.Health.Fhir.Shared.Tests.Integration/Persistence/QueryStoreDiagnosticsWatchdogTests.cs` and the baseline specification.

**Build.**

- [ ] Add versioned collection and interval identities from Appendix E. Prove that repeated reads cannot become additive duplicate CPU totals.
- [ ] Collect explicitly identified closed Query Store intervals. Report their actual boundaries, capture gaps, execution types, and incomplete sections.
- [ ] Validate both timer periods against the real timer and conversion limits. Cover the sibling-task lifecycle instead of testing positive numbers alone.
- [ ] Add an end-to-end deadline, diagnostic-specific timeout and retry limits, and byte-aware log batching. Report budget exhaustion and failed sections explicitly.
- [ ] Verify collector-procedure identity exclusion, including metadata visibility and historical entries, or replace it with a tested collector-specific marker. Classify legacy diagnostic lookups separately as overhead.
- [ ] Treat the lease as coordination, not fencing. Make output idempotent across retries, restarts, and overlapping lease holders.

**You see.**

- [ ] Save one collection manifest showing its interval keys, section statuses, elapsed time, rows, bytes, and completion state. Save a repeated-run result showing no doubled totals.

**Verify, unit.** Tests alone are not sufficient verification. A PR is verified only when its unit, live, and perf boxes are all checked.

- [ ] Extend the existing period, lease-period, batching, and configuration tests with actual timer boundaries, long Unicode identifiers, cancellation, and incomplete collections. Run Appendix D's targeted unit command.

**Verify, live.** Tests alone are not sufficient verification. A PR is verified only when its unit, live, and perf boxes are all checked. Ten lanes on the configured `swarm workers` model at the PR head when agent execution is approved.

- [ ] Lane 1. Regression lane against trunk. Run the disabled collector and existing duration mode on baseline and head. Save `artifacts/query-store/QS-01/lane-1/regression.json`. Pass when disabled mode reads nothing and existing enabled behavior is preserved.
- [ ] Lane 2. Read two closed intervals and one active interval. Save `artifacts/query-store/QS-01/lane-2/intervals.json`. Pass when only the closed intervals enter completed totals.
- [ ] Lane 3. Replay the same intervals after restart. Save `artifacts/query-store/QS-01/lane-3/replay.json`. Pass when downstream replacement by record key does not double totals.
- [ ] Lane 4. Introduce late-arriving captured data. Save `artifacts/query-store/QS-01/lane-4/revision.json`. Pass when the interval receives a replacement revision instead of an additive delta.
- [ ] Lane 5. Exercise unsupported timer values through watchdog startup. Save `artifacts/query-store/QS-01/lane-5/timers.txt`. Pass when diagnostics cannot fault the shared watchdog fleet.
- [ ] Lane 6. Delay a SQL read beyond the deadline. Save `artifacts/query-store/QS-01/lane-6/deadline.json`. Pass when cancellation stops collection and the manifest reports failure.
- [ ] Lane 7. Generate long multibyte statistics identifiers. Save `artifacts/query-store/QS-01/lane-7/bytes.json`. Pass when every serialized record respects the accepted byte budget.
- [ ] Lane 8. Lose the lease during collection and start another holder. Save `artifacts/query-store/QS-01/lane-8/lease.json`. Pass when duplicate output cannot create duplicate logical records.
- [ ] Lane 9. Run collector SQL and legacy lookup SQL. Save `artifacts/query-store/QS-01/lane-9/exclusion.json`. Pass when only identified collector work is excluded from workload rankings and its cost remains independently measurable.
- [ ] Lane 10. Exercise disabled, READ_ONLY, unavailable, and permission-denied Query Store states. Save `artifacts/query-store/QS-01/lane-10/states.json`. Pass when readable history is collected where supported and every unavailable case has an explicit status.

**Verify, perf.** Tests alone are not sufficient verification. A PR is verified only when its unit, live, and perf boxes are all checked.

- [ ] Metric. Record total SQL CPU-seconds and host CPU-seconds per successful FHIR request, request p95, collector SQL CPU, and collector wall time.
- [ ] Probe. Run Appendix E's interleaved baseline/head replay with equal Query Store cardinality and successful request mix. Include cancelled collector work in independent node-level accounting.
- [ ] Baseline. Measure the integrated baseline with collection disabled first. Also measure its existing enabled duration collector where available.
- [ ] Rule. Apply Appendix E's proposed 5% workload-regression gate and absolute collection budgets after approval. Never claim a speedup against a baseline that performs no collection.

**Review gate.** None. QS-01 changes background diagnostics, not an operator interaction. Privacy approval still gates external emission.

**Merge.**

- [ ] Attach the independent review and all unit, live, and perf receipts at the current head. Obtain the operator's merge.

## Rank hourly workload cost and regressions (QS-02)

**Depends on.** QS-01.

**Files.**

- [ ] Edit `QueryStoreDiagnosticsConfiguration.cs`, the Query Store diagnostics reader and models, their unit tests, and the shared SQL integration tests.
- [ ] Update `docs/QueryStorePerformanceDiagnostics.md` with new settings, units, ranking rules, and selection limits.

**Build.**

- [ ] Add an explicit workload-snapshot mode. Retain the existing duration mode and its configuration meanings.
- [ ] Aggregate once per selected interval set. Compute weighted CPU, duration, reads, execution counts, and waits with execution outcomes kept distinguishable.
- [ ] Rank queries across all their plans. Select a bounded union of CPU, duration, reads, waits, and regression candidates. Attach every inclusion reason and its rank.
- [ ] Retain plan-level contributions for selected queries. Report budget-limited coverage rather than hiding omitted plans or rows.
- [ ] Include frequently executed queries below the existing average-duration threshold. Keep that threshold confined to legacy duration selection.
- [ ] Compare matching query identities in explicit baseline and current windows. Require sample counts and absolute cost changes before labeling a regression candidate.
- [ ] Use native hashes and FHIR shapes for grouping only. Preserve database, query, plan, context, replica, and interval identities where supported.
- [ ] Carry #5795's existing value-free FHIR-shape annotation when present. Keep the annotation optional and preserve custom-query hashing.

**You see.**

- [ ] Save a ranked hourly manifest containing a frequent fast CPU-heavy query, a rare slow query, and a multi-plan regression with its baseline.

**Verify, unit.** Tests alone are not sufficient verification. A PR is verified only when its unit, live, and perf boxes are all checked.

- [ ] Test weighted aggregation, query rollup across plans, deterministic ties, multi-ranking deduplication, missing waits, sparse baselines, and explicit truncation statuses with focused tests. Run Appendix D's targeted unit command.

**Verify, live.** Tests alone are not sufficient verification. A PR is verified only when its unit, live, and perf boxes are all checked. Ten lanes on the configured `swarm workers` model at the PR head when agent execution is approved.

- [ ] Lane 1. Regression lane against trunk. Compare legacy duration mode on baseline and head, then enable the new mode only on head. Save `artifacts/query-store/QS-02/lane-1/regression.json`. Pass when legacy selection remains unchanged and the new mode exposes the additional intended workload.
- [ ] Lane 2. Execute a fast query repeatedly. Save `artifacts/query-store/QS-02/lane-2/frequent.json`. Pass when its total CPU qualifies despite low average duration.
- [ ] Lane 3. Execute a rare slow query. Save `artifacts/query-store/QS-02/lane-3/slow.json`. Pass when duration ranking retains it without requiring CPU dominance.
- [ ] Lane 4. Capture several plans for one query. Save `artifacts/query-store/QS-02/lane-4/plans.json`. Pass when query totals equal their plan contributions.
- [ ] Lane 5. Make one query qualify in several rankings. Save `artifacts/query-store/QS-02/lane-5/dedup.json`. Pass when it appears once with all inclusion reasons.
- [ ] Lane 6. Produce a read-heavy workload. Save `artifacts/query-store/QS-02/lane-6/reads.json`. Pass when reads ranking selects the expected query.
- [ ] Lane 7. Disable or deny wait capture. Save `artifacts/query-store/QS-02/lane-7/waits.json`. Pass when missing waits are not reported as zero measured waits.
- [ ] Lane 8. Introduce a reproducible plan regression. Save `artifacts/query-store/QS-02/lane-8/regression-candidate.json`. Pass when the report identifies both windows, plans, and sample counts without asserting causality.
- [ ] Lane 9. Exercise aborted queries and sparse samples. Save `artifacts/query-store/QS-02/lane-9/outcomes.json`. Pass when outcomes remain visible and insufficient samples do not become confident regression claims.
- [ ] Lane 10. Exceed query and plan limits. Save `artifacts/query-store/QS-02/lane-10/limits.json`. Pass when deterministic selection and explicit coverage flags respect every accepted cap.

**Verify, perf.** Tests alone are not sufficient verification. A PR is verified only when its unit, live, and perf boxes are all checked.

- [ ] Metric. Measure collector SQL CPU, logical reads, query count, host allocations, emitted bytes, and end-to-end request CPU and p95.
- [ ] Probe. Replay the same seeded small and large Query Store catalogs at baseline and head. Save actual diagnostic query plans and repeat Appendix E's interleaved procedure.
- [ ] Baseline. Record QS-01 duration-mode cost and disabled-collector workload cost before enabling the new rankings.
- [ ] Rule. Keep the approved 5% workload-regression limit and collection budgets. Fail any implementation that repeats the full aggregation separately for each ranking without measured justification.

**Review gate.** None. QS-02 changes background records, not an operator interaction.

**Merge.**

- [ ] Attach hand-calculated fixture totals, actual SQL plans, and all verification receipts. Obtain the operator's merge.

## Save complete versioned plan artifacts (QS-03)

**Depends on.** QS-02.

**Files.**

- [ ] Edit the Query Store diagnostics sanitizer, plan model, emission code, and their tests.
- [ ] Add a small SQL-agnostic diagnostics artifact-writer contract under `src/Microsoft.Health.Fhir.Core/Features/Diagnostics/` if existing storage helpers cannot supply it.
- [ ] Add its Azure implementation and registration under `src/Microsoft.Health.Fhir.Azure/`, with tests under `src/Microsoft.Health.Fhir.Azure.UnitTests/`.
- [ ] Update the specification and use `create-adr` for the accepted artifact-storage decision. Keep deployment-specific storage policy in the PaaS integration.

**Build.**

- [ ] Preserve complete sanitized compiled Showplan XML. Replace hard-cut plan output with an artifact reference and explicit persistence status.
- [ ] Keep XML parsing, sanitization, and upload within input, output, memory, deadline, and retry limits. Reject oversized input explicitly instead of publishing malformed XML.
- [ ] Version artifacts by database scope, query and plan identities, content digest, and sanitizer version. A plan ID alone cannot identify changing artifact content.
- [ ] Deduplicate unchanged artifacts across ticks and restarts. Keep current and comparison-plan references where available within the budget.
- [ ] Emit small structured manifests with checksum, uncompressed length, format, creation time, and access-controlled artifact identity. Never log bearer tokens or credential-bearing URLs.
- [ ] Register a provider explicitly. Report `NotConfigured`, `Unavailable`, `Rejected`, `BudgetExceeded`, or `Failed` instead of a successful reference when persistence cannot complete.
- [ ] Reuse Azure SDK and authentication helpers where suitable. Do not couple diagnostics to the synchronous, export-job-specific `IExportDestinationClient`.

**You see.**

- [ ] Save a manifest and retrieve its referenced large plan through an authorized test consumer. Parse the complete XML and verify its checksum and sanitizer version.

**Verify, unit.** Tests alone are not sufficient verification. A PR is verified only when its unit, live, and perf boxes are all checked.

- [ ] Extend sanitizer fixtures for large plans, namespaces, prohibited metadata, input limits, and idempotent writes. Test the generic writer contract and Azure failure handling. Run targeted SQL and Azure unit tests.

**Verify, live.** Tests alone are not sufficient verification. A PR is verified only when its unit, live, and perf boxes are all checked. Ten lanes on the configured `swarm workers` model at the PR head when agent execution is approved.

- [ ] Lane 1. Regression lane against trunk. Compare small-plan collection on baseline and head, then retrieve a large artifact on head. Save `artifacts/query-store/QS-03/lane-1/regression.json`. Pass when summaries remain available and the new artifact is complete.
- [ ] Lane 2. Archive a plan exceeding the old log-field limit. Save `artifacts/query-store/QS-03/lane-2/complete.xml`. Pass when an XML parser accepts the full sanitized plan.
- [ ] Lane 3. Recollect an unchanged plan after restart. Save `artifacts/query-store/QS-03/lane-3/dedup.json`. Pass when the existing artifact is reused.
- [ ] Lane 4. Change captured content under an existing plan identity. Save `artifacts/query-store/QS-03/lane-4/version.json`. Pass when the content receives a distinct immutable reference.
- [ ] Lane 5. Exercise authorized and unauthorized readers. Save `artifacts/query-store/QS-03/lane-5/access.json`. Pass when only the intended reader can retrieve the artifact.
- [ ] Lane 6. Inject upload timeouts and retryable failures. Save `artifacts/query-store/QS-03/lane-6/failure.json`. Pass when budgets hold and the manifest never claims an absent artifact exists.
- [ ] Lane 7. Exceed the input and upload byte caps. Save `artifacts/query-store/QS-03/lane-7/oversize.json`. Pass when explicit status replaces partial XML or unbounded memory use.
- [ ] Lane 8. Include synthetic parameter-value metadata. Save `artifacts/query-store/QS-03/lane-8/sanitized.xml`. Pass when prohibited metadata is absent from uploaded content and logs.
- [ ] Lane 9. Omit the artifact provider. Save `artifacts/query-store/QS-03/lane-9/unconfigured.json`. Pass when summaries continue and artifact status is `NotConfigured`.
- [ ] Lane 10. Race two writers and apply retention expiry. Save `artifacts/query-store/QS-03/lane-10/lifecycle.json`. Pass when writes are idempotent and expired references produce an explicit unavailable result.

**Verify, perf.** Tests alone are not sufficient verification. A PR is verified only when its unit, live, and perf boxes are all checked.

- [ ] Metric. Record sanitizer CPU, peak managed memory, upload count, bytes, artifact reuse, collection time, and request CPU and p95.
- [ ] Probe. Replay identical plan fixtures on baseline and head. Alternate unchanged and new large plans, including incompressible content. Measure the complete consumer retrieval separately.
- [ ] Baseline. Record baseline logging cost and its truncation behavior. Record the head's added storage cost without calling incomplete baseline output an equivalent artifact.
- [ ] Rule. Apply Appendix E's absolute byte and time budgets plus the 5% workload-regression gate. Fail any published artifact that is truncated, fails checksum verification, or cannot be parsed.

**Review gate.** None. QS-03 adds background artifact persistence. Privacy and storage-access approval remain mandatory before enablement.

**Merge.**

- [ ] Attach retrieval, sanitization, access-control, failure, and cost receipts at the reviewed head. Obtain the operator's merge.

## Correlate plans with statistics evidence (QS-04)

**Depends on.** QS-03.

**Files.**

- [ ] Edit the SQL diagnostics reader, statistics model, plan parser, and existing unit and integration tests.
- [ ] Update `docs/QueryStorePerformanceDiagnostics.md` with compile-time and collection-time field meanings.

**Build.**

- [ ] Extract `OptimizerStatsUsage` from sanitized plan XML where the engine supplies it. Preserve its per-statistic compile-time update, sampling, and modification metadata.
- [ ] Resolve referenced statistics through fixed parameterized catalog queries. Keep database, object, and statistics identity with the captured names.
- [ ] When the plan supplies names without historical object IDs, mark the historical identity as unverified. Preserve the current resolution without claiming it proves the same object existed at compilation.
- [ ] Collect current `sys.dm_db_stats_properties` values for those references. Distinguish the current snapshot from the values recorded during compilation.
- [ ] Include plan compilation and observed execution times where available. Do not manufacture a general statistics `LastUsed` field or substitute index-usage timestamps.
- [ ] Preserve statistics references that are missing, dropped, recreated, inaccessible, or unsupported with an explicit status. Do not silently attach a same-named replacement.
- [ ] Keep the database-wide health list optional and bounded. Do not let unrelated high-churn small tables displace statistics needed to explain selected plans.
- [ ] Never export histogram boundaries. Treat old age, low sampling, and modifications as evidence, not proof that updating statistics fixes a regression.

**You see.**

- [ ] Save one joined case showing the plan's compile-time statistics metadata beside current metadata, with an explicit status for every reference.

**Verify, unit.** Tests alone are not sufficient verification. A PR is verified only when its unit, live, and perf boxes are all checked.

- [ ] Test namespace variants, absent optimizer metadata, quoted identifiers, stable reference matching, recreated statistics, null properties, and partial budgets. Run targeted diagnostics unit tests.

**Verify, live.** Tests alone are not sufficient verification. A PR is verified only when its unit, live, and perf boxes are all checked. Ten lanes on the configured `swarm workers` model at the PR head when agent execution is approved.

- [ ] Lane 1. Regression lane against trunk. Compare existing health output on baseline and head, then inspect the new joined evidence. Save `artifacts/query-store/QS-04/lane-1/regression.json`. Pass when existing fields retain their meanings.
- [ ] Lane 2. Compile and capture a query, then modify its table. Save `artifacts/query-store/QS-04/lane-2/modifications.json`. Pass when compile-time and current counters remain distinguishable.
- [ ] Lane 3. Preserve a captured plan, then update its referenced statistics. Save `artifacts/query-store/QS-04/lane-3/updated.json`. Pass when the report shows both versions without claiming the historical plan consulted the new statistics.
- [ ] Lane 4. Recompile after that update. Save `artifacts/query-store/QS-04/lane-4/recompiled.json`. Pass when newly captured evidence identifies its compilation context.
- [ ] Lane 5. Capture a plan without optimizer statistics usage. Save `artifacts/query-store/QS-04/lane-5/unsupported.json`. Pass when absence is explicit rather than an empty success.
- [ ] Lane 6. Drop and recreate a referenced statistic. Save `artifacts/query-store/QS-04/lane-6/recreated.json`. Pass when the collector does not assert an unproven identity match.
- [ ] Lane 7. Use filtered, automatic, user-created, and index statistics. Save `artifacts/query-store/QS-04/lane-7/kinds.json`. Pass when each reference retains the appropriate metadata.
- [ ] Lane 8. Deny metadata access or expose null properties. Save `artifacts/query-store/QS-04/lane-8/missing.json`. Pass when statuses preserve the difference from a healthy statistic.
- [ ] Lane 9. Exercise unusual quoted identifiers and database boundaries. Save `artifacts/query-store/QS-04/lane-9/identity.json`. Pass when parameterized resolution cannot cross the intended scope.
- [ ] Lane 10. Exceed the referenced-statistics budget. Save `artifacts/query-store/QS-04/lane-10/budget.json`. Pass when partial coverage is explicit and the deadline holds.

**Verify, perf.** Tests alone are not sufficient verification. A PR is verified only when its unit, live, and perf boxes are all checked.

- [ ] Metric. Record catalog reads, SQL CPU, XML parsing CPU, allocation peaks, emitted bytes, and end-to-end request CPU and p95.
- [ ] Probe. Interleave baseline and head with equal selected plans on small and large statistics catalogs. Exercise shared statistics references across plans.
- [ ] Baseline. Record QS-03 cost before statistics enrichment and existing database-wide health collection separately.
- [ ] Rule. Keep referenced-statistics retrieval within Appendix E's caps and the total collection budget. Apply the 5% workload-regression gate and fail duplicate metadata reads per identical reference without justification.

**Review gate.** None. QS-04 adds correlated background evidence, not an operator interaction.

**Merge.**

- [ ] Attach the compile-update-recompile case, identity edge cases, actual metadata-query plans, and all verification receipts. Obtain the operator's merge.

## Verify SRE consumption and stage enablement (QS-05)

**Depends on.** QS-04 and an approved deployment-specific artifact provider.

**Files.**

- [ ] Update `docs/QueryStorePerformanceDiagnostics.md` and add consumer examples beside it.
- [ ] Add OSS contract tests for the emitted summaries, manifests, and missing-data states.
- [ ] Inspect the PaaS repository before naming its implementation files. Assign its owner for log routing, storage policy, read authorization, retention, and the read-only SRE integration.

**Build.**

- [ ] Give the SRE consumer a versioned read contract for ranked windows, complete plans, comparison plans, and correlated statistics.
- [ ] Join only matching database scope, capture generation, query, plan, interval, and artifact versions. Display uncertainty and partial coverage.
- [ ] Verify that authorized retrieval reconstructs the intended evidence without a new inbound database principal.
- [ ] Keep automated remediation, plan forcing, index changes, and statistics updates out of scope. Return diagnostic findings with supporting evidence.
- [ ] Pilot on synthetic data, then an approved test ring. Publish volume, CPU, capture coverage, missing-plan counts, and artifact failures before widening enablement.
- [ ] Document rollback by disabling the new mode and artifact publication through the actual configuration lifecycle. Do not delete retained evidence as part of rollback.

**You see.**

- [ ] Save an SRE response that identifies a costly query, its contributing plans, the comparison window, and relevant statistics evidence. Include artifact references and uncertainty.

**Verify, unit.** Tests alone are not sufficient verification. A PR is verified only when its unit, live, and perf boxes are all checked.

- [ ] Test consumer handling of versioned records, missing plans, expired artifacts, incomplete collections, and repeated interval revisions. Run the owning projects' targeted contract tests.

**Verify, live.** Tests alone are not sufficient verification. A PR is verified only when its unit, live, and perf boxes are all checked. Ten lanes on the configured `swarm workers` model at the PR head when agent execution is approved.

- [ ] Lane 1. Regression lane against trunk. Replay the same incident fixture through the baseline and new consumer. Save `artifacts/query-store/QS-05/lane-1/regression.json`. Pass when existing investigation data remains usable and the additional evidence is retrievable.
- [ ] Lane 2. Analyze a frequent CPU-heavy query. Save `artifacts/query-store/QS-05/lane-2/cpu.json`. Pass when the response cites total cost and execution count rather than only latency.
- [ ] Lane 3. Analyze a plan regression. Save `artifacts/query-store/QS-05/lane-3/plans.json`. Pass when current and comparison plans are distinguished with their evidence.
- [ ] Lane 4. Analyze changed statistics. Save `artifacts/query-store/QS-05/lane-4/statistics.json`. Pass when the consumer distinguishes compile-time and current metadata without asserting causality.
- [ ] Lane 5. Remove one plan artifact. Save `artifacts/query-store/QS-05/lane-5/missing.json`. Pass when the consumer reports missing evidence rather than inventing plan details.
- [ ] Lane 6. Supply incomplete and repeated intervals. Save `artifacts/query-store/QS-05/lane-6/intervals.json`. Pass when totals are not doubled and gaps remain visible.
- [ ] Lane 7. Attempt cross-deployment artifact access. Save `artifacts/query-store/QS-05/lane-7/isolation.json`. Pass when authorization denies the request and records the attempt.
- [ ] Lane 8. Replay a database restore or capture-generation change. Save `artifacts/query-store/QS-05/lane-8/generation.json`. Pass when reused numeric IDs cannot join unrelated evidence.
- [ ] Lane 9. Disable the new collection mode. Save `artifacts/query-store/QS-05/lane-9/rollback.json`. Pass when collection stops under the documented restart semantics and retained artifacts remain readable.
- [ ] Lane 10. Request an unsupported remediation. Save `artifacts/query-store/QS-05/lane-10/read-only.json`. Pass when the consumer performs no SQL mutation or deployment action.

**Verify, perf.** Tests alone are not sufficient verification. A PR is verified only when its unit, live, and perf boxes are all checked.

- [ ] Metric. Measure total deployment SQL and host CPU, request p95, evidence freshness, ingestion volume, retained bytes, and consumer retrieval latency.
- [ ] Probe. Alternate disabled and enabled runs of the fixed successful FHIR workload. Exercise cold and repeated artifact retrieval with the same access policy.
- [ ] Baseline. Record disabled-collector deployment cost and current investigation retrieval behavior before the pilot.
- [ ] Rule. Apply the approved 5% workload-regression limit, total collection budgets, and accepted retention cost. Require publication within 60 seconds of a successful collection start and test artifact retrieval within 30 seconds.

**Review gate.** The operator accepts the consumer workflow and rollout evidence before merge or enablement.

- [ ] Post synthetic-data input and output with the source manifest and artifact receipts. Obtain recorded operator acceptance.
- [ ] If the consumer has a UI, include a screenshot and a short video. For a headless consumer, supply the equivalent command transcript and JSON instead.
- [ ] Obtain privacy, access, retention, and test-ring approval. Keep production enablement a separate explicit deployment approval.

**Merge.**

- [ ] Attach the OSS and PaaS owner receipts, consumer review, and pilot results. Obtain the operator's merge. Do not equate merge with permission to enable production collection.

## Close the program

- [ ] Verify every completed box has its evidence and exact revision. Leave blocked work visibly unchecked.
- [ ] Update the specification and accepted ADRs to match shipped behavior. Keep this plan's unmeasured proposals distinct from accepted limits.
- [ ] Record the rollback command, configuration lifecycle, artifact retention policy, and operating owner.
- [ ] Report which changes merged, which deployments are enabled with approval, and which rollout gates remain blocked.

## Appendix A. Prototype evidence

Planning date is October 23, 2026.
The reviewed branch is `personal/mikaelw/query-store-performance-diagnostics` at `1ae6d8d634bfe63d362ce92b89d8c85bb3b3e6ba`.
The earlier review pinned `main` at `d4f26b2177fe54a5a8b060ad677a8597bd09451e`.
Refresh and record the real integration baseline when execution starts.

The current code already schedules hourly by default, emits compiled plans, and reads statistics metadata.
The earlier review ran 93 targeted diagnostics unit tests successfully.
It also reproduced unsupported timer periods and a 42,433-byte statistics payload through actual production code.
Those checks establish existing behavior and defects. They do not prove the new design or its CPU cost.

No new implementation prototype or representative SQL benchmark ran for this plan.
Live SQL and the deployment-specific artifact integration remain unproven.
Before QS-01 implementation, run isolated spikes for closed-interval revision handling and internal-marker preservation.
Before QS-03 implementation, prove complete sanitized XML upload and authorized retrieval with the intended storage provider.
Before QS-04 implementation, capture a compile-update-recompile fixture on the supported SQL engine versions.
Save each spike's branch, SHA, commands, and artifacts with its change. Stop that change if its required evidence cannot be produced.

## Appendix B. Alternatives rejected

- A slow-duration threshold alone misses frequent fast workload cost. Keep it for legacy mode, not the new hourly rankings.
- One CPU-only ranking misses read pressure, waits, and regressions. Use a bounded union instead.
- Full XML on every hourly log line repeats large content. Prefer small summaries and immutable artifact references.
- Hard-cut plan XML is not a complete machine-readable artifact. Reject an oversize plan explicitly rather than truncating it.
- Actual execution-plan capture changes collection overhead and disclosure. Keep compiled Query Store plans in this program.
- A generic statistics last-used timestamp misrepresents compile-time optimizer behavior. Preserve compile-time evidence and current metadata separately.
- Index-usage timestamps do not identify statistics consulted during optimization. Do not substitute them.
- Raw statistics histograms can disclose indexed values. They are not needed for this scope.
- Native `query_hash`, parameter HASH comments, and FHIR shape annotations are not unique plan identities.
- Direct SRE database access adds an inbound data-plane path. Keep collection inside the service and authorize artifact reads separately.
- The existing resource-export writer buffers export-job data and commits synchronously. Do not reuse it without proving cancellation and ownership fit.
- Caching, negative caching, replica-routing changes, and unconditional text-fallback removal do not belong in this observability program without their own measurements.

## Appendix C. Risks

| Risk | Change | Required response |
| --- | --- | --- |
| Large Query Store catalogs make a small top-N expensive to compute. | QS-01, QS-02 | Measure examined work, actual plans, SQL CPU, and cancellation. Output row caps are not cost caps. |
| Closed intervals receive delayed data or repeated reads. | QS-01 | Use stable interval keys and replacement revisions. Never sum repeated snapshots. |
| A lease permits overlapping collection during failure. | QS-01, QS-03 | Make records and artifact writes idempotent. Do not promise exactly-once collection. |
| Supported engines expose different Query Store and optimizer metadata. | QS-01, QS-04 | Feature-detect fields and emit explicit unsupported states. Verify actual replica-capture configuration. |
| Literal query text and plan constants remain sensitive after parameter metadata removal. | QS-03, QS-05 | Obtain an approved disclosure policy. Apply least-privilege retrieval, retention, and access audit. Never label artifacts PHI-free. |
| A plan is recompiled or statistics are dropped while collection runs. | QS-03, QS-04 | Save content and observation times. Report unverifiable identity and avoid causal claims. |
| Artifact expiry, failed uploads, or unsupported metadata leave gaps. | QS-03, QS-05 | Preserve summaries with explicit missing-evidence statuses. Never return a success-shaped substitute. |
| Log or storage volume grows across databases and deployments. | QS-03, QS-05 | Measure bytes per database-hour and fleet totals before widening rollout. |
| The PaaS repository and approved live SQL environment are not available in this checkout. | QS-03, QS-05 | Assign owners and block the relevant verification gates. Do not invent file paths or pilot results. |
| The current default selection and log consumers depend on existing fields. | All | Use an opt-in mode and a versioned schema. Preserve old field meanings during migration. |

## Appendix D. Links and reading list

Read the baseline and architecture decisions before implementation.

- [Query Store diagnostics baseline](QueryStorePerformanceDiagnostics.md).
- [ADR-2608](arch/adr-2608-query-store-performance-diagnostics.md).
- [ADR-2602](arch/adr-2602-database-logging.md).
- [ADR-2605](arch/adr-2605-metric-emission-rate-limiting.md).
- [Query Store usage scenarios](https://learn.microsoft.com/sql/relational-databases/performance/query-store-usage-scenarios).
- [How Query Store collects data](https://learn.microsoft.com/sql/relational-databases/performance/how-query-store-collects-data).
- [Query Store runtime statistics](https://learn.microsoft.com/sql/relational-databases/system-catalog-views/sys-query-store-runtime-stats-transact-sql).
- [Statistics properties](https://learn.microsoft.com/sql/relational-databases/system-dynamic-management-objects/sys-dm-db-stats-properties-transact-sql).
- [Optimizer statistics](https://learn.microsoft.com/sql/relational-databases/statistics/statistics).
- [Query Store for secondary replicas](https://learn.microsoft.com/sql/relational-databases/performance/query-store-for-secondary-replicas).

Read `skills/how/SKILL.md` when grounding QS-01, the QS-03 storage boundary, and QS-05 consumer ownership.
Use `skills/interrogate/SKILL.md` if the artifact or identity design remains contested.
Record execution decisions under `skills/show-me-your-work/SKILL.md`.
These paths refer to the installed plugin, not files promised by this repository.

Use the SDK in `global.json`.
Build before adding tests. Restore only after a dependency change or a missing-dependency failure.
The commands below describe the reviewed branch's Microsoft.Testing.Platform runner.
After integrating `main`, inspect the test executable's help and record the equivalent targeted filter if the runner changed.

```bash
dotnet build src/Microsoft.Health.Fhir.SqlServer/Microsoft.Health.Fhir.SqlServer.csproj --no-restore --nologo --verbosity quiet
dotnet build src/Microsoft.Health.Fhir.SqlServer.UnitTests/Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-restore --nologo --verbosity quiet
dotnet test --project src/Microsoft.Health.Fhir.SqlServer.UnitTests/Microsoft.Health.Fhir.SqlServer.UnitTests.csproj --no-build -- --filter 'FullyQualifiedName~Features.Watchdogs.QueryStoreDiagnostics' --minimum-expected-tests 1 --no-ansi --no-progress
```

Use `test/Microsoft.Health.Fhir.R4.Tests.Integration/Microsoft.Health.Fhir.R4.Tests.Integration.csproj` with the existing SQL fixture.
Prepare its documented connection configuration in the isolated test environment.
Build the project, inspect its test-runner help, and run the `QueryStoreDiagnosticsWatchdogTests` class with the supported class filter.
Record the exact command and result. Do not reuse a filter from another runner without checking it.
Add Azure-writer and consumer tests only in the projects that own those changes.

## Appendix E. Collection contract and proposed limits

The following limits are proposals for the isolated pilot, not measurements or approved production defaults.
Before implementation, record accepted values and the reason for any change.
Do not weaken a failed gate after seeing a candidate result without operator approval.

| Item | Proposed pilot rule |
| --- | --- |
| Schedule | Collect every 3,600 seconds. Name the actual closed Query Store intervals rather than claiming exact wall-clock hours when buckets differ. |
| Ranking | Select 10 queries per ranking and at most 50 distinct queries. Retain at most 100 plan contributions with explicit partial-coverage flags. |
| Regression signal | Compare the latest completed hour with the preceding six comparable hours. Require 20 executions in each window and at least 2x average CPU or duration. Include the absolute cost difference and label a candidate, not a proven cause. |
| Collection deadline | Cancel the complete cycle after 30 seconds. Give each diagnostic SQL command at most five seconds within that deadline. Disable automatic SQL retries for the initial pilot. |
| Collection SQL CPU | Block pilot expansion above 10 SQL CPU-seconds per database per hourly cycle in the representative workload. Record cancellations and incomplete work. |
| Plan input | Reject raw XML above 4 MiB through bounded reading. Do not first buffer an unbounded value and then check its size. |
| Plan persistence | Publish at most 25 new or changed artifacts and 10 MiB of uncompressed sanitized content per database per cycle. Report plans omitted by the budget. |
| Referenced statistics | Fetch each distinct reference once. Cap enriched references at 200 per cycle and report omitted references. |
| Log records | Enforce a 24 KiB serialized payload target and verify the complete record against the actual sink limit. Include multibyte text and envelope overhead. |
| Retention | Keep synthetic pilot artifacts for seven days. Require deployment-owner approval for production retention and access policy. |
| Publication | Finish a successful collection and its references within 60 seconds of its start. Keep the stricter collection deadline. |
| Consumer retrieval | Retrieve and verify the pilot evidence package within 30 seconds. Report expired or unavailable artifacts explicitly. |

Every record needs a schema version, database scope, collection ID, source interval IDs and boundaries, observation time, and section status.
Define database scope and capture generation across restores, imports, and Query Store resets before QS-01 ships.
Do not guess a stable generation from a numeric query or plan ID.
If generation cannot be established, prevent cross-generation joins and report that limitation.

Use a deterministic logical record key for each database scope, generation, interval, query, plan, and execution outcome.
Keep collection attempts separate from that key.
When captured values change, emit a replacement revision and have consumers choose the latest observation instead of summing attempts.
Use bounded replay to repair missed or delayed intervals. Report gaps outside the accepted replay window.
Define the replay window during the QS-01 spike and include it in the approved configuration.

Retain query-level totals across plans and the selected plan-level contributions.
Record each ranking's reason, metric, rank, source window, baseline window, and sample counts.
Preserve `Unknown`, `Unsupported`, `Unavailable`, `BudgetExceeded`, and `Failed` states rather than turning missing values into zero.
Expose compiled plans as compiled plans. Do not imply actual per-execution row counts or a universal statistics usage clock.

For the performance gate, seed equal Query Store and statistics catalogs on the same SQL engine, compatibility level, hardware, capture settings, and replica configuration.
Pin both revisions. Keep legacy lookup configuration identical unless its replacement is the explicit experiment.
Warm both sides, then alternate at least five baseline/head pairs.
Run each pair for the same successful request mix and duration, with enough completed requests to compare p95.
Record errors, timeouts, lookup rate, cancellations, collector invocations, and catalog growth.
Reject comparisons where a lower completed workload or missing diagnostics explains lower CPU.

Report SQL CPU-seconds by node and total host CPU-seconds per successful request.
Do not add CPU percentages from differently sized nodes.
Use independent node-level accounting and appropriate execution instrumentation so cancelled work does not disappear.
Do not rely on only successful Query Store rows or evictable cached-plan statistics.
Report measurement variability and block a reproducible increase above 5% in request CPU or p95.
If the baseline lacks collection, gate the candidate's added work with the absolute budgets above. Do not invent a relative collection speedup.

The remaining product decisions are production retention and destination, approved disclosure fields, artifact-reader identities, acceptable evidence freshness, and final cost limits.
Obtain those decisions before the affected change runs against non-synthetic data.
The plan does not authorize automatic remediation or removal of the existing slow-query lookup.
