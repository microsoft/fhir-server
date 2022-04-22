CREATE PROCEDURE dbo.GetExportJobByHash
@hash VARCHAR (64)
AS
SET NOCOUNT ON;
SELECT   TOP (1) RawJobRecord,
                 JobVersion
FROM     dbo.ExportJob
WHERE    Hash = @hash
         AND (Status = 'Queued'
              OR Status = 'Running')
ORDER BY HeartbeatDateTime ASC;

