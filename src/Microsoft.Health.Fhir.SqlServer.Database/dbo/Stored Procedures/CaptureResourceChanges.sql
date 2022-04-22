CREATE PROCEDURE dbo.CaptureResourceChanges
@isDeleted BIT, @version INT, @resourceId VARCHAR (64), @resourceTypeId SMALLINT
AS
BEGIN
    DECLARE @changeType AS SMALLINT;
    IF (@isDeleted = 1)
        BEGIN
            SET @changeType = 2;
        END
    ELSE
        BEGIN
            IF (@version = 1)
                BEGIN
                    SET @changeType = 0;
                END
            ELSE
                BEGIN
                    SET @changeType = 1;
                END
        END
    INSERT  INTO dbo.ResourceChangeData (ResourceId, ResourceTypeId, ResourceVersion, ResourceChangeTypeId)
    VALUES                             (@resourceId, @resourceTypeId, @version, @changeType);
END

