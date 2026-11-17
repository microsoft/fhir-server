# ADR-2611: Tiered PR validation to keep the required check under 20 minutes

**Status**: Proposed
**Date**: 2026-11-16
**Feature**: CI/PR pipeline speedup

## Context

The required GitHub check for `main` is the Azure DevOps "PR Build & Deploy" run (definition 9, [build/pr-pipeline.yml](../../build/pr-pipeline.yml)); the only other required check is `license/cla`. Its median successful duration is 82.5 minutes (p90 93), and 31 of 292 PR builds in October–November 2026 needed a manual stage rerun to pass despite `--retry-failed-tests 3`.

![Today's PR critical path](../images/ci-pipeline/timeline-before.svg)

The critical path is structural:

- **Per-PR Azure provisioning.** Every PR provisions a resource group, NSP, Key Vault, AAD test applications, two SQL servers, an ACA environment, five container apps and Cosmos. This takes about 29 minutes before any E2E test starts. The AAD setup job's median is 10.6 minutes; in build 53317, module preparation took about 53 seconds and `Add-AadTestAuthEnvironment` about 9 minutes 39 seconds.
- **Serial E2E against one deployed server.** The R4 SQL suite's 2,287 tests take 38.7 minutes (`parallelizeTestCollections: false`).
- **Integration tests on Azure SQL.** SQL integration jobs take about 32 minutes, dominated by `SqlServerSqlCompatibilityTests` (30.5 minutes). They also produce the most intermittent failures.
- **Repeated work.** Unit tests run sequentially, twice, on Windows. The solution compiles about eight times per PR. Coverage aggregation adds a 5.6-minute tail.

Every PR runs the same matrix: Stu3, R4 and R5 on SQL, plus Stu3 and R4 on Cosmos. No defect in the observed window was caught only by a Stu3, R5 or Cosmos suite, but the window is short. This ADR therefore decides *where and when* each suite runs, not whether it runs. Version divergence lives mostly in 206 `#if` directives in shared code. Cosmos is the store behind Azure API for FHIR, and parts of Cosmos query generation live in Core. Post-merge CI (definition 27) runs as batched CI on `main` only, against one shared environment.

### Evidence from local spikes (November 2026)

The spikes ran on a 16-CPU Windows host with Docker Desktop, not yet on Linux CI agents.

- **SQL Server 2025 container** (required by the schema's `vector(1536)` type). It is ready in about 27 seconds, and the schema deploys cleanly.
  - **SQL integration:** 804 tests take 7 minutes wall, versus about 32 minutes on Azure.
  - **Schema compatibility:** `SqlServerSqlCompatibilityTests` takes 4.4 minutes in one process. Its methods each create their own database, so split across 4 processes it takes 1.4 minutes.
  - **Container-only failures:** three. They come from row ordering, how a killed connection's error is classified, and dropping a database that's still in use. Each has a small test fix.
- **R4 SQL E2E in-proc.** It takes about 40 minutes in one process, no faster than deployed. **Split into 4 isolated shards (one database each) it took 12.1 minutes wall, with no cross-shard failures.**
  - In-proc runs 81 tests that the deployed run skips, and skips 56 that it runs, mostly `ConvertDataTests` and the ACR-dependent tests.
  - 18 in-proc-only failures repeat on rerun. Some are tests already broken on main that the deployed run never executes. The others come from the development identity provider (403 rather than 401), include limits, and an import fixture.
- **Legacy Linux Cosmos emulator: not viable.**
  - **#5265 settings** (2 partitions, 3 GB) failed 251 of 505 tests within 66 seconds, from partition exhaustion.
  - **Larger settings crash** (25 partitions, 8 GB, 4 CPUs). The emulator crashed in 7 of 8 Gateway runs with a resource error in its HTTP stack ("errno 11"). A standalone stress test reproduces the crash with 32 concurrent queries.
  - **Direct mode** fails the suite with "410 Gone" errors.
  - **The image is an evaluation build with an expiry date.**
  - **The newer vNext emulator** lacks stored procedures, and the server requires four.
- **Shard filters.** In this repository, a `--filter-query` that combines a path with a trait selects zero tests. Shards are selected with `--filter-method <Namespace.Class>.*` plus `--filter-trait`, which was verified complete against `--list-tests`.

### Prior attempt: #5265

[#5265](https://github.com/microsoft/fhir-server/pull/5265) (closed unmerged) moved R4B and R5 to Docker SQL Server 2022, the legacy Cosmos emulator and Azurite. Its Cosmos jobs failed, and the spike above reproduced the cause. It also used weak readiness checks, ran every suite in one serial job, and removed the deployed stages with no fallback. This ADR keeps SQL on containers, where the spike shows they work, and keeps Cosmos on a real account.

## Options Considered

1. **Optimize the current shape**: parallel unit tests, scanners from build output, coverage off the required run. *(Kept as the first step, but rejected as the end state. Provisioning plus serial deployed E2E keeps the PR at about 75 minutes.)*
2. **Warm pool of pre-provisioned environments leased per PR.** *(Rejected. Server-global search-parameter and reindex state needs a full reset on every lease, and idle environments cost money.)*
3. **Everything in containers, including the Cosmos emulator.** *(Rejected. The legacy emulator crashes under the suite, and vNext lacks stored procedures.)*
4. **SQL and storage in containers, Cosmos on a shared pre-provisioned account, no per-PR deployment.** *(Chosen.)*
5. **Path-triggered Azure deployment on PRs.** *(Rejected. Replaying the last 255 merges, 59% would have triggered it.)*

## Decision

We will split validation into tiers. **Ordinary PRs deploy no Azure environment.** SQL and storage run in containers on the agent, and Cosmos tests use one shared, pre-provisioned Cosmos account. Real deployments (ACA, AAD test applications, Key Vault, Azure SQL) run on explicit request, post-merge and nightly. Nothing that runs on PRs today stops running; only its trigger and hosting change.

![Where each check runs](../images/ci-pipeline/tiers.svg)

**Tier 1 — every PR, no deployment, target p50 under 20 minutes.**

- **Unit tests** for every FHIR version in parallel; net10 as a separate parallel job.
- **SQL integration tests** for every version on a SQL Server 2025 container, including schema compatibility split by method.
- **Stu3, R4 and R5 SQL E2E, full suites, in-proc** on SQL Server 2025 and Azurite containers, sharded. These run in parallel, so the extra versions cost agents rather than minutes.
- **R4 Cosmos integration and E2E, in-proc,** against the shared Cosmos account.
- **One R4 amd64 image**, with an authenticated container smoke test.
- **Source scanners**, including CredScan.

Full Guardian and coverage aggregation (Codecov) move to a separate, non-required pipeline. Fork PRs, which receive no secrets, run everything except the Cosmos jobs.

![Target PR gate](../images/ci-pipeline/timeline-after.svg)

**Sharding and isolation.** Each shard runs on its own agent, with its own test process, its own database (unique `Initial Catalog`) and its own SQL Server and Azurite containers. Azurite runs on host port 10000, because three test helpers hard-code `127.0.0.1:10000`. Collections stay serial within a shard. Shards come from a checked-in class-to-shard manifest built from measured durations. The last shard runs everything not listed, so new tests are always scheduled. Classes longer than the shard budget (`ImportTests`, `ReindexTests`, `SqlServerSqlCompatibilityTests`) are split by method.

**Cosmos on the shared account.**

- **Isolation.** Each run creates uniquely named containers and deletes them at the end. A scheduled sweeper removes leaked containers.
- **Authentication.** The account keeps `disableLocalAuth = true`. In-proc hosting and the integration fixtures authenticate with the pipeline's Entra identity.
- **Container creation goes through ARM, so it is serialized per account.** Run concurrency and throughput are sized from measurement. If ARM throttling appears, runs spread across two or three accounts.
- **Fixture configuration.** The Cosmos test fixtures must honor the configured connection mode; today they construct their configuration without it.

**Tier 2 — real deployment on a PR, on request.** Today's deployed stages run inside the same required run, in addition to Tier 1, when any of the following applies:

- The PR has the `full-matrix` label. Applying labels requires triage permission.
- The run was queued manually with the `fullDeployment` parameter.
- The PR targets a `release/*` or `hotfix/*` branch, or comes from a same-repository branch with those names.

The label is expected (not enforced) for changes to `samples/templates/**`, authentication registration, ACA or Key Vault configuration, or Cosmos client and collection setup. This is the canonical list.

The classifier **fails closed**: if labels cannot be read, the run fails rather than assuming "no label". Fork PRs need a maintainer-controlled path, because a label does not grant secrets. On the deployed target, tests that assert server-wide counts (`HistoryTests`) run in their own job after the reindex and bulk-update jobs, which write to the same server.

**One test definition, two targets.** E2E targets a deployed server when `TestEnvironmentUrl*` is set and runs in-proc otherwise. One test-job template with a `container | azure` target is shared by the PR pipeline and post-merge CI; only setup and concurrency differ. On the container target, the template must not emit `TestEnvironmentUrl*` values at all, because ADO passes undefined variables through as literal text. In-proc configuration (`launchSettings.json`) must be available next to the test binaries.

**Tier 3 — post-merge and nightly.** Definition 27 keeps batched CI with the full deployed matrix: real AAD, managed identity, Key Vault, multi-replica reindex, Azure SQL, multi-arch images and full Guardian. It also re-enables the Cosmos `ReindexOperation` jobs that today run only on PRs.

- **When a batch fails,** the on-call engineer runs a bisect script. It queues definition 9 with full deployment for each merge SHA and the last green baseline.
- **Blame and ownership.** A culprit requires reproducing both the failure and a passing baseline, and infrastructure failures are never blamed on a PR. The merging PR's author owns the fix. Applied schema migrations are forward-fixed.
- **Automation.** Bisect is automated only if manual use proves frequent.
- **Nightly:** ExportLongRunning, large import performance tests, schema compatibility across the full version range (needs the `Max−5..Max` window extended), CodeQL with a full build, R4B, and net10 E2E.

**Cutover gates.** Tier 1 replaces today's gate only when all of the following hold:

- **Parity.** The 18 in-proc-only failures are fixed or tracked. Every test skipped in-proc is covered by Tier 2 or Tier 3: ConvertData and ACR anonymization are known skips, and OpenIddict replaces AAD.
- **Shadow period.** Tier 1 has run as a non-required pipeline on all PRs on Linux agents. Its first-attempt pass rate is at least today's, its rerun rate is below 3%, and its measured p50 is under 20 minutes, including startup and queueing.
- **Cosmos on the shared account.** Throughput and concurrency are measured under real PR load.
- **Bisect.** The bisect runbook has been exercised once.

## Consequences

- **Faster PRs.** Ordinary PRs target a p50 under 20 minutes, down from 82. Moving SQL to containers alone leaves deployed Cosmos on the critical path, at about 60 minutes. Moving Cosmos to the shared account removes per-PR provisioning entirely. Under 20 minutes also requires test jobs not to wait for a 17-minute shared build stage.
- **Far fewer Azure resources.** PRs stop creating resource groups, AAD applications, Key Vaults, SQL servers, ACA environments and per-PR Cosmos accounts. One shared Cosmos account remains, with per-run containers and a sweeper.
- **Less real-deployment coverage before merge.** Real AAD, managed identity, Key Vault, ACA configuration, multi-replica cache sync and Azure SQL behavior no longer run on PRs unless Tier 2 is requested. Post-merge CI catches them, so `main` can be red for a batch.
- **Stronger version coverage.** Stu3 and R5 SQL E2E and all integration tests still run on every PR, in parallel. In-proc also runs 81 tests that the deployed run skips today.
- **SQL container differences.** SQL authentication replaces Entra workload identity, and Azure SQL resource governance and Azure-only options are not reproduced. Tier 3 covers them.
- **New infrastructure and test work.** Packaged in-proc configuration, the container-target template, the shard manifest and its update script, Cosmos test authentication and container cleanup, the fail-closed classifier, the bisect runbook, three container-specific integration-test fixes and the 18 in-proc failures. More agents run per PR: roughly 15–20 instead of about 25 jobs that mostly wait on provisioning.
- **New failure modes.** Container start-up and host contention, and ARM throttling on the shared Cosmos account. Both are measured in the shadow period; retries are not raised to compensate.
- **Other ADRs.** This ADR changes CI policy only. It does not change product behavior or the FHIR contract, and it supersedes no existing ADR.
