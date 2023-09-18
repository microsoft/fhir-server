# Squash/Merge Requirements

When commiting your PR, please make sure to do these steps at Squash/Merge to assist with Release Note creation. Note that this does not apply to auto-generated PRs that have auto-generated titles (such as dependabot or MicrosoftHealthService PRs) - please leave these as-is.

1. **Update the title** of your PR to be succinct and less than 50 characters
1. **Add a milestone** to your PR for the sprint that it is merged (i.e. add S47)
1. Tag the PR with the type of update: **Bug**, **Enhancement**, or **New-Feature**
1. Tag the PR with **Azure API for FHIR** if this will release to the Azure API for FHIR and a tag for **Azure Healthcare APIs** if this will release to the FHIR service in the Azure Healthcare APIs.
1. Tag the PR with **Schema Version backward compatible** or  **Schema Version backward incompatible** if this adds new Sql script which is/is not backward compatible with the code.
1. Include a user friendly, 1-2 sentence in the Squash/Merge **description** wrapped at 72 characters
1. Note if it **addresses a GitHub issue and/or a VSTS item** in the Squash/Merge description (i.e. #1234 or AB#12345)

Sometimes our changes will create breaking changes or may need a warning associated with them. If this is the case, take these additional steps:

1. **Create a new issue** in GitHub that describes the warning/issue
1. Tag the PR with **KI-Breaking** or **KI-Warning**
1. **Reference the new GitHub issue** from the PR description

## Example

Squash/Merge commit heading:

PR title + PR number (both should add up to a total of maximum 50 characters)

`Adds CMK option to deployment templates. (#1234)`

Squash/Merge description:

User friendly description (maximum 72 characters per line) + GitHub or VSTS item number + Semver command (if relevant)

```
Enables customer managed keys for Cosmos DB backed services.
Adds details to the healthcheck endpoint when keys are not configured.

Refs AB#12345
+semver: patch
```
