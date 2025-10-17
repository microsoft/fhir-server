-- Add index on LastUpdated column for SearchParam table to improve query performance
-- Only create if it doesn't already exist
IF NOT EXISTS (
    SELECT 1 
    FROM sys.indexes 
    WHERE object_id = OBJECT_ID('dbo.SearchParam') 
    AND name = 'IX_SearchParam_LastUpdated'
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_SearchParam_LastUpdated
    ON dbo.SearchParam (LastUpdated)
    WITH (DATA_COMPRESSION = PAGE)
END
GO
