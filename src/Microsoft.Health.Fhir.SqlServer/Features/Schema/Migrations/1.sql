-- Enable RCSI
ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON
GO

IF NOT EXISTS(SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SchemaVersion' AND TABLE_SCHEMA = 'dbo')
BEGIN
    CREATE TABLE dbo.SchemaVersion (
        [Version] int PRIMARY KEY, 
        [Status] varchar(10) 
    )
END
GO

INSERT INTO dbo.SchemaVersion
VALUES (1, 'started')
GO

CREATE PROCEDURE dbo.SelectCurrentSchemaVersion
AS BEGIN
    SELECT MAX([Version])
    FROM dbo.SchemaVersion 
    WHERE [Status] = 'complete'
END
GO

CREATE PROCEDURE dbo.UpsertSchemaVersion(
        @version int,
        @status varchar(10) 
    )
AS BEGIN
    IF EXISTS(SELECT * FROM dbo.SchemaVersion WHERE [Version] = @version)
    BEGIN
        UPDATE dbo.SchemaVersion
        SET [Status] = @status
        WHERE [Version] = @version
    END
    ELSE
    BEGIN
        INSERT INTO dbo.SchemaVersion ([Version], [Status])
        VALUES (@version, @status)
    END
END
GO
