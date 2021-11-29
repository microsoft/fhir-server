CREATE TABLE dbo.ResourceType
(
    ResourceTypeId          smallint IDENTITY(1,1)          NOT NULL,
    Name                    nvarchar(50)                    COLLATE Latin1_General_100_CS_AS  NOT NULL
)

CREATE UNIQUE CLUSTERED INDEX IXC_ResourceType on dbo.ResourceType
(
    Name
)
