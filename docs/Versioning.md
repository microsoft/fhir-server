# Semantic Versioning for FHIR Server

This guide gives an overview of the Semantic versioning implementation in use with this project.
To achieve semantic versioning consistently and reliably the [GitVersion](https://github.com/GitTools/GitVersion) library is used.

## Git Version

### Overview
GitVersion is a software library and build task that uses Git history to calculate the version that should be used for the current build. The following sections explain how it is configured and the commands available to assist in versioning.

### Setup
A [configuration](https://github.com/microsoft/fhir-server/blob/master/GitVersion.yml) file is included in the root directory that is used to setup the version strategy and specify how versioning should be calculated against the default and other branches. Currently, all commits to main will be treated as a release, all commits to other branches (including pull requests) will be treated as pre-release (e.g. `1.2.0-my-branch+1`).

The configured GitVersion versioning strategy is [mainline development](https://gitversion.net/docs/reference/versioning-modes/mainline-development), which increments the patch version on every commit to the main branch. Our current development workflow assumes that the main branch will stage a release on every commit, some releases however will be not be approved.

When a release is approved this should result in the assets being published to the nuget feed and a tag being created against the code to mark the release.

### Commands
Several commands are available during the squash-merge to allow incrementing the major/minor release numbers.

For a major feature or major breaking changes, the following commands can be added to the commit message:
```
+semver: breaking
or
+semver: major
```

Smaller changes can choose to increment the minor version:
```
+semver: feature
or
+semver: minor
```

For bug fixes or other incremental changes, nothing needs to be added, this will happen automatically.