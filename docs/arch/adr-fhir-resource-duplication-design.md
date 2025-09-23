# FHIR Resource Duplication for Clinical Notes - Design Document

## Overview

This document outlines the design for implementing FHIR US Core 6.1.0 compliance for clinical notes exchange, specifically the requirement to expose overlapping scanned or narrative-only reports through both DocumentReference and DiagnosticReport resources.
https://hl7.org/fhir/us/core/STU6.1/clinical-notes.html#fhir-resources-to-exchange-clinical-notes

## Background

### Problem Statement

The FHIR US Core specification requires that FHIR servers **SHALL** expose overlapping scanned or narrative-only reports through both DiagnosticReport and DocumentReference resources when they contain the same attachment URL. This duplication requirement exists due to inconsistent implementation practices across healthcare systems:

- Some systems store all scanned reports as DocumentReference resources
- Other systems categorize scanned reports by type (e.g., Lab reports as DiagnosticReport)
- This inconsistency makes it difficult for clients to find all clinical information for a patient

### Specification Requirements

When a resource contains a URL reference to an attachment (such as a PDF scan), the server must ensure that attachment is accessible through both resource types using the corresponding elements:

- **DocumentReference**: `content.attachment.url`
- **DiagnosticReport**: `presentedForm.url`

### Example Scenario

If a DiagnosticReport is created with a PDF attachment:
```json
{
  "resourceType": "DiagnosticReport",
  "presentedForm": [
    {
      "contentType": "application/pdf",
      "url": "http://example.org/fhir/Binary/1e404af3-077f-4bee-b7a6-a9be97e1ce32"
    }
  ]
}
```

The server must ensure a corresponding DocumentReference is accessible:
```json
{
  "resourceType": "DocumentReference", 
  "content": [
    {
      "attachment": {
        "contentType": "application/pdf",
        "url": "http://example.org/fhir/Binary/1e404af3-077f-4bee-b7a6-a9be97e1ce32"
      }
    }
  ]
}
```

## Sequence Diagram

```mermaid
sequenceDiagram
    participant Client
    participant FHIR_Server
    participant Storage

    Note over Client,Storage: Scenario: Client uploads DiagnosticReport, then searches for DocumentReference

    Client->>FHIR_Server: POST DiagnosticReport with presentedForm.url
    FHIR_Server->>Storage: Store DiagnosticReport
    Storage-->>FHIR_Server: Success
    
    Note over FHIR_Server: Implementation-specific logic triggered
    Note over FHIR_Server: (Either store duplicate or prepare for search interception)
    
    FHIR_Server-->>Client: 201 Created

    Client->>FHIR_Server: GET DocumentReference?patient=123
    
    alt Implementation 1: Stored Duplicates
        FHIR_Server->>Storage: Query DocumentReference resources
        Storage-->>FHIR_Server: Return stored DocumentReference resources
        Note over FHIR_Server: Includes generated DocumentReference<br/>created from DiagnosticReport
    else Implementation 2: Search Interception  
        FHIR_Server->>Storage: Query DocumentReference resources
        Storage-->>FHIR_Server: Return DocumentReference resources
        FHIR_Server->>Storage: Query DiagnosticReport resources<br/>with presentedForm.url
        Storage-->>FHIR_Server: Return DiagnosticReport resources
        Note over FHIR_Server: Generate DocumentReference<br/>on-the-fly from DiagnosticReport
    end

    FHIR_Server-->>Client: Bundle with DocumentReference resources<br/>(including generated from DiagnosticReport)
```

## Implementation Approaches

### Approach 1: Resource Generation and Storage

#### Description
This approach uses event notifications to automatically generate and store corresponding resources whenever a DocumentReference or DiagnosticReport is created, updated, or deleted.

#### Flow
1. **Create/Update Event**: When a resource with attachment URLs is created/updated:
   - Extract attachment URLs from the source resource
   - Generate corresponding resource of the other type
   - Add metadata linking the generated resource to the original
   - Store the generated resource in the database

2. **Delete Event**: When the original resource is deleted:
   - Find and delete the corresponding generated resource

3. **Search**: Normal search operations return both original and generated resources

#### Implementation Details
- **Linking Metadata**: Add extension or identifier to mark generated resources
- **Sync Mechanism**: Use resource versioning to detect when generated resources need updates
- **Conflict Prevention**: Ensure generated resources don't trigger generation of counter-resources

#### Pros
- ✅ Simple search implementation (no special logic needed)
- ✅ Consistent search performance
- ✅ Standard FHIR resource storage and retrieval
- ✅ Works with existing caching and indexing

#### Cons
- ❌ **Additional Storage**: Doubles storage requirements for affected resources
- ❌ **Sync Issues**: Generated resources may become out of sync with originals
- ❌ **Data Integrity**: Risk of orphaned generated resources
- ❌ **Complexity**: Additional logic for create/update/delete operations

### Approach 2: Search Interception and On-Demand Generation

#### Description
This approach intercepts search operations and dynamically generates resources on-the-fly by cross-searching the other resource type and transforming results.

#### Flow
1. **Search Interception**: When searching for DocumentReference or DiagnosticReport:
   - When a search query comes in, it will be analyzed for any DiagnosticReport or DocumentReference resource type to be included in the results.  If only one of those types is being queried, the other type will be added
    - When the other type is added it will be added with the same search criteria, and in addition, if not already present the new type will also have two more search criteria
      - Must have a patient reference
      - Must have a reference to a url
    - Once the search results come back, the resources in the results will be examined.  Any items in the result set which are of the "other" type will be removed from the search results, and replaced with a dynamically generated resource of the primary type.
      - This dynamically generated resource will not have an ID
      - This dynamically generated resource will have a source property populated with a link to resource that was used to generate it.

#### Example:

```mermaid
sequenceDiagram
    participant Client
    participant FHIR_Service
    participant Storage

    Note over Client,Storage: Search Interception Flow for DocumentReference Query

    Client->>FHIR_Service: GET DocumentReference?patient=123
    
    Note over FHIR_Service: Analyze search query
    Note over FHIR_Service: Rewrite query to include both DocumentReference<br/>and DiagnosticReport with patient reference<br/>and url constraints
    
    FHIR_Service->>Storage: Query (DocumentReference?patient=123) OR<br/>(DiagnosticReport?patient=123&presentedForm.url:exists=true)
    Storage-->>FHIR_Service: Combined result set with both resource types
    
    loop For each DiagnosticReport in results
        Note over FHIR_Service: Remove DiagnosticReport from result set
        Note over FHIR_Service: Generate DocumentReference from DiagnosticReport
        Note over FHIR_Service: Add generated DocumentReference to result set
    end
    
    Note over FHIR_Service: Apply paging and count limits to final result set
    
    FHIR_Service-->>Client: Bundle with DocumentReference resources<br/>(original + generated from DiagnosticReport)
```

2. **Resource Operations**: Normal create/update/delete operations (no special logic)

#### Implementation Details
- **Search Expansion**: Modify search parameters to find equivalent resources
- **Resource Transformation**: Convert between DocumentReference and DiagnosticReport formats
- **Result Merging**: Combine original and generated results while respecting count limits
- **Caching**: Cache generated resources per request to avoid duplicate generation

#### Pros
- ✅ **No Additional Storage**: No duplicate resources stored
- ✅ **Always Synchronized**: Generated resources are always current
- ✅ **Simple Data Model**: No metadata pollution or linking complexity
- ✅ **Flexible**: Can be enabled/disabled without data migration

#### Cons
- ❌ **Performance Impact**: Additional search and transformation overhead
- ❌ **Result Count Management**: Complex handling of page size limits and total counts
- ❌ **Search Complexity**: Complex logic for various search parameters and combinations
- ❌ **Caching Challenges**: Generated resources can't be easily cached across requests

## Technical Considerations

### Configuration
This feature must only be enabled via configuration flag:
- `EnablClinicalReferenceDuplication`: Master switch for the feature

### Patient Context
We only "duplicate" a resource if it also has a patient reference

### Resource Identification
Generated resources need clear identification:
- meta property?
- tag?
- extension?

### Error Handling
- Handle cases where resource generation fails
- Graceful degradation when duplication is temporarily unavailable
- Logging and monitoring for duplication-related issues

### Testing Considerations

#### Paging Scenarios
The search interception approach requires careful testing of pagination scenarios to ensure correct behavior across multiple pages:

1. **Basic Paging**:
   - Test with `_count` parameter to limit results per page
   - Verify `Bundle.total` reflects the correct count after resource transformation
   - Ensure `Bundle.link` navigation URLs work correctly for next/previous pages

2. **Cross-Page Resource Distribution**:
   - Test scenarios where DiagnosticReport resources span multiple pages
   - Verify generated DocumentReference resources appear in correct pages
   - Test edge cases where transformation changes the total page count

3. **Mixed Resource Types**:
   - Test pages containing both original DocumentReference and DiagnosticReport resources
   - Verify transformation maintains proper sort order across pages
   - Test scenarios where all resources on a page are transformed

4. **Large Result Sets**:
   - Test performance with large numbers of resources requiring transformation
   - Verify memory usage remains acceptable during bulk transformations
   - Test timeout scenarios for large result sets

#### Test Cases
- **Empty Results**: Search returning no resources of either type
- **Single Type Results**: Search returning only DocumentReference or only DiagnosticReport
- **Mixed Results**: Search returning both resource types in various combinations
- **Boundary Conditions**: First page, last page, single-item pages
- **Error Scenarios**: Transformation failures, storage errors during cross-type queries
- **Configuration Testing**: Feature enabled/disabled states

## Monitoring
- Metric for everytime a "duplicate" is created/updated

## Optimization
- Caching strategies for generated resources?

## Recommended Approach  (Note this recommendation is from Claude :) )

**Recommendation**: Start with **Approach 2 (Search Interception)** for the following reasons:

1. **Lower Risk**: No data integrity concerns or additional storage requirements
2. **Easier Rollback**: Can be disabled without data cleanup
3. **Better Alignment**: Matches the specification's intent of "exposing" rather than "storing" duplicates
4. **Simpler Testing**: Easier to test and validate without complex data setup

Future migration to Approach 1 could be considered if performance requirements demand it, but the search-based approach provides a solid foundation for initial implementation.
