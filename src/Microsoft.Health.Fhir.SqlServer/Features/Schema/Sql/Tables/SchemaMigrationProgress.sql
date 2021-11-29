CREATE TABLE dbo.SchemaMigrationProgress
(
    Timestamp       datetime2(3)       default CURRENT_TIMESTAMP,
    Message         nvarchar(max)
)
