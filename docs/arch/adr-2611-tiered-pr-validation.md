# ADR-2611: Tiered PR validation to keep the required check under 20 minutes

**Status**: Proposed
**Date**: 2026-11-16
**Feature**: CI/PR pipeline speedup

## Context

The required GitHub check for `main` is the Azure DevOps "PR Build & Deploy" run (definition 9, [build/pr-pipeline.yml](../../build/pr-pipeline.yml)); the only other required check is `license/cla`. Its median successful duration is 82.5 minutes (p90 93), and 31 of 292 PR builds in October–November 2026 needed a manual stage rerun to pass despite `--retry-failed-tests 3`.

![Today's PR critical path](../images/ci-pipeline/timeline-before.svg)

The critical path is structural rather than incidental:

- **Per-PR Azure provisioning.** Every PR provisions its own environment: resource group, NSP, Key Vault, AAD test applications, two SQL servers, an ACA environment, five container apps and Cosmos. This takes about 29 minutes before any E2E test starts. The AAD setup job alone spends 10.6 minutes, largely reinstalling Graph and Az modules.
- **Serial E2E.** E2E tests run serially (`parallelizeTestCollections: false` in `test/Microsoft.Health.Fhir.Shared.Tests.E2E/xunit.runner.json`). The R4 SQL suite's 2,287 tests take 38.7 minutes, about their summed test duration.
- **Slow SQL integration.** SQL integration jobs (~32 minutes) are dominated by `SqlServerSqlCompatibilityTests`, which creates Azure SQL databases for each of its 13 tests.
- **Repeated work.** Unit-test projects run sequentially, twice, on Windows. The solution is compiled about eight times per PR. Coverage aggregation adds a 5.6-minute tail to the required run.

Every PR runs the same matrix: Stu3, R4 and R5 on SQL, plus Stu3 and R4 on Cosmos. In the observed window, failures that appeared on only one FHIR version were spread across versions and cleared on rerun. No defect was seen that only a Stu3, R5 or Cosmos suite caught. The window is short and subject to survivorship bias, so this ADR does not treat the matrix as redundant. Instead it decides *when* each part of the matrix must run.

Version divergence lives mostly in 206 `#if` directives in shared code, in per-version search-parameter definitions, and in per-version Firely SDK packages. Cosmos is the store behind Azure API for FHIR, and parts of Cosmos query generation live in Core. Post-merge CI (definition 27, [build/ci-pipeline.yml](../../build/ci-pipeline.yml)) runs as batched CI against one shared environment. A failure there cannot be traced to a single merge today.

## Options Considered

1. **Optimize the current shape (deployed E2E on every PR)**: cache modules, parallelize unit tests, shard E2E against the per-PR deployment, and start scanners earlier. *(Rejected as the end state. Per-PR provisioning keeps the floor at ~45–50 minutes. Kept as Phase 1 because it changes no coverage.)*
2. **Warm pool of pre-provisioned environments leased per PR**: removes provisioning time. *(Rejected. Search parameters and reindex state are server-global, so each lease needs a full reset. Idle environments cost money, and tests stay serial within an environment.)*
3. **Tiered validation**: PRs run only against local containers (SQL Server, Cosmos emulator, Azurite) and never provision Azure. Real Azure SQL and Cosmos run post-merge, on request, and nightly. *(Chosen.)*
4. **Tiered validation with path-triggered Azure deployment on PRs**: like option 3, but risky paths deploy today's Azure matrix on the PR. *(Rejected. Replaying the last 255 merges on `main` against the candidate path list, 59% would have triggered it (56% excluding Dependabot). Package bumps, auth/SMART, `build/**` and search-parameter changes are frequent, so most PRs would stay slow.)*

## Decision

We will split validation into tiers. **PRs never provision Azure resources.** Real Azure SQL and Cosmos run post-merge, nightly, or on explicit request. Nothing that runs on PRs today stops running; only its trigger and hosting change.

![Where each check runs](../images/ci-pipeline/tiers.svg)

**Tier 1 — every PR, target under 20 minutes (p50).**

- **Build once.** A single Linux build covers both target frameworks and captures analyzer results. Later jobs reuse its outputs.
- **Unit tests.** All test projects for every FHIR version run in parallel. net10 runs as a separate parallel job.
- **R4 SQL E2E, full suite, in-proc.** Tests run in `InProcTestFhirServer` against a pinned SQL Server 2025 Linux container and Azurite. SQL Server 2025 is required because the schema uses the `vector(1536)` type; it uses no vector indexes or preview features. They are split into disjoint shards by measured class time. Each shard runs collections serially and gets its own database and storage. Classes that touch server-global state (reindex, custom search parameters, server-wide history counts, import, bulk delete, system export) share one serial shard.
- **R4 SQL integration tests** run against the container, excluding schema-compatibility tests.
- **R4 Cosmos integration and E2E** run against a Cosmos emulator container on each agent, described below.
- **Stu3 and R5 version-sensitive subset.** This is an explicit, checked-in manifest of discovered tests covering search, bundle and transaction, conformance, validate, patch and CRUD.
- **Reindex lifecycle check and BulkUpdate tests.**
- **One R4 amd64 image**, built from the published output, with an authenticated container smoke test.
- **Source scanners**, including CredScan, start at time zero.

Full Guardian and coverage aggregation with the Codecov upload move to a separate pipeline triggered by the PR run. That pipeline receives the PR number and head SHA explicitly. The build-once step replaces the Roslyn analyzer rebuild by capturing equivalent analyzer output.

![Target PR gate](../images/ci-pipeline/timeline-after.svg)

**Tier 1+ — wider container coverage, path-triggered on the PR.** A classifier stage at time zero adds container-hosted jobs; Azure is never provisioned. The extra jobs run in parallel with Tier 1, so wall time stays roughly the same.

- **Full Stu3 and R5 SQL E2E suites, in-proc and sharded.** Triggered by `Directory.Packages.props`, `global.json`, Stu3/R4B/R5-specific source, search-parameter definitions and the registry, or reindex code.
- **Full reindex E2E against containers.** Triggered by search-parameter or reindex code.
- **`SqlServerSqlCompatibilityTests` against the SQL container.** Triggered by `src/Microsoft.Health.Fhir.SqlServer/**` (including `Schema/**`) or Core persistence code. In the merge history, that matches about 26% of commits.
- **All four images built, with container smoke tests.** Triggered by the Dockerfile or `build/**`.

**Tier 2 — real Azure on a PR, on request.** When `fullDeployment` is true (see *Modular hosting* below), today's deployed stages run inside the same required run, with real Azure SQL, Cosmos, ACA, AAD, managed identity and Key Vault. The deployed tests run in addition to Tier 1, not instead of it. Running inside "PR Build & Deploy" means the deployed run blocks merging, which a separately triggered pipeline could not.

- **When to use it.** It is expected, but not enforced, for changes to `samples/templates/**`, Cosmos client initialization and collection setup, authentication registration, or ACA/Key Vault configuration. These changed in under 5% of recent merges.
- **Release and hotfix branches** always run Tier 2.

**Modular hosting: one test definition, two targets.**

- **The test code already switches on environment.** `TestFhirServerFactory` targets a deployed server when `TestEnvironmentUrl*` is set and starts `InProcTestFhirServer` otherwise. Integration fixtures use whatever `SqlServer:ConnectionString` provides, and fall back to the product's emulator defaults (`CosmosDbLocalEmulator`) when `CosmosDb__Host` is unset.
- **One test-job template.** It takes a `target: container | azure` parameter, and only the setup steps differ by target. For `container`, setup starts the pinned SQL Server, Cosmos emulator and Azurite containers with readiness probes, and clears remote URLs. For `azure`, it reads Key Vault secrets and deployed URLs. Test selection, sharding, retry and result publishing are identical. The PR pipeline (definition 9) and post-merge CI (definition 27) share this template, so their test definitions stop diverging.
- **One switch, `fullDeployment`.** A classifier job at time zero computes it as true when any of the following holds:
  - The PR has the `full-matrix` label. The job reads it through the GitHub API, and only at run start, so adding the label later requires re-queuing.
    - **Reading the label.** It is one call to `GET /repos/microsoft/fhir-server/issues/{System.PullRequest.PullRequestNumber}/labels`, authenticated through the existing GitHub service connection to avoid rate limits on shared agents. `validate-prs.yml` already gates on PR labels.
    - **Who can request it.** Applying labels needs triage or write permission. Fork and external contributors therefore cannot trigger Azure spend; a maintainer decides.
    - **Re-queuing.** After adding the label mid-review, a `/azp run` comment re-queues the run. Optionally, a small GitHub Action on the `labeled` event does this automatically.
  - The run was queued manually with the `fullDeployment` runtime parameter set. A `/azp run` comment cannot pass parameters.
  - The source is a `release/*` or `hotfix/*` branch, or the run is CI.

  The classifier publishes the switch as an output variable. Provisioning, deployment, deployed-test and Azure cleanup stages are conditioned on it. Stages that depend on optional stages (cleanup, coverage, aggregation) accept `Skipped` as well as `Succeeded`, because ADO otherwise skips them.
- **To verify in Phase 3.** Whether a manually queued run on `refs/pull/<n>/merge` reports its status to the GitHub PR. If it does not, the label is the only on-PR entry point.

**Tier 3 — post-merge and nightly.** Definition 27 keeps batched CI and runs the full deployed matrix: real AAD, managed identity and Key Vault, multi-replica reindex, schema compatibility against Azure SQL, all images multi-arch, and full Guardian. It also re-enables the Cosmos `ReindexOperation` jobs that today run only on PRs. When a batch fails, a failure-triggered bisect replays the immutable merge SHAs in fresh environments. Before assigning a culprit it reproduces both the failure and a passing baseline. It reports an inconclusive result and never blames a PR for an infrastructure failure. The merging PR's author owns the fix. Revert is allowed except for applied schema migrations, which are forward-fixed. Nightly runs cover:

- ExportLongRunning
- Full reindex breadth
- Large import performance tests
- Schema compatibility across the full version range (which needs the compatibility tests' `Max−5..Max` window extended)
- CodeQL with a full build
- net10 E2E

**Cosmos in Tier 1: the legacy Linux emulator container.**

- **Which emulator.** Tier 1 uses `mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator` (the legacy image, pinned by digest), one instance per agent. It is already the repository's devcontainer Cosmos, and the product's `CosmosDbLocalEmulator` defaults target it. Each agent's emulator is private, so there is no shared state, leasing or cross-PR cleanup.
- **Why not vNext.** The newer `vnext` emulator is not used. Its documentation lists stored procedures as "Not planned", and the server relies on four: `hardDelete`, `replaceSingleResource`, `acquireReindexJobs` and `updateUnsupportedSearchParameters`. It is also gateway-only, while the server defaults to Direct mode.
- **What it covers.** The legacy emulator runs the Cosmos query engine and stored procedures. It therefore covers query generation, persistence and stored-procedure behavior, the main risk from Core changes.
- **What it misses.** Managed identity and data-plane RBAC, ARM-driven container setup, throttling and 429 retry behavior, partition splits, and current service query-engine behavior. Changes to Cosmos client initialization, collection setup or retry policies should use the `full-matrix` label. Tier 3 runs real Cosmos on every batch.
- **Known constraints.** It is x64 only, has a slow start-up that needs a readiness probe, degrades beyond about ten containers (a partition count is set and per-fixture container creation is bounded), and has a history of unreliable starts.
- **Stability spike.** Before the emulator becomes required, R4 Cosmos integration and E2E run against it as a non-required job for about two weeks. The spike measures start-up time, start failures, and first-attempt pass rate against the deployed run.
- **Fallback.** If the spike fails, Tier 1 uses one pre-existing shared Cosmos account, or two to three if ARM throttling appears. No account is provisioned per PR, and the account keeps `disableLocalAuth = true`. Each run creates uniquely named containers through ARM, as fixtures do today, deletes them at the end, and relies on a sweeper for leaked containers. Test hosting must then use the pipeline's credential instead of the system-assigned managed identity. A leased, pre-provisioned container pool is a last resort if ARM throttling cannot be contained.

**Cutover gates.** Tier 1 replaces today's default gate only when all of the following hold:

- **Parity.** A per-test-case inventory shows that everything skipped or behaving differently in-proc is covered by Tier 2 or Tier 3. Known differences: ACR anonymization and custom convert skip in-proc, OpenIddict replaces AAD, and import fault-injection assertions activate in-proc.
- **Shadow period.** Tier 1 has run as a non-required shadow pipeline on all PRs. Its first-attempt pass rate is at least today's, and its rerun rate is below 3%.
- **Bisect ready.** Auto-bisect is operational.
- **Cosmos ready.** Either the emulator stability spike has passed, or the per-run-container fallback is in place.
- **Shard validation.** Shard filters are generated as a single `--filter-query` per shard, never mixed with `--filter-class`. They are validated as disjoint and complete, and no shard may run zero tests.

![Phasing](../images/ci-pipeline/phases.svg)

## Consequences

- **Faster PRs.** Every PR without the `full-matrix` label gets a required check of about 19–25 minutes, down from 82, after three phases. Phase 1 brings it to about 45–50 minutes with no coverage change. Phase 2 adds in-proc SQL and sharding. Phase 3 stops provisioning Azure for PRs.
- **Fewer Azure resources and less cleanup.** PRs stop creating a resource group, NSP, Key Vault, AAD test applications, two SQL servers, an ACA environment, five container apps and a Cosmos account per build. The per-PR cleanup stages and the "Delete Old Resource Groups" job (p90 17 minutes) are no longer needed for unlabeled PRs.
- **Less real-Azure coverage before merge.** Real AAD, managed identity, Key Vault, ACA configuration, multi-replica cache sync, Azure SQL behavior and Cosmos throttling stop running on PRs unless full deployment is requested (label or runtime parameter). Their regressions are caught post-merge and attributed by bisect, so main can be red for a batch before it is fixed.
- **Label discipline.** Authors may forget the label on template, auth-registration or Cosmos-client changes. Post-merge CI catches these, and reviewers are expected to request the label.
- **SQL container differences.** The container uses SQL authentication rather than Entra workload identity, and does not reproduce Azure SQL resource governance or Azure-only database options. Schema compatibility against Azure SQL runs in Tier 3.
- **New test infrastructure to build and maintain:**
  - In-proc configuration loading from packaged test artifacts. Today `InProcTestFhirServer` reads `launchSettings.json` from the source tree, and inherited remote test URLs must be cleared.
  - Generated shard manifests and the shard harness.
  - Container readiness probes for SQL schema and Azurite blob read/write; fixed sleeps don't count.
  - Cosmos emulator start-up, certificate trust and readiness handling. If the fallback is needed: a sweeper for leaked containers, and RU budgeting that honors 429 retry delays.
  - The bisect orchestrator.
- **New failure modes:**
  - Container start-up and agent CPU/memory contention, including timing-sensitive import assertions.
  - Azurite addressing. Helpers recognize local storage by loopback address.
  - Emulator fidelity and start-up reliability: the emulator's query engine lags the service, and it does not emulate throttling. In the fallback, Cosmos throttling.

  Each needs measurement during the shadow period. Retries are not raised to compensate.
- **Test burden.** Tests that rely on server-global state must be identified and kept on the serial shard. Cosmos fixtures change ownership semantics.
- **Full-deployment PRs stay slow.** PRs that request full deployment, and release and hotfix branches, still take today's duration minus the Phase 1 savings.
- **Other ADRs.** This ADR changes CI policy only. It does not change product behavior or the FHIR contract, and it supersedes no existing ADR.
