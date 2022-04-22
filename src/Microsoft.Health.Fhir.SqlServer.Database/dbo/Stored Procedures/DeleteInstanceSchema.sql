
CREATE PROCEDURE dbo.DeleteInstanceSchema   
AS
    SET NOCOUNT ON

    DELETE FROM dbo.InstanceSchema
    WHERE Timeout < SYSUTCDATETIME()

