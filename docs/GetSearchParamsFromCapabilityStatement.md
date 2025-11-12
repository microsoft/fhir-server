# GetSearchParamsFromCapabilityStatement SQL Function

## Overview

The `GetSearchParamsFromCapabilityStatement` function is a SQL Server table-valued function that extracts search parameter information from a FHIR Capability Statement JSON document.

## Function Signature

```sql
CREATE OR ALTER FUNCTION dbo.GetSearchParamsFromCapabilityStatement
(
    @capabilityStatementJson NVARCHAR(MAX)
)
RETURNS TABLE
```

## Parameters

- **@capabilityStatementJson** (NVARCHAR(MAX)): A FHIR Capability Statement in JSON format

## Returns

A table with the following columns:

| Column Name | Data Type | Description |
|------------|-----------|-------------|
| ResourceType | NVARCHAR(128) | The FHIR resource type (e.g., 'Patient', 'Observation') |
| SearchParamUrl | NVARCHAR(512) | The canonical URL of the search parameter |
| SearchParamType | NVARCHAR(64) | The type of the search parameter (e.g., 'string', 'token', 'reference') |

## Usage Examples

### Example 1: Basic Usage

```sql
DECLARE @capabilityStatement NVARCHAR(MAX) = N'{
  "resourceType": "CapabilityStatement",
  "rest": [
    {
      "mode": "server",
      "resource": [
        {
          "type": "Patient",
          "searchParam": [
            {
              "name": "identifier",
              "definition": "http://hl7.org/fhir/SearchParameter/Patient-identifier",
              "type": "token"
            },
            {
              "name": "name",
              "definition": "http://hl7.org/fhir/SearchParameter/Patient-name",
              "type": "string"
            }
          ]
        }
      ]
    }
  ]
}';

SELECT * FROM dbo.GetSearchParamsFromCapabilityStatement(@capabilityStatement);
```

**Output:**

| ResourceType | SearchParamUrl | SearchParamType |
|-------------|----------------|-----------------|
| Patient | http://hl7.org/fhir/SearchParameter/Patient-identifier | token |
| Patient | http://hl7.org/fhir/SearchParameter/Patient-name | string |

### Example 2: Filtering Results

```sql
-- Get only token-type search parameters
SELECT * 
FROM dbo.GetSearchParamsFromCapabilityStatement(@capabilityStatement)
WHERE SearchParamType = 'token';

-- Get search parameters for a specific resource type
SELECT * 
FROM dbo.GetSearchParamsFromCapabilityStatement(@capabilityStatement)
WHERE ResourceType = 'Patient';
```

### Example 3: Joining with Other Tables

```sql
-- Join with SearchParam table to find matching search parameter IDs
SELECT 
    sp.SearchParamId,
    caps.ResourceType,
    caps.SearchParamUrl,
    caps.SearchParamType
FROM dbo.GetSearchParamsFromCapabilityStatement(@capabilityStatement) caps
INNER JOIN dbo.SearchParam sp ON sp.Uri = caps.SearchParamUrl;
```

### Example 4: Reading from Resource Table

If the Capability Statement is stored in the Resource table:

```sql
-- Extract search parameters from a stored CapabilityStatement resource
DECLARE @capabilityJson NVARCHAR(MAX);

SELECT @capabilityJson = CONVERT(NVARCHAR(MAX), RawResource)
FROM dbo.Resource r
INNER JOIN dbo.ResourceType rt ON r.ResourceTypeId = rt.ResourceTypeId
WHERE rt.Name = 'CapabilityStatement'
  AND r.IsHistory = 0
  AND r.IsDeleted = 0;

SELECT * FROM dbo.GetSearchParamsFromCapabilityStatement(@capabilityJson);
```

## Implementation Details

The function uses SQL Server's `OPENJSON` to parse the nested JSON structure:

1. Parses the `rest` array from the Capability Statement
2. For each rest entry, parses the `resource` array
3. For each resource, extracts the resource type and parses the `searchParam` array
4. For each search parameter, extracts the definition (URL) and type
5. Filters out any rows where ResourceType, SearchParamUrl, or SearchParamType is NULL

## Notes

- The function requires SQL Server 2016 or later (for OPENJSON support)
- The function handles multiple `rest` entries in the Capability Statement
- The function handles multiple resources per rest entry
- The function handles multiple search parameters per resource
- NULL values in any of the required fields (type, definition, or type) are filtered out

## Schema Version

This function was introduced in schema version 100.

## See Also

- [FHIR Capability Statement Specification](http://hl7.org/fhir/capabilitystatement.html)
- [FHIR Search Parameters](http://hl7.org/fhir/searchparameter.html)
- [SQL Server OPENJSON](https://docs.microsoft.com/en-us/sql/t-sql/functions/openjson-transact-sql)
