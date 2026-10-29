CREATE OR ALTER PROCEDURE dbo.GetQueryStoreDiagnosticsPlans
    @PlanIds nvarchar(max)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT plan_id, query_plan
    FROM sys.query_store_plan
    WHERE plan_id IN
    (
        SELECT CONVERT(bigint, [value])
        FROM STRING_SPLIT(@PlanIds, ',')
    );
END
GO
