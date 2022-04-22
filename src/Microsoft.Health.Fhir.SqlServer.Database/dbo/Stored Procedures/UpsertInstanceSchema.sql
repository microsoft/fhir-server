
CREATE PROCEDURE dbo.UpsertInstanceSchema
    @name varchar(64),
    @maxVersion int,
    @minVersion int,
    @addMinutesOnTimeout int   
AS
    SET NOCOUNT ON

    DECLARE @timeout datetime2(0) = DATEADD(minute, @addMinutesOnTimeout, SYSUTCDATETIME())
    DECLARE @currentVersion int = (SELECT COALESCE(MAX(Version), 0)
                                  FROM dbo.SchemaVersion
                                  WHERE  Status = 'completed' OR Status = 'complete' AND Version <= @maxVersion)
    IF EXISTS(SELECT *
        FROM dbo.InstanceSchema
        WHERE Name = @name)
    BEGIN
        UPDATE dbo.InstanceSchema
        SET CurrentVersion = @currentVersion, MaxVersion = @maxVersion, Timeout = @timeout
        WHERE Name = @name
        
        SELECT @currentVersion
    END
    ELSE
    BEGIN
        INSERT INTO dbo.InstanceSchema
            (Name, CurrentVersion, MaxVersion, MinVersion, Timeout)
        VALUES
            (@name, @currentVersion, @maxVersion, @minVersion, @timeout)

        SELECT @currentVersion
    END
