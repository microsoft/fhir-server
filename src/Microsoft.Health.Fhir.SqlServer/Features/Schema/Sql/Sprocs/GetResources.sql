--DROP PROCEDURE dbo.GetResources
GO
CREATE PROCEDURE dbo.GetResources @ResourceKeys dbo.ResourceKeyList READONLY
AS
set nocount on
DECLARE @st datetime = getUTCdate()
       ,@SP varchar(100) = 'GetResources'
       ,@InputRows int = (SELECT count(*) FROM @ResourceKeys)
       ,@DummyTop bigint = 9223372036854775807

DECLARE @Mode varchar(100) = 'Input='+convert(varchar,@InputRows)

BEGIN TRY
  SELECT ResourceTypeId
        ,ResourceId
        ,ResourceSurrogateId
        ,Version
        ,IsDeleted
        ,IsHistory
        ,RawResource
        ,IsRawResourceMetaSet
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
