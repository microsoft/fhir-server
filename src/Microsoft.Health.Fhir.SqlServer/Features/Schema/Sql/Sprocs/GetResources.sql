--DROP PROCEDURE dbo.GetResources
GO
CREATE PROCEDURE dbo.GetResources @ResourceKeys dbo.ResourceKeyList READONLY
AS
set nocount on
DECLARE @st datetime = getUTCdate()
       ,@SP varchar(100) = 'GetResources'
       ,@InputRows int
       ,@DummyTop bigint = 9223372036854775807
       ,@NotNullVersionExists bit 

SELECT @InputRows = count(*), @NotNullVersionExists = max(CASE WHEN Version IS NOT NULL THEN 1 ELSE 0 END) FROM @ResourceKeys

DECLARE @Mode varchar(100) = 'Cnt='+convert(varchar,@InputRows)+' V='+convert(varchar,@NotNullVersionExists)

BEGIN TRY
  IF @NotNullVersionExists = 1
    SELECT *
      FROM (SELECT ResourceTypeId
                  ,ResourceId
                  ,ResourceSurrogateId
                  ,Version
                  ,IsDeleted
                  ,IsHistory
                  ,RawResource
                  ,IsRawResourceMetaSet
                  ,SearchParamHash
              FROM dbo.Resource A
              WHERE EXISTS (SELECT TOP (@DummyTop) * FROM @ResourceKeys B WHERE B.Version IS NOT NULL AND B.Version = A.Version AND B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId)
            UNION ALL
            SELECT ResourceTypeId
                  ,ResourceId
                  ,ResourceSurrogateId
                  ,Version
                  ,IsDeleted
                  ,IsHistory
                  ,RawResource
                  ,IsRawResourceMetaSet
                  ,SearchParamHash
              FROM dbo.Resource A
              WHERE EXISTS (SELECT TOP (@DummyTop) * FROM @ResourceKeys B WHERE B.Version IS NULL AND B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId)
                AND IsHistory = 0
           ) A
      OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
  ELSE
    SELECT ResourceTypeId
          ,ResourceId
          ,ResourceSurrogateId
          ,Version
          ,IsDeleted
          ,IsHistory
          ,RawResource
          ,IsRawResourceMetaSet
          ,SearchParamHash
      FROM dbo.Resource A
      WHERE EXISTS (SELECT TOP (@DummyTop) * FROM @ResourceKeys B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId)
        AND IsHistory = 0
      OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
