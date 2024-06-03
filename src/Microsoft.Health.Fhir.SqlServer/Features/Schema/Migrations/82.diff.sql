--DROP PROCEDURE dbo.GetResourceVersions
GO
ALTER PROCEDURE dbo.GetResourceVersions @ResourceDateKeys dbo.ResourceDateKeyList READONLY
AS
-- This stored procedure allows to identifiy if version gap is available and checks dups on lastUpdated
set nocount on
DECLARE @st datetime = getUTCdate()
       ,@SP varchar(100) = 'GetResourceVersions'
       ,@Mode varchar(100) = 'Rows='+convert(varchar,(SELECT count(*) FROM @ResourceDateKeys))
       ,@DummyTop bigint = 9223372036854775807

BEGIN TRY
  SELECT A.ResourceTypeId
        ,A.ResourceId
        ,A.ResourceSurrogateId
        -- set version to 0 if there is no gap available, or lastUpdated is already used. It would indicate potential conflict for the caller.
        ,Version = CASE
                     -- ResourceSurrogateId is generated from lastUpdated only without extra bits at the end. Need to ckeck interval (0..79999) on resource id level.
                     WHEN D.Version IS NOT NULL THEN 0 -- input lastUpdated matches stored 
                     WHEN isnull(U.Version, 1) - isnull(L.Version, 0) > ResourceIndex THEN isnull(U.Version, 1) - ResourceIndex -- gap is available
                     ELSE isnull(M.Version, 0) - ResourceIndex -- late arrival
                   END
        ,MatchedVersion = isnull(D.Version,0)
        ,MatchedRawResource = D.RawResource
        -- ResourceIndex allows to deal with more than one late arrival per resource 
    FROM (SELECT TOP (@DummyTop) *, ResourceIndex = convert(int,row_number() OVER (PARTITION BY ResourceTypeId, ResourceId ORDER BY ResourceSurrogateId DESC)) FROM @ResourceDateKeys) A
         OUTER APPLY (SELECT TOP 1 * FROM dbo.Resource B WITH (INDEX = IX_Resource_ResourceTypeId_ResourceId_Version) WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId AND B.Version > 0 AND B.ResourceSurrogateId < A.ResourceSurrogateId ORDER BY B.ResourceSurrogateId DESC) L -- lower
         OUTER APPLY (SELECT TOP 1 * FROM dbo.Resource B WITH (INDEX = IX_Resource_ResourceTypeId_ResourceId_Version) WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId AND B.Version > 0 AND B.ResourceSurrogateId > A.ResourceSurrogateId ORDER BY B.ResourceSurrogateId) U -- upper
         OUTER APPLY (SELECT TOP 1 * FROM dbo.Resource B WITH (INDEX = IX_Resource_ResourceTypeId_ResourceId_Version) WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId AND B.Version < 0 ORDER BY B.Version) M -- minus
         OUTER APPLY (SELECT TOP 1 * FROM dbo.Resource B WITH (INDEX = IX_Resource_ResourceTypeId_ResourceId_Version) WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId AND B.ResourceSurrogateId BETWEEN A.ResourceSurrogateId AND A.ResourceSurrogateId + 79999) D -- date
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
