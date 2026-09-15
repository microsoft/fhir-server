

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

IF NOT EXISTS
    (
        SELECT 1
        FROM sys.types
        WHERE name = 'vector'
          AND is_user_defined = 0
    )
    THROW 50419, 'This schema requires native vector type support. Use Azure SQL Database or SQL Server 2025 or later.', 1

GO
