## Description
Describe the changes in this PR.

<!-- State the intended user/FHIR outcome, important invariants, and compatibility risks. For permission or filtering changes, describe both permitted and refused behavior. Identify affected layers, SQL/Cosmos providers, FHIR versions, and derived paths such as includes, continuation, bundles, or background jobs; use N/A where inapplicable. -->

## Related issues
Addresses [issue #].

<!-- FHIR team members: retain the required Azure DevOps work-item reference or link. Link relevant ADRs. If no linked issue applies, state acceptance criteria in Testing rather than inferring them from the implementation. -->

## Testing
Describe how this change was tested.

<!-- Map each acceptance criterion (from the linked work item/issue, or stated here) to evidence that would detect a violation. Include exact commands or CI-job links, results, and the providers/versions/configurations exercised. If automation is impractical, record reproducible manual steps, observed results, and the limitation. Use "none" for unverified criteria. Declare assumptions, untested paths, and what this change does not promise. -->

| Acceptance criterion | Implemented in | Evidence and result |
|---|---|---|
|  |  |  |

## FHIR Team Checklist
- **Update the title** of the PR to be succinct and less than 65 characters
- **Add a milestone** to the PR for the sprint that it is merged (i.e. add S47)
- Tag the PR with the type of update: **Bug**, **Build**, **Dependencies**, **Enhancement**, **New-Feature** or **Documentation**
- Tag the PR with **Open source**, **Azure API for FHIR** (CosmosDB or common code) or **Azure Healthcare APIs** (SQL or common code) to specify where this change is intended to be released.
- Tag the PR with **Schema Version backward compatible** or **Schema Version backward incompatible** or **Schema Version unchanged** if this adds or updates Sql script which is/is not backward compatible with the code.
- When changing or adding behavior, if your code modifies the system design or changes design assumptions, please create and include an [ADR](https://github.com/microsoft/fhir-server/blob/main/docs/arch).
- [ ] CI is green before merge [![Build Status](https://microsofthealthoss.visualstudio.com/FhirServer/_apis/build/status/CI%20Build%20%26%20Deploy?branchName=main)](https://microsofthealthoss.visualstudio.com/FhirServer/_build/latest?definitionId=27&branchName=main) 
- Review [squash-merge requirements](https://github.com/microsoft/fhir-server/blob/main/SquashMergeRequirements.md)

### Semver Change ([docs](https://github.com/microsoft/fhir-server/blob/main/docs/Versioning.md))
Patch|Skip|Feature|Breaking (reason)
