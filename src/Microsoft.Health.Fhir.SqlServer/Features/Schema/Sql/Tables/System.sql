CREATE TABLE dbo.System
(
    SystemId            int IDENTITY(1,1)           NOT NULL,
    CONSTRAINT UQ_System UNIQUE (SystemId),
    Value               nvarchar(256)               NOT NULL,
    CONSTRAINT PKC_System PRIMARY KEY CLUSTERED (Value),
)
