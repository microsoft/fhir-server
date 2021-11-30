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
GO

INSERT dbo.ResourceChangeType (ResourceChangeTypeId, Name) VALUES (0, N'Creation')
INSERT dbo.ResourceChangeType (ResourceChangeTypeId, Name) VALUES (1, N'Update')
INSERT dbo.ResourceChangeType (ResourceChangeTypeId, Name) VALUES (2, N'Deletion')
GO
