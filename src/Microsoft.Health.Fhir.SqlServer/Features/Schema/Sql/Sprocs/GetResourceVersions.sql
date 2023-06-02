--DROP PROCEDURE dbo.GetResourceVersions
GO
CREATE PROCEDURE dbo.GetResourceVersions @ResourceDateKeys dbo.ResourceDateKeyList READONLY
AS
-- This stored procedure allows to identifiy version gap is available
set nocount on
DECLARE @st datetime = getUTCdate()
       ,@SP varchar(100) = 'GetResourceVersions'
       ,@Mode varchar(100) = 'Rows='+convert(varchar,(SELECT count(*) FROM @ResourceDateKeys))
       ,@DummyTop bigint = 9223372036854775807

BEGIN TRY
  SELECT A.ResourceTypeId
        ,A.ResourceId
        ,A.ResourceSurrogateId
        -- set version to 0 if there is no gap available. It would indicate conflict for the caller.
        ,Version = CASE 
                     WHEN EXISTS (SELECT * FROM dbo.Resource B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId AND B.ResourceSurrogateId = A.ResourceSurrogateId) THEN 0
                     WHEN isnull(U.Version, 1) - isnull(L.Version, 0) > 1 THEN isnull(U.Version, 1) - 1 
                     ELSE 0 
                   END
    FROM (SELECT TOP (@DummyTop) * FROM @ResourceDateKeys) A
         OUTER APPLY (SELECT TOP 1 * FROM dbo.Resource B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId AND B.ResourceSurrogateId < A.ResourceSurrogateId ORDER BY B.ResourceSurrogateId DESC) L -- lower
         OUTER APPLY (SELECT TOP 1 * FROM dbo.Resource B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId AND B.ResourceSurrogateId > A.ResourceSurrogateId ORDER BY B.ResourceSurrogateId) U -- upper
    OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
--DECLARE @ResourceDateKeys dbo.ResourceDateKeyList
--SELECT ResourceTypeId, ResourceId, ResourceSurrogateId, Version, IsHistory FROM Resource WHERE ResourceTypeId = 100 AND ResourceId = '00036927-6ef5-38cc-947d-b7900257b33e'
--DELETE FROM Resource WHERE ResourceTypeId = 100 AND ResourceSurrogateId = 5105560146179009828
--INSERT INTO @ResourceDateKeys SELECT TOP 1 ResourceTypeId, ResourceId, 5105560060153802438 FROM Resource WHERE ResourceTypeId = 100
--EXECUTE dbo.GetResourceVersions @ResourceDateKeys
