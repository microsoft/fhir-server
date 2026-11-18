# Reviewing Changes and Verifying Copilot Guidance

[AGENTS.md](../AGENTS.md) defines repository conventions. [REVIEW.md](../REVIEW.md) adds evidence-first review criteria, and the security instruction files in [`instructions/`](instructions) add boundary checks. These are advisory instructions, not a scanner, certification, or merge gate.

The security rules are split so that each review loads a short, specific set:

| File | Loaded for | Covers |
|---|---|---|
| [`fhir-security.instructions.md`](instructions/fhir-security.instructions.md) | Every path (`applyTo: "**"`) | The authorization invariant, ten change patterns to flag, what not to flag, input/output limits, how to write a finding |
| [`security-search.instructions.md`](instructions/security-search.instructions.md) | Search, compartment, definitions, SQL schema, Cosmos queries and storage, `$everything`, `$member-match`, resource reads, query parameter names | Alternate execution paths, includes and chaining, paging, history, definitions |
| [`security-background-operations.instructions.md`](instructions/security-background-operations.instructions.md) | Operations, task hosting, delete, upsert, persistence | Carrying caller restrictions into jobs, job status and results |
| [`security-bundles-conditional.instructions.md`](instructions/security-bundles-conditional.instructions.md) | Resource handlers, bundles, validation | Bundle admission, conditional lookups, derived writes |
| [`security-smart-scopes.instructions.md`](instructions/security-smart-scopes.instructions.md) | SMART, security, context, conformance, modules, pipeline registration, the development identity provider, configuration, controllers, filters | Scope parsing, identity binding, new endpoints and inputs |

Copilot code review does not follow links between instruction files, so each file is self-contained. Keep each rule short, imperative, and tied to a type or pattern a reviewer can find in a diff. When a path-specific file grows past roughly twenty rules, split it instead of lengthening it.

A file can match more than one selector, and a change loads every file whose selector matches. When code that a rule targets lives outside that rule file's selector, widen the selector instead of copying the rule into another file.

`excludeAgent: "cloud-agent"` excludes these files from GitHub's cloud coding agent, not from code review. It is not a universal control over local clients.

## Before merging

1. Confirm intent, acceptance criteria, invariants, and affected providers/versions in the [PR template](pull_request_template.md). Missing evidence is a gap; a manual check must record reproducible steps and observed results.
2. Read the review and CI evidence for the current revision. Inspect actual jobs, selected tests, assertions, skipped cases, and artifacts. A green rollup, job name, or review run means only what that check established.
3. Verify material findings against the code and tests. Fix real defects and rerun affected checks; record concrete evidence when dismissing a false positive. Resolving a thread alone is not proof of a fix.
4. Personally inspect authorization, sensitive-data, schema, deployment, and review/CI-control changes. Copilot reads instructions from the PR head, so compare instruction changes with the target branch rather than trusting a PR's revised quality bar.
5. Confirm applicable live branch/ruleset and approval requirements. Automatic review requests, approval assessments, counted Copilot approvals, and automatic merging are separate settings. This documentation changes none of them and does not establish their current values.

If a review is missing, failed, or stale, request a current review or record a manual review against the same criteria; absence is not approval. An overview saying **"No open issues"** describes published issues, not proof of safety. If it also reports an unpublished security concern or **"Needs a closer look"**, obtain an authorized human assessment before treating the PR as ready. Do not guess why publication was withheld, reconstruct undisclosed details, or repost restricted evidence publicly.

## After a security report or fix

Pull request review only sees the lines a pull request changes. A flaw in code that no pull request touches is found by an audit, not by review. Treat each confirmed report as three pieces of work:

1. **Fix and test.** Add a regression test that fails without the fix, with one caller that must succeed and one that must be refused.
2. **Sweep for siblings.** Run the [security variant sweep prompt](prompts/securityVariantSweep.prompt.md) against `main` for the same pattern in other handlers, jobs, providers, and FHIR versions. Handle anything it finds through the private advisory process, not a public issue or pull request.
3. **Update the rules.** If no existing rule would have led a reviewer to the flaw, add or sharpen one in the matching instruction file. Describe the pattern, not the exploit, and do not reference an unpublished advisory.

`SearchServiceConsumerGuardTests` fails the build when a new type is constructed with `ISearchService`. It does not show that existing consumers are safe. When it fails, review the new consumer against the search rules before adding it to the list.

## Verify instruction loading for a specific review

GitHub documents `.github/copilot-instructions.md`, root `AGENTS.md`, `REVIEW.md`, and matching `.github/instructions/**/*.instructions.md` as supported review inputs. Confirm that repository custom instructions are enabled in Copilot Code review settings; file presence alone does not establish use.

1. Record the PR head SHA and the specific review's commit, timestamp, and outcome. A later push does not update an earlier review.
2. Open that review's linked session/workflow run from the PR timeline. Match the run's revision to the reviewed SHA; do not substitute a successful run from a different push.
3. Inspect the instruction-loading portion of the logs. Where available, a `[customInstructions] Built ... names=[...]` entry identifies the files discovered for that run; a `head-checkout` entry identifies their source. Log wording can change. If no loading evidence is exposed, record loading as unverified, not failed or successful.
4. Record the run URL, SHA, timestamp, loaded file names, and any instruction-specific attribution attached to a finding. Do not infer use of every linked document or compliance with every instruction from a file being listed.

These read-only CLI commands help locate the evidence. Replace the uppercase placeholders; use the run linked to the review, not merely the newest build:

```powershell
gh pr view PR_NUMBER --repo microsoft/fhir-server --json headRefOid,reviews
gh run list --repo microsoft/fhir-server --branch HEAD_BRANCH --limit 20
gh run view RUN_ID --repo microsoft/fhir-server --json headSha,url,jobs
gh run view RUN_ID --repo microsoft/fhir-server --log |
    Select-String -Pattern '\[customInstructions\]|head-checkout'
```

Logs and settings may require additional permissions. An inaccessible or absent result is an evidence limitation, not proof that instructions were disabled.

## Loading is not causal attribution

Keep three claims separate:

| Claim | Useful evidence | What it does not establish |
|---|---|---|
| The instructions loaded | Matching review/run SHA and loader entries naming the files | Every instruction was followed |
| A review reported a relevant defect | The finding, source trace, authorized local reproduction or regression test | The custom guidance caused detection, or the AI's severity is correct |
| The guidance improved detection | Repeated controlled comparisons with known-bad and intentional-safe cases | Complete defect detection or a permanent safety guarantee |

A finding about job authorization aligns with guidance covering kickoff, status, cancellation, and results, but Copilot's baseline review may also find it. Review effort, context, model changes, and nondeterminism are alternative explanations.

For a **separately authorized** evaluation, use isolated, clearly marked do-not-merge control branches/PRs with equivalent synthetic code changes, with and without the new review profile. Preserve existing baseline guidance and live protections; do not disable instructions repository-wide. Hold review effort and other controllable inputs fixed, record each revision and loaded profile, repeat observations, and compare misses and false positives against known expectations. Include an authorized-success case so blanket denial cannot pass. Use no real patient data, credentials, or live-service probes; never merge a control.

Use lessons from real PRs to refine generalizable guidance and add regression tests, not to promise that one successful detection establishes effectiveness.

## CI evidence in this repository

The following describes the checked-in entry points, not a verified inventory of live required checks or Azure Pipelines settings:

| Entry point | What to inspect |
|---|---|
| [PR pipeline](../build/pr-pipeline.yml) and its referenced templates | Azure Pipelines build, packaging, security, and test stages. It declares `trigger: none` and no explicit `pr` path filter; this alone does not establish effective PR triggers, UI overrides, or required-check policy. |
| [PR internal checks](../build/.vsts-PRInternalChecks-azureBuild-pipeline.yml) | Build and analysis tasks, including tolerated failures and final analysis results. A task starting is not evidence that its findings passed. |
| [Validate Pull Request](workflows/validate-prs.yml) | `pull_request_target` metadata checks for main, without a path filter. This checks PR metadata, not application behavior. |
| [CodeQL](workflows/codeql.yml) | Push/PR filters exclude Markdown, JSON, and HTTP-file-only changes. Inspect the applicable language jobs and scan configuration; these filters do not prove the check is optional or required. |
| [ARM validation](../build/pr-arm.yml) | PR path inclusion is limited to `samples/templates`; inspect live requirements before changing this specialized trigger. |

Do not copy another repository's CI filter removal without establishing which checks are required here and how they are triggered. Missing, cancelled, unexpectedly skipped, or failed work is not a pass. Evidence for one provider, FHIR version, feature flag, or test suite does not establish coverage of the others. This guidance does not modify any workflow, trigger, or merge requirement.

## References

- [Using Copilot code review and custom instructions](https://docs.github.com/en/copilot/how-tos/use-copilot-agents/use-code-review)
- [Repository instruction scope and agent exclusions](https://docs.github.com/en/copilot/how-tos/copilot-on-github/customize-copilot/add-custom-instructions/add-repository-instructions)
- [Source adaptation: brendankowitz/ignixa-fhir#521](https://github.com/brendankowitz/ignixa-fhir/pull/521)
