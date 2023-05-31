--DROP PROCEDURE dbo.GetResources
GO
CREATE OR ALTER PROCEDURE dbo.GetResources @ResourceKeys dbo.ResourceKeyList READONLY
AS
set nocount on
DECLARE @st datetime = getUTCdate()
       ,@SP varchar(100) = 'GetResources'
       ,@InputRows int
       ,@DummyTop bigint = 9223372036854775807
       ,@NotNullVersionExists bit 
       ,@NullVersionExists bit
       ,@MinRT smallint
       ,@MaxRT smallint

SELECT @MinRT = min(ResourceTypeId), @MaxRT = max(ResourceTypeId), @InputRows = count(*), @NotNullVersionExists = max(CASE WHEN Version IS NOT NULL THEN 1 ELSE 0 END), @NullVersionExists = max(CASE WHEN Version IS NULL THEN 1 ELSE 0 END) FROM @ResourceKeys

DECLARE @Mode varchar(100) = 'RT=['+convert(varchar,@MinRT)+','+convert(varchar,@MaxRT)+'] Cnt='+convert(varchar,@InputRows)+' NNVE='+convert(varchar,@NotNullVersionExists)+' NVE='+convert(varchar,@NullVersionExists)

BEGIN TRY
  IF @NotNullVersionExists = 1
    IF @NullVersionExists = 0
      SELECT B.ResourceTypeId
            ,B.ResourceId
            ,ResourceSurrogateId
            ,B.Version
            ,IsDeleted
            ,IsHistory
            ,RawResource
            ,IsRawResourceMetaSet
            ,SearchParamHash
        FROM (SELECT TOP (@DummyTop) * FROM @ResourceKeys) A
             JOIN dbo.Resource B WITH (INDEX = IX_Resource_ResourceTypeId_ResourceId_Version) ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId AND B.Version = A.Version
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
    ELSE
      SELECT *
        FROM (SELECT B.ResourceTypeId
                    ,B.ResourceId
                    ,ResourceSurrogateId
                    ,B.Version
                    ,IsDeleted
                    ,IsHistory
                    ,RawResource
                    ,IsRawResourceMetaSet
                    ,SearchParamHash
                FROM (SELECT TOP (@DummyTop) * FROM @ResourceKeys WHERE Version IS NOT NULL) A
                     JOIN dbo.Resource B WITH (INDEX = IX_Resource_ResourceTypeId_ResourceId_Version) ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId AND B.Version = A.Version
            UNION ALL
            SELECT B.ResourceTypeId
                  ,B.ResourceId
                  ,ResourceSurrogateId
                  ,B.Version
                  ,IsDeleted
                  ,IsHistory
                  ,RawResource
                  ,IsRawResourceMetaSet
                  ,SearchParamHash
              FROM (SELECT TOP (@DummyTop) * FROM @ResourceKeys WHERE Version IS NULL) A
                   JOIN dbo.Resource B WITH (INDEX = IX_Resource_ResourceTypeId_ResourceId) ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
              WHERE IsHistory = 0
           ) A
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
  ELSE
    SELECT B.ResourceTypeId
          ,B.ResourceId
          ,ResourceSurrogateId
          ,B.Version
          ,IsDeleted
          ,IsHistory
          ,RawResource
          ,IsRawResourceMetaSet
          ,SearchParamHash
      FROM (SELECT TOP (@DummyTop) * FROM @ResourceKeys) A
           JOIN dbo.Resource B WITH (INDEX = IX_Resource_ResourceTypeId_ResourceId) ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
      WHERE IsHistory = 0
      OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
--DECLARE @ResourceKeys dbo.ResourceKeyList
--INSERT INTO @ResourceKeys SELECT TOP 1 ResourceTypeId, ResourceId, NULL FROM Resource
--EXECUTE dbo.GetResources @ResourceKeys
--IF object_id('ExecuteCommandForRebuildIndexes') IS NOT NULL DROP PROCEDURE dbo.ExecuteCommandForRebuildIndexes
GO
CREATE OR ALTER PROCEDURE dbo.ExecuteCommandForRebuildIndexes @Tbl varchar(100), @Ind varchar(1000), @Cmd varchar(max)
WITH EXECUTE AS 'dbo'
AS
set nocount on
DECLARE @SP varchar(100) = 'ExecuteCommandForRebuildIndexes' 
       ,@Mode varchar(200) = 'Tbl='+isnull(@Tbl,'NULL')
       ,@st datetime
       ,@Retries int = 0
       ,@Action varchar(100)
       ,@msg varchar(1000)

RetryOnTempdbError:

BEGIN TRY
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Start',@Text=@Cmd

  SET @st = getUTCdate()

  IF @Tbl IS NULL RAISERROR('@Tbl IS NULL',18,127)
  IF @Cmd IS NULL RAISERROR('@Cmd IS NULL',18,127)

  SET @Action = CASE 
                  WHEN @Cmd LIKE 'UPDATE STAT%' THEN 'Update statistics'
                  WHEN @Cmd LIKE 'CREATE%INDEX%' THEN 'Create Index'
                  WHEN @Cmd LIKE 'ALTER%INDEX%REBUILD%' THEN 'Rebuild Index'
                  WHEN @Cmd LIKE 'ALTER%TABLE%ADD%' THEN 'Add Constraint'
                END
  IF @Action IS NULL 
  BEGIN
    SET @msg = 'Not supported command = '+convert(varchar(900),@Cmd)
    RAISERROR(@msg,18,127)
  END

  IF @Action = 'Create Index' WAITFOR DELAY '00:00:05'

  EXECUTE(@Cmd)
  SELECT @Ind;

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Action=@Action,@Status='End',@Start=@st,@Text=@Cmd
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  IF error_number() = 40544 -- '%database ''tempdb'' has reached its size quota%'
  BEGIN
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st,@Retry=@Retries
    SET @Retries = @Retries + 1
    IF @Tbl = 'TokenText_96' 
      WAITFOR DELAY '01:00:00' -- 1 hour
    ELSE 
      WAITFOR DELAY '00:10:00' -- 10 minutes
    GOTO RetryOnTempdbError
  END
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
