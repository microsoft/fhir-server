GO
CREATE PROCEDURE dbo.InitializeBase
     @searchParams nvarchar(max)
    ,@resourceTypes nvarchar(3000)
    ,@claimTypes varchar(100)
    ,@compartmentTypes varchar(100)
AS
SET XACT_ABORT ON
BEGIN TRANSACTION

INSERT INTO dbo.ResourceType (Name)
SELECT value FROM string_split(@resourceTypes, ',')
EXCEPT SELECT Name FROM dbo.ResourceType WITH (TABLOCKX);

-- result set 1
SELECT ResourceTypeId, Name FROM dbo.ResourceType;

;WITH Input AS (
    SELECT DISTINCT
            j.Uri,
            CAST(j.IsPartiallySupported AS bit) AS IsPartiallySupported
    FROM OPENJSON(@searchParams)
    WITH (Uri varchar(128) '$.Uri', IsPartiallySupported bit '$.IsPartiallySupported') AS j
)
INSERT dbo.SearchParam (Uri, Status, LastUpdated, IsPartiallySupported)
SELECT i.Uri, 'Initialized', SYSDATETIMEOFFSET(), i.IsPartiallySupported
FROM Input AS i
WHERE NOT EXISTS (SELECT 1 FROM dbo.SearchParam AS sp WHERE sp.Uri = i.Uri);

-- result set 2
SELECT Uri, SearchParamId FROM dbo.SearchParam;

INSERT INTO dbo.ClaimType (Name)
SELECT value FROM string_split(@claimTypes, ',')
EXCEPT SELECT Name FROM dbo.ClaimType;

-- result set 3
SELECT ClaimTypeId, Name FROM dbo.ClaimType;

INSERT INTO dbo.CompartmentType (Name)
SELECT value FROM string_split(@compartmentTypes, ',')
EXCEPT SELECT Name FROM dbo.CompartmentType;

-- result set 4
SELECT CompartmentTypeId, Name FROM dbo.CompartmentType;
                        
COMMIT TRANSACTION
    
-- result set 5
SELECT Value, SystemId from dbo.System;

-- result set 6
SELECT Value, QuantityCodeId FROM dbo.QuantityCode
GO
