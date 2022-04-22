
CREATE PROCEDURE dbo.SelectCurrentVersionsInformation
AS
BEGIN
    SET NOCOUNT ON

    SELECT SV.Version, SV.Status, STRING_AGG(SCH.NAME, ',')
    FROM dbo.SchemaVersion AS SV LEFT OUTER JOIN dbo.InstanceSchema AS SCH
    ON SV.Version = SCH.CurrentVersion
    GROUP BY Version, Status
END
