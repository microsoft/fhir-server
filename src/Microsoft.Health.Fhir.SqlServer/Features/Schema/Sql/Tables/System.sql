CREATE TABLE dbo.System
(
    SystemId            int IDENTITY(1,1)           NOT NULL,
    CONSTRAINT UQ_System_SystemId UNIQUE (SystemId),
    Value               nvarchar(256)               NOT NULL,
    CONSTRAINT PKC_System PRIMARY KEY CLUSTERED (Value)
    WITH (DATA_COMPRESSION = PAGE)
)
