/*************************************************************
    Stored procedures for capturing and fetching resource changes
**************************************************************/
--
-- STORED PROCEDURE
--     CaptureResourceChanges
--
-- DESCRIPTION
--     Inserts resource change data
--
-- PARAMETERS
--     @isDeleted
--         * Whether this resource marks the resource as deleted.
--     @version
--         * The version of the resource being written
--     @resourceId
--         * The resource ID
--     @resourceTypeId
--         * The ID of the resource type
--
-- RETURN VALUE
--     It does not return a value.
--
CREATE PROCEDURE dbo.CaptureResourceChanges
    @isDeleted bit,
    @version int,
    @resourceId varchar(64),
    @resourceTypeId smallint
AS
BEGIN
	/* The CaptureResourceChanges procedure is intended to be called from
       the UpsertResource_5 procedure, so it does not begin a new transaction here. */
	DECLARE @changeType AS SMALLINT;
    IF (@isDeleted = 1)
        BEGIN
            SET @changeType = 2; /* DELETION */
        END
    ELSE
        BEGIN
            IF (@version = 1)
                BEGIN
                    SET @changeType = 0; /* CREATION */
                END
            ELSE
                BEGIN
                    SET @changeType = 1; /* UPDATE */
                END
        END
    INSERT  INTO dbo.ResourceChangeData (ResourceId, ResourceTypeId, ResourceVersion, ResourceChangeTypeId)
    VALUES                             (@resourceId, @resourceTypeId, @version, @changeType);
END
GO
