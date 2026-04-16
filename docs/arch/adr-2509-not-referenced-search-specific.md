# Not Referenced Search on Specific Fields

## Context 
   Not referenced search currently only supports checking if a resource isn't referenced by any other resource. A need has been identified to search for resources that aren't referenced by specific fields. 
   
   For example: Find all Patients that don't have an Encounter listing them as a subject.

## Decision
   We will enhance not referenced search to support checking against specific references. This will be done by using the same syntax as `_include`.

   `<fhir endpoint>/Patient?_not-referenced=Encounter:subject`

   This allows us to maintain the existing syntax of `*:*` for wildcard searches and keeps this custom search feature in line with FHIR spec search features.

## Status
   Accepted

## Consequences
   There are no consequences to this decision, it is new functionality that maintains all existing functionality.
