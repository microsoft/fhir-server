/*************************************************************
    Function: GetSearchParamsFromCapabilityStatement
    
    Description:
        Extracts search parameter information (resource type, URL, and type) 
        from a FHIR Capability Statement JSON document.
        
    Parameters:
        @capabilityStatementJson - NVARCHAR(MAX): The FHIR Capability Statement in JSON format
        
    Returns:
        Table with the following columns:
            - ResourceType: The FHIR resource type (e.g., 'Patient', 'Observation')
            - SearchParamUrl: The canonical URL of the search parameter
            - SearchParamType: The type of the search parameter (e.g., 'string', 'token', 'reference')
            
    Usage Example:
        SELECT * FROM dbo.GetSearchParamsFromCapabilityStatement(
            '{"resourceType":"CapabilityStatement","rest":[{"mode":"server","resource":[{"type":"Patient","searchParam":[{"name":"identifier","definition":"http://hl7.org/fhir/SearchParameter/Patient-identifier","type":"token"}]}]}]}'
        )
**************************************************************/

CREATE OR ALTER FUNCTION dbo.GetSearchParamsFromCapabilityStatement
(
    @capabilityStatementJson NVARCHAR(MAX)
)
RETURNS TABLE
AS
RETURN
(
    SELECT 
        resource.ResourceType,
        searchParam.SearchParamUrl,
        searchParam.SearchParamType
    FROM OPENJSON(@capabilityStatementJson, '$.rest') AS rest
    CROSS APPLY OPENJSON(rest.value, '$.resource') 
        WITH (
            ResourceType NVARCHAR(128) '$.type',
            SearchParams NVARCHAR(MAX) '$.searchParam' AS JSON
        ) AS resource
    CROSS APPLY OPENJSON(resource.SearchParams)
        WITH (
            SearchParamUrl NVARCHAR(512) '$.definition',
            SearchParamType NVARCHAR(64) '$.type'
        ) AS searchParam
    WHERE 
        resource.ResourceType IS NOT NULL
        AND searchParam.SearchParamUrl IS NOT NULL
        AND searchParam.SearchParamType IS NOT NULL
)
GO
