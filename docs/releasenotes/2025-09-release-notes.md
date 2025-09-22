# Microsoft FHIR Server Release Notes - September 2025

## Overview
This document outlines the changes, improvements, and fixes implemented in the Microsoft FHIR Server during September 2025. The release contains critical bug fixes, performance enhancements, and new features across both Azure Health Data Services (SQL) and Common Platform components.

## Azure Health Data Services (SQL)

### Bug Fixes

#### Fixed search parameter synchronization issues across multiple instances
**Old behavior:** Search parameters were not being properly synchronized across multiple FHIR server instances, leading to inconsistent behavior when search parameters were added or updated.
**New behavior:** Implemented SearchParameterCacheRefreshBackgroundService for cross-pod synchronization of Search Parameter changes with configurable refresh intervals.
**Impact:** Resolves multi-instance SearchParameter synchronization issues in distributed deployments.
[PR #5150](https://github.com/microsoft/fhir-server/pull/5150) - [AB#170098](https://microsofthealth.visualstudio.com/Health/_workitems/edit/170098)

#### Resolved compartment search expression comparison bug
**Old behavior:** Compartment search expressions were not properly considering compartmentId during comparison, causing duplicate expressions and incorrect search results.
**New behavior:** Search expressions now properly check compartmentId when comparing, and handle multiple compartment expressions correctly.
**Impact:** Fixes search result accuracy for compartment-based queries.
[PR #5137](https://github.com/microsoft/fhir-server/pull/5137) - [AB#169470](https://microsofthealth.visualstudio.com/Health/_workitems/edit/169470)

#### Fixed system-level SMART search with compartment resources
**Old behavior:** System-level searches with historical records and access to all resources were not returning compartment resources properly.
**New behavior:** Compartment resources are now correctly returned when searching on system level with historical records and access to all resources.
**Impact:** Ensures complete search results for SMART-enabled applications.
[PR #5141](https://github.com/microsoft/fhir-server/pull/5141) - [AB#169986](https://microsofthealth.visualstudio.com/Health/_workitems/edit/169986)

#### Corrected count logic for bulk update partition jobs
**Old behavior:** Resource type-surrogate id partition bulk update jobs had incorrect count logic, leading to improper job execution.
**New behavior:** Fixed the count logic to properly handle resource type-surrogate id partition bulk update jobs.
**Impact:** Improves reliability of bulk update operations.
[PR #5134](https://github.com/microsoft/fhir-server/pull/5134) - [AB#168715](https://microsofthealth.visualstudio.com/Health/_workitems/edit/168715)

#### Fixed search parameter enablement after reindex completion
**Old behavior:** Search parameters with no impacted resources were not being marked as enabled after reindex job completion.
**New behavior:** All search parameters are now properly marked as enabled after reindex completion, regardless of whether they have impacted resources.
**Impact:** Ensures all custom search parameters are usable after creation.
[PR #5132](https://github.com/microsoft/fhir-server/pull/5132) - [AB#168980](https://microsofthealth.visualstudio.com/Health/_workitems/edit/168980)

#### Resolved custom search parameter conflicts with built-in functionality
**Old behavior:** Custom SearchParameters with the same code as built-in parameters were affecting built-in search functionality.
**New behavior:** Custom search parameters no longer interfere with built-in search functionality when they share the same code.
**Impact:** Maintains system stability while allowing custom search parameter flexibility.
[PR #5024](https://github.com/microsoft/fhir-server/pull/5024) - [AB#117004](https://microsofthealth.visualstudio.com/Health/_workitems/edit/117004)

#### Eliminated race conditions in search parameter operations
**Old behavior:** Concurrent search parameter create, update, or delete operations would fail unpredictably due to race conditions.
**New behavior:** Implemented robust two-layer concurrency control with application-level pessimistic locking and database-level optimistic concurrency.
**Impact:** Ensures reliable search parameter operations in high-load environments.
[PR #5097](https://github.com/microsoft/fhir-server/pull/5097) - [AB#164155](https://microsofthealth.visualstudio.com/Health/_workitems/edit/164155)

### Enhancements

#### Improved bulk update operation performance
**Enhancement:** Optimized bulk update operations by processing records in batches of 1,000 instead of 10,000, enqueuing CT-level sub-jobs immediately after reading each page, and verifying job status before creating new ones.
**Impact:** Significantly improves bulk update performance and resource utilization.
[PR #5124](https://github.com/microsoft/fhir-server/pull/5124) - [AB#168715](https://microsofthealth.visualstudio.com/Health/_workitems/edit/168715)

#### Enhanced async operation query optimization
**Enhancement:** Included ResourceSurrogateId in query hash for async operations to improve query caching and performance.
**Impact:** Better query optimization and caching for background operations.
[PR #5128](https://github.com/microsoft/fhir-server/pull/5128) - [AB#167462](https://microsofthealth.visualstudio.com/Health/_workitems/edit/167462)

## Common Platform

### Bug Fixes

#### Fixed URL construction for bundles with forwarded headers
**Old behavior:** Bundle URL construction was incorrect when using forwarded headers, leading to malformed URLs.
**New behavior:** Properly constructs URLs by appending "/" to BaseUri and correctly extracting Path from bundle entries.
**Impact:** Resolves URL issues in proxy scenarios for bundle operations.
[PR #5129](https://github.com/microsoft/fhir-server/pull/5129)

### Enhancements

#### Enhanced import statistics with ImportMode information
**Enhancement:** Added ImportMode as part of Import Statistics to provide better visibility into import operations.
**Impact:** Improved monitoring and diagnostics for import operations.
[PR #5145](https://github.com/microsoft/fhir-server/pull/5145) - [AB#170321](https://microsofthealth.visualstudio.com/Health/_workitems/edit/170321)

#### Updated documentation with architecture details
**Enhancement:** Added architecture section and ProcessingUnitBytesToRead parameter documentation to import documentation.
**Impact:** Better understanding of system architecture and configuration options.
[PR #5133](https://github.com/microsoft/fhir-server/pull/5133)

#### Simplified generic parameter naming
**Enhancement:** Replaced ProcessingJobBytesToRead with more generic ProcessingUnitBytesToRead for better consistency.
**Impact:** Improved parameter naming consistency across the codebase.
[PR #5127](https://github.com/microsoft/fhir-server/pull/5127)

### Infrastructure Changes

#### Reverted network service perimeter associations
**Infrastructure:** Temporarily reverted CosmosDB association to network service perimeter due to deployment issues.
**Impact:** Maintains service availability while security configurations are adjusted.
[PR #5149](https://github.com/microsoft/fhir-server/pull/5149)

#### Updated deployment region configuration
**Infrastructure:** Changed resource group region from eastus2 to westus2 to avoid Cosmos DB Account provisioning failures.
**Impact:** Improves deployment reliability by using more stable regions.
[PR #5136](https://github.com/microsoft/fhir-server/pull/5136) - [AB#169462](https://microsofthealth.visualstudio.com/Health/_workitems/edit/169462)

#### Performance testing improvements
**Infrastructure:** Multiple improvements to test execution including compute upgrades (P2V3), database scaling (S4), and optimized polling intervals.
**Impact:** Reduced SQL E2E test execution time from ~50 minutes to <25 minutes and improved overall testing efficiency.
[PR #5118](https://github.com/microsoft/fhir-server/pull/5118)

## Assets

### Docker Images
- **Azure Container Registry**: `healthplatformregistry.azurecr.io/fhir-server:4.0.516`
- **Docker Hub**: `mcr.microsoft.com/healthcareapis/fhir-server:4.0.516`

### NuGet Packages
- **Microsoft.Health.Fhir.R4.Core**: 4.0.516
- **Microsoft.Health.Fhir.R5.Core**: 4.0.516  
- **Microsoft.Health.Fhir.Stu3.Core**: 4.0.516
- **Microsoft.Health.Fhir.CosmosDb**: 4.0.516
- **Microsoft.Health.Fhir.SqlServer**: 4.0.516

### Release Artifacts
- **Source Code**: Available as ZIP and TAR.GZ archives
- **Release Notes**: This document
- **Schema Scripts**: SQL Server schema version 96 migration scripts
- **Configuration Templates**: ARM templates and deployment scripts

## Contributors

We thank the following contributors for their work on this release:

### Microsoft FHIR Team
- **John Estrada (@jestradaMS)** - Search parameter synchronization, reindex improvements, and concurrency management
- **Apurva Bhale (@apurvabhaleMS)** - Bulk update enhancements, compartment search fixes, and async query optimization
- **Fernando Henrique Inocêncio Borba Ferreira (@fhibf)** - Import statistics improvements and documentation updates
- **Will Sugarman (@wsugarman)** - Bundle URL construction fixes for forwarded headers
- **Richa Bansal (@rbans96)** - Network service perimeter configuration and infrastructure management
- **Sergey Galuzo (@SergeyGaluzo)** - Performance testing improvements, parameter naming consistency, and documentation
- **Robert Johnson (@LTA-Thinking)** - Code review and technical guidance
- **Isao Yamauchi (@v-isyamauchi-gh)** - Custom search parameter bug fixes and conflict resolution

### Community Contributors
- **Dependabot** - Automated dependency updates for security and performance
- **GitHub Advanced Security** - Automated code scanning and security improvements

### Special Recognition
- **Testing and Quality Assurance**: Multiple team members contributed to comprehensive testing that reduced E2E test execution time by 50%
- **Documentation**: Enhanced architecture documentation and parameter guidance for better developer experience
- **Performance Engineering**: Significant improvements to bulk operations and query optimization

*Total Contributors: 8 Microsoft team members + community contributions*

## Summary

September 2025 was focused on improving system reliability, performance, and fixing critical bugs related to search parameters and bulk operations. Key highlights include:

- **Search Parameter Reliability**: Fixed multiple synchronization and race condition issues
- **Performance Improvements**: Enhanced bulk update operations and async query optimization  
- **Bug Fixes**: Resolved compartment search, URL construction, and parameter enablement issues
- **Infrastructure**: Improved testing performance and deployment reliability

These changes collectively improve the stability, performance, and user experience of the Microsoft FHIR Server across both Azure Health Data Services and Common Platform deployments.

---

*This release note was automatically generated based on merged pull requests and associated work items for September 2025.*