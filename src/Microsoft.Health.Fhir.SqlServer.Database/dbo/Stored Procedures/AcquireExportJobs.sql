CREATE PROCEDURE dbo.AcquireExportJobs
@jobHeartbeatTimeoutThresholdInSeconds BIGINT, @maximumNumberOfConcurrentJobsAllowed INT
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;
DECLARE @expirationDateTime AS DATETIME2 (7);
SELECT @expirationDateTime = DATEADD(second, -@jobHeartbeatTimeoutThresholdInSeconds, SYSUTCDATETIME());
DECLARE @numberOfRunningJobs AS INT;
SELECT @numberOfRunningJobs = COUNT(*)
FROM   dbo.ExportJob WITH (TABLOCKX)
WHERE  Status = 'Running'
       AND HeartbeatDateTime > @expirationDateTime;
DECLARE @limit AS INT = @maximumNumberOfConcurrentJobsAllowed - @numberOfRunningJobs;
IF (@limit > 0)
    BEGIN
        DECLARE @availableJobs TABLE (
            Id         VARCHAR (64) COLLATE Latin1_General_100_CS_AS NOT NULL,
            JobVersion BINARY (8)   NOT NULL);
        INSERT INTO @availableJobs
        SELECT   TOP (@limit) Id,
                              JobVersion
        FROM     dbo.ExportJob
        WHERE    (Status = 'Queued'
                  OR (Status = 'Running'
                      AND HeartbeatDateTime <= @expirationDateTime))
        ORDER BY HeartbeatDateTime;
        DECLARE @heartbeatDateTime AS DATETIME2 (7) = SYSUTCDATETIME();
        UPDATE dbo.ExportJob
        SET    Status            = 'Running',
               HeartbeatDateTime = @heartbeatDateTime,
               RawJobRecord      = JSON_MODIFY(RawJobRecord, '$.status', 'Running')
        OUTPUT inserted.RawJobRecord, inserted.JobVersion
        FROM   dbo.ExportJob AS job
               INNER JOIN
               @availableJobs AS availableJob
               ON job.Id = availableJob.Id
                  AND job.JobVersion = availableJob.JobVersion;
    END
COMMIT TRANSACTION;

