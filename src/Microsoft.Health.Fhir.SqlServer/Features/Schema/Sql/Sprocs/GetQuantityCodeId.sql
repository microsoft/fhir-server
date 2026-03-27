GO
CREATE PROCEDURE dbo.GetQuantityCodeId
    @stringValue nvarchar(255)
AS

SET TRANSACTION ISOLATION LEVEL SERIALIZABLE
BEGIN TRANSACTION

DECLARE @id int = (SELECT QuantityCodeId FROM dbo.QuantityCode WITH (UPDLOCK) WHERE Value = @stringValue)

IF (@id IS NULL) BEGIN
    INSERT INTO dbo.QuantityCode 
        (Value)
    VALUES 
        (@stringValue)
    SET @id = SCOPE_IDENTITY()
END

COMMIT TRANSACTION

SELECT @id
GO
