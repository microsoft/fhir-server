CREATE TABLE dbo.ResourceType
(
    ResourceTypeId          smallint IDENTITY(1,1)          NOT NULL,
    CONSTRAINT UQ_ResourceType_ResourceTypeId UNIQUE (ResourceTypeId),
    Name                    nvarchar(50)                    COLLATE Latin1_General_100_CS_AS  NOT NULL,
    CONSTRAINT PKC_ResourceType PRIMARY KEY CLUSTERED (Name)
    WITH (DATA_COMPRESSION = PAGE)
)
