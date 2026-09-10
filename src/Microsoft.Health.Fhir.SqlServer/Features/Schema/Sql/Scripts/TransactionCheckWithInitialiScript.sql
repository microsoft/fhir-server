

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

GO

DECLARE @ProductMajorVersion int = TRY_CONVERT(int, SERVERPROPERTY('ProductMajorVersion'))
DECLARE @EngineEdition int = TRY_CONVERT(int, SERVERPROPERTY('EngineEdition'))

IF ISNULL(@ProductMajorVersion, 0) < 17 AND ISNULL(@EngineEdition, 0) NOT IN (5, 8)
    THROW 50419, 'This schema requires SQL Server 2025, Azure SQL Database, or Azure SQL Managed Instance with native vector support.', 1

GO
