
CREATE PROCEDURE dbo.SelectCurrentSchemaVersion
AS
BEGIN
	SET NOCOUNT ON

	SELECT MAX(Version)
	FROM SchemaVersion
	WHERE Status = 'complete' OR Status = 'completed'
END
