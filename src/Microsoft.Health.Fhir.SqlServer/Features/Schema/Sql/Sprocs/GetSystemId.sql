GO
CREATE PROCEDURE dbo.GetSystemId
    @stringValue nvarchar(255)
AS

SET TRANSACTION ISOLATION LEVEL SERIALIZABLE
BEGIN TRANSACTION

DECLARE @id int = (SELECT SystemId FROM dbo.System WITH (UPDLOCK) WHERE Value = @stringValue)

IF (@id IS NULL) BEGIN
    INSERT INTO dbo.System 
        (Value)
    VALUES 
        (@stringValue)
    SET @id = SCOPE_IDENTITY()
END

COMMIT TRANSACTION

SELECT @id
GO
