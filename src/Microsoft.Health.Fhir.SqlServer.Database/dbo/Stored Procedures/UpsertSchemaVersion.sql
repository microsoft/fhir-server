
CREATE PROCEDURE dbo.UpsertSchemaVersion
    @version int,
    @status varchar(10)
AS
    SET NOCOUNT ON

    IF EXISTS(SELECT *
        FROM dbo.SchemaVersion
        WHERE Version = @version)
    BEGIN
        UPDATE dbo.SchemaVersion
        SET Status = @status
        WHERE Version = @version
    END
    ELSE
    BEGIN
        INSERT INTO dbo.SchemaVersion
            (Version, Status)
        VALUES
            (@version, @status)
    END
