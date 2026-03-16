# ADR: Not Referenced Search
Labels: [Search](https://github.com/microsoft/fhir-server/labels/Area-Search)

Pull Requests: [Initial Change](https://github.com/microsoft/fhir-server/pull/4856)

## Context 
   This search parameter is needed to help users find resources that are in a bad state by not being referenced. Many users have data usage paterns that require resources to have specific connections to other resources. If a resource is missing these connections it can be hard to find and fix. This search parameter gives users a tool to help find these resources.

## Decision
   The descision was made to have the value for the search parameter be `*:*`, for example `/Patient?_not-referenced=*:*` to aline with the patern used by the FHIR standard for `_include` and `_revinclude`. While `_not-referenced` currently doestn't support other values, this gives us the option to expand the functionality in the future without a breaking change.
   In theory this parameter could be enhanced to take in any specific reference, like `_revinclude` does. The format would then be `<Resource Type>:<Search Parameter>`, for example `/Patient?_not-referenced=Observation:subject`. This would allow for a search for Patients that aren't referenced by an Observation in the subject field. 

## Status
   Accepted

## Consequences
   This search parameter will be available for all resources. There are no forseen consequences of this change as it is new functionality.
