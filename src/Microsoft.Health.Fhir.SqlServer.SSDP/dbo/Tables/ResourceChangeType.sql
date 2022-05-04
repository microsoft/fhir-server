/*************************************************************
    Resource change type table
**************************************************************/
CREATE TABLE dbo.ResourceChangeType
(
    ResourceChangeTypeId tinyint NOT NULL,
    Name nvarchar(50) NOT NULL,
    CONSTRAINT PK_ResourceChangeType PRIMARY KEY CLUSTERED (ResourceChangeTypeId),
    CONSTRAINT UQ_ResourceChangeType_Name UNIQUE NONCLUSTERED (Name)
)
ON [PRIMARY]