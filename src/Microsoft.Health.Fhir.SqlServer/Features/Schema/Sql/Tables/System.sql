CREATE TABLE dbo.System
(
    SystemId            int IDENTITY(1,1)           NOT NULL,
    Value               nvarchar(256)               NOT NULL,
)

CREATE UNIQUE CLUSTERED INDEX IXC_System ON dbo.System
(
    Value
)
