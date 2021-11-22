CREATE PROCEDURE dbo.LogSchemaMigrationProgress
    @message varchar(max)
AS
    INSERT INTO dbo.SchemaMigrationProgress (Message) VALUES (@message)
GO
