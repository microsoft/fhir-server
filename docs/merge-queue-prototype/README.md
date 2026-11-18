# Merge queue prototype

A throwaway prototype that lets a GitHub merge queue gate on the full Azure DevOps `PR Build & Deploy` run.

## What this is

This branch tests whether a GitHub merge queue can gate on definition 9 (`PR Build & Deploy`), the same run a pull request needs today. The queue guards only the branch `mq-prototype`.

**Boundary:** this branch is never merged into `main`. Nothing changes on `main`, in any Azure DevOps definition (9, 120, 100, 118) or in the protection of `main`, and pull requests to `main` keep running `main`'s own YAML.

ADR 2611 on tiered PR validation (`docs/arch/adr-2611-tiered-pr-validation.md`, proposed in PR #6061) lists a merge queue as a "future option, not adopted"; this prototype produces the evidence for that option. That PR rewrites `build/pr-pipeline.yml` and edits `build/pr-cleanup-pipeline.yml`, so if it lands first, re-apply the edits listed under "How it works" to the new layout instead of rebasing mechanically.

The plumbing is verified offline (the Azure DevOps compile preview, GitVersion and the real cleanup script). Whether the CI trigger starts definition 9 for a queue branch, and how a live queue behaves, can only be shown live. That is what the test plan below measures, and T0 comes first because every other case depends on it.

## How it works

1. A maintainer enqueues a pull request that targets `mq-prototype`.
2. GitHub cuts the queue branch `gh-readonly-queue/mq-prototype/pr-<n>-<sha>` (the base plus the pull request).
3. A CI trigger added to the YAML of definition 9 (`build/pr-pipeline.yml`) is meant to match that branch and start `PR Build & Deploy` (T0 confirms that it does). Azure Pipelines has no `merge_group` event, so a branch trigger is the only hook.
4. A queue build has no pull request number, so `prName` is taken from the branch name (`pr-<n>-<sha>` gives `<n>`). T0 checks that this value beats the pipeline's UI variable `PrName`.
5. GitVersion gives the build an `mq<n>` pre-release tag, for example `5.2.95-mq6061.0`, so the build number differs from the `pr<n>` form.
6. The Azure Pipelines GitHub app reports the `PR Build & Deploy` check on the queue commit (T0 confirms), and GitHub squash-merges the entry when that check is green.

Files edited compared with `main`:

| File | Change |
|---|---|
| `build/pr-pipeline.yml` | CI trigger on `gh-readonly-queue/mq-prototype/pr-*`; queue-only `prName` taken from the branch name. |
| `GitVersion.yml` | `merge-queue` branch entry with the `mq` tag. |
| `build/pr-cleanup-pipeline.yml` | Accepts `refs/heads/gh-readonly-queue/<base>/pr-<n>-<sha>` when parsing the pull request number. |

**Security note.** A queue build of a fork pull request runs with the privileged Azure service connection and without the fork comment gate. ADR 2611 requires an Azure DevOps approval check on that connection before any pilot, and this prototype adds none because it must not change Azure DevOps settings. So enqueue only pull requests whose code you wrote or reviewed in full (especially `build/` and scripts), and never an external contributor's fork pull request.

## Enable it (repo admin)

Creating a ruleset needs repository admin rights. From the repository root:

```bash
gh api -X POST repos/microsoft/fhir-server/rulesets --input docs/merge-queue-prototype/ruleset.mq-prototype.json
```

The ruleset targets only `refs/heads/mq-prototype`. It requires a pull request (squash only, no approvals) and the `PR Build & Deploy` check, and it turns on the merge queue: one entry at a time, squash merge, 120-minute check timeout.

Verify:

- `gh api repos/microsoft/fhir-server/rules/branches/mq-prototype` returns three rules (`pull_request`, `required_status_checks`, `merge_queue`).
- `gh api repos/microsoft/fhir-server/rules/branches/main` is unchanged from before.

To disable it, find the ruleset id in the create response or with `gh api repos/microsoft/fhir-server/rulesets`, then:

```bash
gh api -X PUT repos/microsoft/fhir-server/rulesets/<id> -f enforcement=disabled
```

While the ruleset is active, direct pushes to `mq-prototype` are blocked, so disable it first to push. `license/cla` is deliberately not required: it is posted on pull request heads, not on queue commits. T1 confirms this.

## Test plan

| Case | Do | Look for |
|---|---|---|
| T0 | Needs no ruleset or admin, but starts a full build, so agree it first. Push the hand-made branch `gh-readonly-queue/mq-prototype/pr-999999-<mq-prototype tip sha>` (commands below). `999999` is synthetic, so cleanup can never touch the resources of a real pull request. Let the run finish, because cancelling mid-run can leak resource groups; delete the branch after definition 120 completes. | GitHub accepts the branch name. Definition 9 starts as `IndividualCI` within 5 minutes. The build number matches `^\d+mq999999\d+$` and is at most 15 characters. The resource group is `msh-fhir-pr-999999-<buildId>`. The `PR Build & Deploy` check comes from `azure-pipelines` on the commit. Whether definition 120 fires, and whether it runs the YAML of `main` or of the queue branch. |
| T1 | Happy path: open a pull request into `mq-prototype`, let its PR build go green, then enqueue it. | The check is posted on the queue commit. `license/cla` does not block. The squash merge happens on green. Cleanup ran. Elapsed time. |
| T2 | Failed queue build: cancel the queue build in Azure DevOps mid-run, or enqueue two pull requests that only fail together. | The entry is ejected with a failed required check. The resource group stays behind (definition 120 fires only on success) until definitions 100 and 118 sweep it. |
| T3 | Dequeue a pull request while its queue build runs. | The Azure DevOps build keeps running. Cancel it by hand and note what it leaked. |
| T4 | After the PR build is green, put `[skip ci]` in the pull request title (the squash message comes from it), then enqueue. | No build starts on the queue commit. The entry waits for the 120-minute check timeout, then is ejected. |
| T5 | Queue two pull requests. | Builds run one at a time. The second queue branch is cut after the first merges. Each resource group carries its own PR number. |
| T6 | Optional, and only with a fork pull request you wrote yourself from your own fork (see the security note). | The build starts with no comment gate and with the privileged service connection. This measures that exposure; it does not make an external contributor's fork pull request safe to enqueue. |

T0 commands (they work in bash and PowerShell; `<sha>` is the 40-character commit that `rev-parse` prints, written out in both pushes):

```text
git fetch origin mq-prototype
git rev-parse origin/mq-prototype
git push origin <sha>:refs/heads/gh-readonly-queue/mq-prototype/pr-999999-<sha>
# after definition 120 completes:
git push origin --delete gh-readonly-queue/mq-prototype/pr-999999-<sha>
```

## Costs and cleanup

- One queue build takes about 88 minutes and uses a full set of Azure resources (`msh-fhir-pr-<n>-<buildId>`). A PR build plus a queue build is about 3 hours per scenario.
- Queue and PR builds of the same pull request share the `msh-fhir-pr-<n>` namespace and can delete each other's resource groups. This is accepted for the prototype.
- Definition 120 may run the YAML of `main` and exit 1 on queue runs. Cleanup then stays manual through definition 100 (`cleanup-pr-environments`, which sweeps `msh-fhir-pr*` older than 7 days) and definition 118 (the weekly AAD sweep).
- Never queue definition 29 (`Deploy CI if broken`): it deletes the shared `msh-fhir-ci` resource group.

## Findings to record

- [ ] Does the YAML CI trigger start definition 9 when GitHub cuts a queue branch (T0)? Every other case depends on it.
- [ ] Does the Azure Pipelines app post `PR Build & Deploy` on queue commits (T0, T1)?
- [ ] Does GitHub accept the hand-made branch name (T0)?
- [ ] Which YAML does definition 120 run for a queue build, and does it race the deletion of the queue branch?
- [ ] Does the YAML `prName` beat the UI variable `PrName` on a real CI run? If not, expect a literal `$(...)` in the resource group name, which should fail at creation.
- [ ] How do Codecov and the AAD steps behave on a run that is not a pull request?
- [ ] Elapsed time per queue entry.

## Teardown

1. Disable or delete the ruleset: `gh api -X DELETE repos/microsoft/fhir-server/rulesets/<id>`.
2. Delete `mq-prototype` and any `gh-readonly-queue/mq-prototype/*` branches.
3. Sweep leaked `msh-fhir-pr-*` resource groups with definition 100 (`cleanup-pr-environments`).
