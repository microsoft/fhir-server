

/***********************************************************************
 NOTE: just checking first object, since this is run in transaction
***************************************************************************/
IF EXISTS (
    SELECT *
    FROM sys.tables
    WHERE name = 'ClaimType')
BEGIN
    ROLLBACK TRANSACTION
    RETURN
END

/*************************************************************
    Schema Version - Make sure to update the version here for new migration
**************************************************************/
Go

INSERT INTO dbo.SchemaVersion
VALUES
    (44, 'started')

Go
