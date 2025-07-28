/*************************************************************
    Stored procedures - GetGeoReplicationLag
**************************************************************/
--
-- STORED PROCEDURE
--     GetGeoReplicationLag
--
-- DESCRIPTION
--     Retrieves geo-replication lag information for the primary database.
--     This procedure monitors the replication status and lag time between
--     the primary and secondary replicas in an Azure SQL Database geo-replication setup.
--
-- PARAMETERS
--     None
--
-- RETURN VALUE
--     Returns a result set containing:
--     - replication_state_desc: Description of the current replication state
--     - replication_lag_sec: Number of seconds the secondary is behind the primary
--     - last_replication: Timestamp when the secondary last hardened a log block
--
--     Only returns data when the current database is acting as the primary replica.
--
CREATE OR ALTER PROCEDURE dbo.GetGeoReplicationLag
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        SELECT
            replication_state_desc,
            replication_lag_sec,            -- seconds behind on the secondary
            last_replication                -- time the secondary hardened last log block
        FROM sys.dm_geo_replication_link_status
        WHERE role_desc = 'PRIMARY';        -- return rows only when this DB is primary
    END TRY
    BEGIN CATCH
        -- For local database, sys.dm_geo_replication_link_status table does not exist
        -- Return same columns but no rows (empty result set) to avoid failure
        SELECT
            CAST(NULL AS NVARCHAR(256))  AS replication_state_desc,
            CAST(NULL AS INT)            AS replication_lag_sec,
            CAST(NULL AS DATETIMEOFFSET)   AS last_replication
        WHERE 1 = 0;
    END CATCH
    END
GO
