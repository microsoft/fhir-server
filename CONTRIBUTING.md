# Contributing to FHIR server

This document describes guidelines for contributing to the FHIR server repo.

## Submitting Pull Requests

- **DO** submit all changes via pull requests (PRs). They will be reviewed and potentially be merged by maintainers after a peer review that includes at least one of the team members.
- **DO** give PRs short but descriptive names.
- **DO** write a useful but brief description of what the PR is for.
- **DO** refer to any relevant issues and use [keywords](https://help.github.com/articles/closing-issues-using-keywords/) that automatically close issues when the PR is merged.
- **DO** ensure each commit successfully builds. The entire PR must pass all checks before it will be merged.
- **DO** address PR feedback in additional commits instead of amending.
- **DO** assume that [Squash and Merge](https://blog.github.com/2016-04-01-squash-your-commits/) will be used to merge the commits unless specifically requested otherwise.
- **DO NOT** submit "work in progress" PRs. A PR should only be submitted when it is considered ready for review.
- **DO NOT** mix independent and unrelated changes in one PR.

## Coding Style

The coding style is enforced through [StyleCop.Analyzers](https://github.com/DotNetAnalyzers/StyleCopAnalyzers), [.editorconfig](.editorconfig), and [stylecop.json](stylecop.json). Contributors should ensure these guidelines are followed when making submissions.

- **DO** address the StyleCop.Analyzers errors.
- **DO** follow the [.editorconfig](.editorconfig) and [stylecop.json](stylecop.json) settings.

## Creating Issues

- **DO** use a descriptive title that identifies the issue or the requested feature.
- **DO** write a detailed description of the issue or the requested feature.
- **DO** provide details for issues you create:
  - Describe the expected and actual behavior.
  - Provide any relevant exception message or OperationOutcome.
- **DO** subscribe to notifications for created issues in case there are any follow-up questions.
