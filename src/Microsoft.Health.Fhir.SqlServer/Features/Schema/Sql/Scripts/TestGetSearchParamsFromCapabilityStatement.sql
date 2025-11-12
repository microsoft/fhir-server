-- Manual Test Script for GetSearchParamsFromCapabilityStatement Function
-- Run this script against a SQL Server database with schema version 100 or later

-- Test 1: Basic functionality with Patient and Observation resources
PRINT 'Test 1: Basic functionality with Patient and Observation resources'
DECLARE @capabilityStatement1 NVARCHAR(MAX) = N'{
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
            },
            {
              "name": "birthdate",
              "definition": "http://hl7.org/fhir/SearchParameter/Patient-birthdate",
              "type": "date"
            }
          ]
        },
        {
          "type": "Observation",
          "searchParam": [
            {
              "name": "code",
              "definition": "http://hl7.org/fhir/SearchParameter/Observation-code",
              "type": "token"
            },
            {
              "name": "patient",
              "definition": "http://hl7.org/fhir/SearchParameter/Observation-patient",
              "type": "reference"
            }
          ]
        }
      ]
    }
  ]
}';

SELECT * FROM dbo.GetSearchParamsFromCapabilityStatement(@capabilityStatement1);
-- Expected: 5 rows (3 for Patient, 2 for Observation)
GO

-- Test 2: Empty rest array
PRINT 'Test 2: Empty rest array'
DECLARE @capabilityStatement2 NVARCHAR(MAX) = N'{
  "resourceType": "CapabilityStatement",
  "rest": []
}';

SELECT * FROM dbo.GetSearchParamsFromCapabilityStatement(@capabilityStatement2);
-- Expected: 0 rows
GO

-- Test 3: Resource with no search parameters
PRINT 'Test 3: Resource with no search parameters'
DECLARE @capabilityStatement3 NVARCHAR(MAX) = N'{
  "resourceType": "CapabilityStatement",
  "rest": [
    {
      "mode": "server",
      "resource": [
        {
          "type": "Patient",
          "searchParam": []
        }
      ]
    }
  ]
}';

SELECT * FROM dbo.GetSearchParamsFromCapabilityStatement(@capabilityStatement3);
-- Expected: 0 rows
GO

-- Test 4: Multiple rest entries (server and client)
PRINT 'Test 4: Multiple rest entries (server and client)'
DECLARE @capabilityStatement4 NVARCHAR(MAX) = N'{
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
            }
          ]
        }
      ]
    },
    {
      "mode": "client",
      "resource": [
        {
          "type": "Observation",
          "searchParam": [
            {
              "name": "code",
              "definition": "http://hl7.org/fhir/SearchParameter/Observation-code",
              "type": "token"
            }
          ]
        }
      ]
    }
  ]
}';

SELECT * FROM dbo.GetSearchParamsFromCapabilityStatement(@capabilityStatement4);
-- Expected: 2 rows (1 for Patient from server mode, 1 for Observation from client mode)
GO

-- Test 5: Filter by resource type
PRINT 'Test 5: Filter by resource type'
DECLARE @capabilityStatement5 NVARCHAR(MAX) = N'{
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
        },
        {
          "type": "Observation",
          "searchParam": [
            {
              "name": "code",
              "definition": "http://hl7.org/fhir/SearchParameter/Observation-code",
              "type": "token"
            }
          ]
        }
      ]
    }
  ]
}';

SELECT * FROM dbo.GetSearchParamsFromCapabilityStatement(@capabilityStatement5)
WHERE ResourceType = 'Patient';
-- Expected: 2 rows (only Patient search parameters)
GO

-- Test 6: Filter by search parameter type
PRINT 'Test 6: Filter by search parameter type'
SELECT * FROM dbo.GetSearchParamsFromCapabilityStatement(@capabilityStatement5)
WHERE SearchParamType = 'token';
-- Expected: 2 rows (Patient-identifier and Observation-code)
GO

-- Test 7: Count search parameters by resource type
PRINT 'Test 7: Count search parameters by resource type'
SELECT 
    ResourceType,
    COUNT(*) as SearchParamCount
FROM dbo.GetSearchParamsFromCapabilityStatement(@capabilityStatement5)
GROUP BY ResourceType;
-- Expected: Patient: 2, Observation: 1
GO

-- Test 8: Get distinct search parameter types
PRINT 'Test 8: Get distinct search parameter types'
DECLARE @capabilityStatement8 NVARCHAR(MAX) = N'{
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
            },
            {
              "name": "birthdate",
              "definition": "http://hl7.org/fhir/SearchParameter/Patient-birthdate",
              "type": "date"
            },
            {
              "name": "general-practitioner",
              "definition": "http://hl7.org/fhir/SearchParameter/Patient-general-practitioner",
              "type": "reference"
            }
          ]
        }
      ]
    }
  ]
}';

SELECT DISTINCT SearchParamType
FROM dbo.GetSearchParamsFromCapabilityStatement(@capabilityStatement8)
ORDER BY SearchParamType;
-- Expected: date, reference, string, token (in alphabetical order)
GO

PRINT 'All tests completed!'
