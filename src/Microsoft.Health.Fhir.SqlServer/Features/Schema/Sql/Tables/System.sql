CREATE TABLE dbo.System
(
    SystemId            int IDENTITY(1,1)           NOT NULL,
    CONSTRAINT PK_System PRIMARY KEY NONCLUSTERED (SystemId),
    Value               nvarchar(256)               NOT NULL,
)

CREATE UNIQUE CLUSTERED INDEX IXC_System ON dbo.System
(
    Value
)
