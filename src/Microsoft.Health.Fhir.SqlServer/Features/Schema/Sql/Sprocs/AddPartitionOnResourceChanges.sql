--
-- STORED PROCEDURE
--     AddPartitionOnResourceChanges
--
-- DESCRIPTION
--     Creates a new partition at the right for the future date which will be
--     the next hour of the right-most partition boundry.
--
-- PARAMETERS
--     @partitionBoundary
--         * The output parameter to stores the added partition boundary.
--
CREATE OR ALTER PROCEDURE dbo.AddPartitionOnResourceChanges
    @partitionBoundary datetime2(7) OUTPUT
AS
BEGIN
	
	/* using XACT_ABORT to force a rollback on any error. */
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
	
	/* Finds the highest boundary value */
    DECLARE @rightPartitionBoundary AS DATETIME2 (7) = CAST ((SELECT   TOP (1) value
                                                              FROM     sys.partition_range_values AS prv
                                                                       INNER JOIN
                                                                       sys.partition_functions AS pf
                                                                       ON pf.function_id = prv.function_id
                                                              WHERE    pf.name = N'PartitionFunction_ResourceChangeData_Timestamp'
                                                              ORDER BY prv.boundary_id DESC) AS DATETIME2 (7));
															  
	/* Rounds the current datetime to the hour. */
    DECLARE @timestamp AS DATETIME2 (7) = DATEADD(hour, DATEDIFF(hour, 0, sysutcdatetime()), 0);
    
	/* Ensures the next boundary value is greater than the current datetime. */
	IF (@rightPartitionBoundary < @timestamp)
        BEGIN
            SET @rightPartitionBoundary = @timestamp;
        END
    
	/* Adds one hour for the next partition. */
	SET @rightPartitionBoundary = DATEADD(hour, 1, @rightPartitionBoundary);
    
	/* Creates new empty partition by creating new boundary value and specifying NEXT USED file group. */
	ALTER PARTITION SCHEME PartitionScheme_ResourceChangeData_Timestamp NEXT USED [Primary];
    ALTER PARTITION FUNCTION PartitionFunction_ResourceChangeData_Timestamp( )
        SPLIT RANGE (@rightPartitionBoundary);
    SET @partitionBoundary = @rightPartitionBoundary;
    COMMIT TRANSACTION;
END
GO
