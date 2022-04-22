
CREATE PROCEDURE dbo.SelectCompatibleSchemaVersions
AS
BEGIN
    SET NOCOUNT ON

    SELECT MAX(MinVersion), MIN(MaxVersion)
    FROM dbo.InstanceSchema
    WHERE Timeout > SYSUTCDATETIME()
END
