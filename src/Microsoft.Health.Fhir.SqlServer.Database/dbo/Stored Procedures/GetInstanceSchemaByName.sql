
CREATE PROCEDURE dbo.GetInstanceSchemaByName
    @name varchar(64)
AS
    SET NOCOUNT ON

    SELECT CurrentVersion, MaxVersion, MinVersion, Timeout
    FROM dbo.InstanceSchema
    WHERE Name = @name
