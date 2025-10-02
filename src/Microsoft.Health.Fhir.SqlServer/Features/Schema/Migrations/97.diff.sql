/*************************************************************
    Schema Version 97: Compartment Search Performance Optimization

    Add covering index to ReferenceSearchParam table optimized for
    compartment query patterns (e.g., /Patient/123/[all resources] queries).

    This index enables:
    - Faster compartment searches (3-5x improvement)
    - Eliminates key lookups to clustered index
    - Optimized column order for WHERE clause predicates

    Related: ADR 2510 - Compartment Search Performance Optimization
**************************************************************/

-- Create covering index for compartment search optimization
-- ONLINE = ON ensures non-blocking index creation
-- RESUMABLE = ON allows pausing/resuming if needed (SQL Server 2017+)
--   - Can pause with: ALTER INDEX IX_ReferenceSearchParam_Compartment ON dbo.ReferenceSearchParam PAUSE;
--   - Can resume with: ALTER INDEX IX_ReferenceSearchParam_Compartment ON dbo.ReferenceSearchParam RESUME;
--   - Survives failures and can be resumed instead of restarting from scratch
--   - Note: RESUMABLE and SORT_IN_TEMPDB cannot both be ON, so we prioritize RESUMABLE
CREATE NONCLUSTERED INDEX IX_ReferenceSearchParam_Compartment
ON dbo.ReferenceSearchParam
(
    ReferenceResourceId,           -- Compartment ID (e.g., "123")
    ReferenceResourceTypeId,       -- Compartment type (e.g., Patient=1)
    SearchParamId                  -- Reference parameter (e.g., subject, patient)
)
INCLUDE (ResourceTypeId, ResourceSurrogateId)  -- Covering columns for SELECT
WITH (
    DATA_COMPRESSION = PAGE,       -- Compress index to save space
    ONLINE = ON,                   -- Non-blocking: allows queries during creation
    RESUMABLE = ON                 -- Allow pause/resume (SQL Server 2017+)
)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
GO
