# Full test matrix: requesting, approving and the merge-queue pilot

The PR pipeline (`PR Build & Deploy`, ADO definition 9, `build/pr-pipeline.yml`) validates most PRs with Tier 1 only:
SQL Server and Azurite in containers on the agent, no Azure credential
([ADR-2611](../arch/adr-2611-tiered-pr-validation.md)). The **full matrix** adds the deployed stages: Stu3, R4 and R5 on
Azure SQL, Stu3 and R4 on Cosmos DB, Azure Container Apps, Entra ID test apps and Key Vault. It takes 80 to 95 minutes
and uses the privileged service connection `oss-service-connection-with-workloadidentity`.

Sections (a) to (c) apply to `validationMode: tier1`. In the default `legacy` mode every PR run deploys the full matrix,
as before.

## (a) Requesting the full matrix

**Automatically, by path.** The `Classify` stage (`build/templates/jobs/classify-pr.yml`, rules in
`build/scripts/Get-PrValidationScope.ps1`) makes the scope `full` when the PR changes any of:

- a `src/` or `test/` project whose directory name contains `Cosmos`, or is a Stu3, R4B or R5 project
  (`*.Stu3.*`, `*.R4B.*`, `*.R5.*`);
- `build/**` or `samples/templates/**` (this covers the ACA and Key Vault configuration);
- an authentication path: `src/**/Security/**`, `src/Microsoft.Health.Fhir.Api.OpenIddict/**`;
- a root build input: `Directory.Build.props`, `Directory.Build.targets`, `global.json`, `nuget.config`;
- `Directory.Packages.props`, when a changed package (or a version property it uses) matches `Cosmos`, `^Azure.`,
  `Identity`, `OpenIddict`, `KeyVault` or `SqlClient`, or a build-wide property changes.

A PR that targets `release/*` or `hotfix/*` is always full. The run's `Classify validation scope` step lists every rule
that matched.

**The `Full-Test-Matrix` label.** `.github/labeler.yml` adds the label to PRs to `main` that change the paths above (all but
`Directory.Packages.props`, which only the classifier can judge), so reviewers see that a PR takes the full matrix.
`build/scripts/tests/Labeler.Tests.ps1` keeps the label's globs and the classifier in agreement. The labeler never
removes the label: if a later push drops those paths, the label stays and the PR keeps the full matrix.

To request the full matrix for any other PR:

1. Add the `Full-Test-Matrix` label. A label does not start a run: Azure Pipelines has no label trigger.
2. A maintainer comments on the PR:

   ```text
   /azp run PR Build & Deploy
   ```

   The new run reads the labels when it classifies and takes the full matrix. The commenter must be a collaborator with
   write access. Whether ADO honors `/azp run` from a bot (`github-actions[bot]` or an app token) is untested, so this
   is a maintainer step.

Fork PRs never take the full matrix from a label: their labels are not read (see (c)).

If the run logs `Could not read PR labels`, the GitHub API failed (often the unauthenticated rate limit on a shared
agent IP) and the PR was classified by its paths alone. Comment `/azp run PR Build & Deploy` again, or queue the run
manually with full scope (below).

**Manually.** Queue definition 9 on the branch `refs/pull/<n>/merge` with **Validation mode** = `tier1` and **Tier 1
scope (manual runs)** = `full`. The pipeline takes the PR number (`prName`) from the branch name, so the resource group,
test environment and cleanup are named as for the PR's own runs. Only do this for a PR from this repository; for a fork,
see (c).

## (b) Approving

**The label is only a request.** The PR supplies `build/**`, including the classifier, so a PR author can change what
the classifier decides. The real grant is the ADO approval check on the privileged service connection: a maintainer
approves each run that deploys, after reviewing the commit it runs.

Admin steps (a project administrator of `microsofthealthoss/FhirServer`; not done by this change):

1. **Project settings** > **Pipelines** > **Service connections** > `oss-service-connection-with-workloadidentity`.
2. **⋮ (More actions)** > **Approvals and checks** > **+** > **Approvals**: approvers = the FHIR server maintainers
   group, minimum 1, "Allow approvers to approve their own runs" off, timeout 2 hours (a run that waits longer fails
   and must be re-run).
3. **+** > **Branch control**: allowed branches `refs/heads/main, refs/pull/*, refs/heads/gh-readonly-queue/main/*` (add
   `refs/heads/gh-readonly-queue/mq-pilot/*` for the pilot), "Verify branch protection" on.
4. Repeat for every other connection and resource the deployed stages use (`docker-build`, and the variable groups and
   secure files that hold Entra ID and Key Vault secrets), or the gate can be bypassed through them.

Until the approval check exists, the routing in `classify-pr.yml` (deployed stages run only when the scope is full and
no maintainer is required) is the only control, and the PR can edit it.

## (c) Fork PRs

A fork PR runs all of Tier 1 and never receives a secret or service connection. When its scope is full, job
`RequireMaintainer` fails the run with a message and the deployed stages are skipped.

To run the full matrix for a fork PR, a maintainer:

1. reviews the PR, including every change under `build/`, `.github/` and any script the pipeline runs;
2. pushes the reviewed SHA to a branch in `microsoft/fhir-server`:

   ```bash
   git fetch origin pull/<n>/head
   git push origin <reviewed-sha>:refs/heads/maint/pr-<n>
   ```

3. opens a PR from that branch and approves its deployed run.

Never queue a fork's `refs/pull/<n>/merge` manually: a manual run is not a PR run, so ADO's fork protection does not
apply and the fork's YAML would run with the service connections.

## (d) Merge-queue pilot (admin only; not enabled by this change)

### Already in the repository

These pieces are inert while no merge-queue rule exists:

- **Trigger.** `build/pr-pipeline.yml` has `trigger:` on `gh-readonly-queue/main/*`, with no batching and no path
  filters, so each queue entry gets its own run and its `PR Build & Deploy` check. GitHub creates those branches only
  for a merge-queue rule.
- **Definition 9 overrides the YAML CI trigger.** Its only trigger is the pull request trigger: "Override the YAML
  continuous integration trigger from here" is on, with continuous integration disabled. **The YAML trigger does
  nothing until an admin turns that override off** (Pipelines > `PR Build & Deploy` > **Edit** > **⋮** > **Triggers** >
  **Continuous integration**). Turning it off enables only the YAML trigger, which lists queue branches alone.
- **Scope.** A run on `refs/heads/gh-readonly-queue/...` is always `full` (classifier reason `merge-queue: <branch>`).
  In `legacy` mode every run deploys anyway.
- **PR number.** `prName` comes from the branch (`pr-<n>-`), so resource groups are `msh-fhir-pr-<n>-<buildId>` and
  the per-stage cleanups work. The queue branch's base must be one path segment (`main`, `mq-pilot`).
- **Version.** `GitVersion.yml` versions `gh-readonly-queue/*` like a PR run (`<version>-pr<n>.<count>`). Without that
  entry every queue run's build number, which names the image tag and Key Vaults, would be `<version>ghreadonlyq`,
  the same for every PR.
- **Cleanup after the run.** `build/pr-cleanup-pipeline.yml` (definition 120) reads the PR number from queue branches
  too. Definition 120 always runs the YAML on `main`, so this takes effect only once merged.
- **Older resource groups.** A run deletes its PR's older groups, `msh-fhir-pr-<n>` and `msh-fhir-pr-<n>-*` only
  (`build/scripts/Test-PrResourceGroupName.ps1`). The previous pattern, `msh-fhir-pr-<n>*`, also matched every PR whose
  number starts with the same digits.
- **Fork gate (`tier1` mode only).** `Classify` looks up the queued PR (`GET /repos/microsoft/fhir-server/pulls/<n>`).
  When its head repository is not `microsoft/fhir-server`, or the lookup fails, the run needs a maintainer: the deployed
  stages are skipped and the queue ejects the entry. This is routing, not security: the queue branch carries the PR's
  own `build/`. `legacy` mode has no gate at all.

### Security: required before any pilot

A queue run is a CI build of this repository, not a PR build. ADO's fork protection and comment gate do not apply, the
run gets secrets and service connections, and the YAML it runs is the queue commit's, which includes the PR's own
`build/**`. Required:

1. The approval and branch-control checks of (b) on every privileged service connection and secret-holding resource.
2. `CODEOWNERS` entries for `/build/`, `/.github/`, `/GitVersion.yml`, `/global.json`, `/nuget.config`,
   `/Directory.Build.*`, `/Directory.Packages.props` and `/tools/`, owned by maintainers who know the pipeline.
3. The ruleset keeps required review, code-owner review and "Require approval of the most recent reviewable push", so a
   push after approval needs a new approval before the PR can be queued. Only users with write access can enqueue.
4. Queue runs use `tier1` mode, so the fork gate applies: either after the cutover makes it the default, or, for the
   pilot, by setting the `validationMode` default to `tier1` on the `mq-pilot` branch (queue commits carry its YAML).

### Pilot runbook

1. Merge to `main`, by normal review, a change that adds `gh-readonly-queue/mq-pilot/*` to the `trigger` of
   `build/pr-pipeline.yml` (a CI trigger cannot come from a template or a variable).
2. Create branch `mq-pilot` from `main`, with the `validationMode` default set to `tier1` (security item 4).
3. Admin: add the checks of (b), allowing `refs/heads/gh-readonly-queue/mq-pilot/*`; then turn off the CI trigger
   override of definition 9 (above).
4. Admin: create a repository ruleset that targets only `refs/heads/mq-pilot`:
   - require a pull request; require status check `PR Build & Deploy`;
   - **Require merge queue**: merge method SQUASH, `max_entries_to_build` = 1, `max_entries_to_merge` = 1,
     `min_entries_to_merge` = 1, `min_entries_to_merge_wait_minutes` = 0, `grouping_strategy` = ALLGREEN,
     `check_response_timeout_minutes` = 180 (runs reach 114 minutes, plus agent wait).

   Leave the `main` ruleset and branch protection unchanged.
5. Test PRs to `mq-pilot`, from branches in this repository unless stated:
   1. a docs-only change: it must take the full matrix;
   2. a second PR queued while the first runs;
   3. a PR with a failing test: it must be ejected;
   4. remove an entry while its run is in progress;
   5. a squash commit message containing `[skip ci]`;
   6. (security) a fork PR that changes `build/pr-pipeline.yml` to echo a marker: no step may use a service connection
      before the approval check.

**Success criteria.** The queue SHA gets the `PR Build & Deploy` check within about 5 minutes of the queue push; the
queue merges on green and ejects on red; `prName`, the resource group, Key Vault names, image tag and GitVersion output
are valid; resources are cleaned up: the next run's `cleanupOldResources` deletes the PR's older groups and nothing
outside its PR, definition 120 finds the PR number and deletes the run's group, and nothing is left after the daily
sweep; the fork PR is stopped before any service connection is used.

**Known risks.**

- **Ejected builds keep running.** When GitHub ejects or removes an entry it deletes the queue branch, but ADO does not
  cancel the build: it runs to completion (up to about 95 minutes), holding agents and Azure resources. Cancel it by
  hand during the pilot; automate the cancellation before enabling the queue on `main`.
- **`[skip ci]` stalls the queue.** A CI-triggered run honors `[skip ci]`, `[ci skip]` and `skip-checks: true` in the
  commit message, so a squash message containing one starts no run, the check never posts, and the entry waits for the
  check timeout before it is ejected. Remove those markers from squash messages.
- **ADO parallelism.** Each queue entry is a full deployed run beside the PR runs. At about 2.9 merges a day and 80 to 95
  minutes a run, a serial queue tops out near 15 merges a day. Measure agent wait and parallel runs in the pilot.
- **Same PR, two runs.** A queue run and a PR run of the same PR share `prName`, so each run's `cleanupOldResources`
  deletes the other's resource group. A push to a queued PR removes it from the queue, so this needs a manual run of
  the PR while it is queued.
- **Flaky tests** eject entries and rebuild every entry behind them. Measure the failure rate before enabling on `main`.

**Rollback.** Delete the `mq-pilot` ruleset and branch, revert the trigger change, turn the CI trigger override of
definition 9 back on, and delete the pilot PRs' leftover `msh-fhir-pr-<n>-*` resource groups.

Background research: `docs/ci/merge-queue-research.md` on branch `personal/bkowitz/merge-queue-azure-devops-research`.
