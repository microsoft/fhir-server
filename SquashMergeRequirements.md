# Squash/Merge Requirements

When commiting your PR, please make sure to do these steps at Squash/Merge to assist with Release Note creation.

1. **Update the title** of your PR to be succinct and less than 50 characters
1. **Add a milestone** to your PR for the sprint that it is merged (i.e. add S47)
1. Tag the PR with the type of update: **Bug**, **Enhancement**, or **New-Feature**
1. Tag the PR with **Azure API for FHIR** if this will release to the managed service
1. Include a user friendly, 1-2 sentence in the Squash/Merge **description** wrapped at 72 characters
1. Note if it **addresses a GitHub issue and/or a VSTS item** in the Squash/Merge description with a link (i.e. #1234 or AB#12345)

Sometimes our changes will create breaking changes or may need a warning associated with them. If this is the case, take these additional steps:

1. **Create a new issue** in GitHub that describes the warning/issue
1. Tag the PR with **KI-Breaking** or **KI-Warning**
1. **Reference the new GitHub issue** from the PR description
