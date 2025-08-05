SET XACT_ABORT ON

GO
CREATE OR ALTER PROCEDURE dbo.GetGeoReplicationLag
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        replication_state_desc,
        replication_lag_sec,            -- seconds behind on the secondary
        last_replication                -- time the secondary hardened last log block
    FROM sys.dm_geo_replication_link_status
    WHERE role_desc = 'PRIMARY';        -- return rows only when this DB is primary
END
GO
