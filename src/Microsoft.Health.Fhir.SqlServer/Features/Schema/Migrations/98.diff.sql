IF NOT EXISTS (
    SELECT 1 
    FROM sys.indexes 
    WHERE object_id = OBJECT_ID('dbo.SearchParam') 
    AND name = 'IX_LastUpdated'
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_LastUpdated
    ON dbo.SearchParam (LastUpdated)
END
GO
