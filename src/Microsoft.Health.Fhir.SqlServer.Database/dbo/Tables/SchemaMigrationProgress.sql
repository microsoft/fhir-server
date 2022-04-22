CREATE TABLE [dbo].[SchemaMigrationProgress] (
    [Timestamp] DATETIME2 (3)  DEFAULT (getdate()) NULL,
    [Message]   NVARCHAR (MAX) NULL
);

