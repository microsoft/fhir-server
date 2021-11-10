/***********************************************************************
 NOTE: just checking first object, since this is run in transaction
***************************************************************************/
IF EXISTS (
    SELECT *
    FROM sys.tables
    WHERE name = 'Instance')
BEGIN
    ROLLBACK TRANSACTION
    RETURN
END
