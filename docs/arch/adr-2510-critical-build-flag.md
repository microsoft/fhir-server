# ADR 2510: Critical-Only Build Flag for CI/PR Pipelines
Labels: [CI/CD](https://github.com/microsoft/fhir-server/labels/Area-CI-CD)

## Context
The FHIR server supports four FHIR specification versions: STU3, R4, R4B, and R5. In production environments, the majority of deployments use STU3 and R4, which are mature and widely adopted specifications. R4B and R5 are newer versions with limited production usage.

The current CI and PR pipelines build, deploy, and test all four versions for every commit. Each version requires:
- Docker image builds (multi-platform for CI)
- Deployment to test environments (CosmosDB + SQL variants)
- Integration tests (CosmosDB + SQL)
- End-to-end tests (CosmosDB + SQL)

This comprehensive validation is essential for ensuring quality across all supported versions. However, when shipping critical hotfixes or iterating rapidly on production issues affecting only R4 or STU3, the time spent deploying and testing R4B and R5 represents significant overhead. A typical full CI build takes substantial time, with R4B and R5 stages accounting for approximately 40% of deployment and test time.

We need a mechanism to accelerate builds when working exclusively on R4 and STU3 production issues, while maintaining the default behavior of comprehensive multi-version validation.

## Decision
We will add a `buildCriticalOnly` parameter to both the CI and PR pipeline definitions. When set to `true`, this parameter will:

1. **Skip deployment and test stages** for R4B and R5 versions:
   - Deployment stages (CosmosDB + SQL)
   - Integration test jobs
   - End-to-end test jobs

2. **Skip Docker image builds** for R4B and R5 versions in the `docker-build-all.yml` job template

3. **Continue to build all code** in the solution, including R4B and R5 projects, during the build stages. This ensures that changes to shared code (e.g., Core libraries) do not introduce compilation errors in non-critical versions.

The parameter will default to `false`, ensuring that the standard behavior remains comprehensive validation across all versions. Developers and release engineers can opt into the fast path explicitly when appropriate.

### Implementation Approach
- Add `buildCriticalOnly` parameter (type: boolean, default: false) to pipeline definitions
- Use Azure Pipelines conditional syntax (`condition: eq(variables.buildCriticalOnly, false)`) on R4B and R5 stages
- Update stage dependencies to handle conditionally skipped stages
- Use template conditionals (`${{ if }}`) to skip R4B/R5 Docker builds

### Usage Guidelines
**When to use `buildCriticalOnly: true`:**
- Hotfixes for production R4 or STU3 issues
- Rapid iteration during R4/STU3 feature development
- Cost-sensitive CI runs when R4B/R5 validation is not required

**When NOT to use it:**
- Before merging to main branch (full validation recommended)
- When changes affect shared Core libraries (cross-version validation needed)
- Release builds
- When explicitly developing or fixing R4B/R5 functionality

## Status
Accepted

## Consequences
### Benefits:
- **Reduced build time**: 20-30% time savings by skipping ~40% of deployment and test stages
- **Faster hotfix delivery**: Critical R4/STU3 fixes can be validated and shipped more quickly
- **Lower build costs**: Reduced Azure DevOps agent minutes and Azure resource usage for test environments
- **Preserved safety**: Default behavior remains unchanged; full validation is opt-out, not opt-in

### Adverse Effects:
- **Maintenance overhead**: New parameter must be propagated when adding future FHIR versions
- **Risk of misuse**: Developers might use the flag inappropriately, skipping validation when it's needed
- **Incomplete time savings**: All code still compiles, so savings are less than a full solution filter approach would provide

### Neutral Effects:
- **No impact on default behavior**: Existing builds and processes remain unchanged

## References
- CI Pipeline: `build/ci-pipeline.yml`
- PR Pipeline: `build/pr-pipeline.yml`
- Docker Build Template: `build/jobs/docker-build-all.yml`
