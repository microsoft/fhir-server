-- Enable RCSI
ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON
GO

-- Create Resource table
IF NOT EXISTS ( SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SchemaVersion' )
BEGIN
    CREATE TABLE SchemaVersion (
        [Version] int PRIMARY KEY, 
        [Status] varchar(10) 
    )
END
GO

CREATE PROCEDURE dbo.SelectCurrentSchemaVersion
AS BEGIN
    SELECT
        MAX([Version])
    FROM SchemaVersion 
    WHERE [Status] = 'complete'
END
GO

CREATE PROCEDURE dbo.UpsertSchemaVersion(
        @Version int,
        @Status varchar(10) 
    )
AS BEGIN
    IF EXISTS(SELECT * FROM SchemaVersion WHERE [Version] = @Version)
    BEGIN
        UPDATE SchemaVersion
        SET	[Status] = @Status
        WHERE [Version] = @Version
    END
    ELSE
    BEGIN
        INSERT INTO SchemaVersion ([Version], [Status])
        VALUES(@Version, @Status)
    END
END
GO

INSERT INTO SchemaVersion
VALUES (1, 'complete')
GO
