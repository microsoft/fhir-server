CREATE OR ALTER PROCEDURE dbo.GetQueryStoreDiagnosticsState
AS
BEGIN
    SET NOCOUNT ON;

    SELECT actual_state_desc, readonly_reason
    FROM sys.database_query_store_options;
END
GO
