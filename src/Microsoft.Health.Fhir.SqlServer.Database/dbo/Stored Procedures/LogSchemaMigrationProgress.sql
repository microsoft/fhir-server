CREATE PROCEDURE dbo.LogSchemaMigrationProgress
@message VARCHAR (MAX)
AS
INSERT  INTO dbo.SchemaMigrationProgress (Message)
VALUES                                  (@message);

