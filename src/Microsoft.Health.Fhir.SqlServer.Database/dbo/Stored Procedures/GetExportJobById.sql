CREATE PROCEDURE dbo.GetExportJobById
@id VARCHAR (64)
AS
SET NOCOUNT ON;
SELECT RawJobRecord,
       JobVersion
FROM   dbo.ExportJob
WHERE  Id = @id;

