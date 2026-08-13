IF NOT EXISTS
(
    SELECT 1
    FROM sys.database_principals
    WHERE name = N'FhirDiagnosticsReader'
      AND type = 'R'
)
BEGIN
    IF EXISTS
    (
        SELECT 1
        FROM sys.database_principals
        WHERE name = N'FhirDiagnosticsReader'
    )
    BEGIN
        THROW 50100, 'A database principal named FhirDiagnosticsReader already exists but is not a database role.', 1;
    END

    CREATE ROLE [FhirDiagnosticsReader];
END
GO

GRANT EXECUTE ON dbo.GetQueryStoreSlowQueries TO [FhirDiagnosticsReader];
GRANT EXECUTE ON dbo.GetQueryStorePlanDiagnostics TO [FhirDiagnosticsReader];
GRANT EXECUTE ON dbo.GetStatisticsHealth TO [FhirDiagnosticsReader];
GO
