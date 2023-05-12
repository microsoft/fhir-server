--DROP TYPE dbo.ResourceDateKeyList
GO
IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'ResourceDateKeyList')
CREATE TYPE dbo.ResourceDateKeyList AS TABLE
(
    ResourceTypeId       smallint            NOT NULL
   ,ResourceId           varchar(64)         COLLATE Latin1_General_100_CS_AS NOT NULL
   ,ResourceSurrogateId  bigint              NOT NULL

    PRIMARY KEY (ResourceTypeId, ResourceId, ResourceSurrogateId)
)
GO
--DROP PROCEDURE dbo.GetResources
GO
CREATE OR ALTER PROCEDURE dbo.GetResources @ResourceKeys dbo.ResourceKeyList READONLY, @ResourceDateKeys dbo.ResourceDateKeyList READONLY
AS
set nocount on
DECLARE @st datetime = getUTCdate()
       ,@SP varchar(100) = 'GetResources'
       ,@InputRows int
       ,@InputDateRows int
       ,@DummyTop bigint = 9223372036854775807
       ,@NotNullVersionExists bit 
       ,@NullVersionExists bit 

SELECT @InputRows = count(*), @NotNullVersionExists = max(CASE WHEN Version IS NOT NULL THEN 1 ELSE 0 END), @NullVersionExists = max(CASE WHEN Version IS NULL THEN 1 ELSE 0 END) FROM @ResourceKeys
SET @InputDateRows = (SELECT count(*) FROM @ResourceDateKeys)

DECLARE @Mode varchar(100) = 'Dats='+convert(varchar,@InputDateRows)+' Vers='+convert(varchar,@InputRows)+' NotNullVer='+convert(varchar,@NotNullVersionExists)+' NullVer='+convert(varchar,@NullVersionExists)

BEGIN TRY
  IF @InputRows > 0 AND @InputDateRows > 0 RAISERROR('Both input TVPs have data', 18, 127)

  IF @InputDateRows > 0 -- find closest from the bottom
    SELECT C.ResourceTypeId
          ,C.ResourceId
          ,C.ResourceSurrogateId
          ,Version
          ,IsDeleted
          ,IsHistory
          ,RawResource
          ,IsRawResourceMetaSet
          ,SearchParamHash
      FROM (SELECT TOP (@DummyTop) * FROM @ResourceDateKeys) A
           CROSS APPLY (SELECT TOP 1 * FROM dbo.Resource B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId AND B.ResourceSurrogateId < A.ResourceSurrogateId ORDER BY B.ResourceSurrogateId DESC) C
      OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
  ELSE
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
               JOIN dbo.Resource B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId AND B.Version = A.Version
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
                       JOIN dbo.Resource B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId AND B.Version = A.Version
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
                     JOIN dbo.Resource B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
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
             JOIN dbo.Resource B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
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
--DROP PROCEDURE dbo.MergeResourcesBeginTransaction
GO
CREATE OR ALTER PROCEDURE dbo.MergeResourcesBeginTransaction @Count int, @SurrogateIdRangeFirstValue bigint = 0 OUT, @SequenceRangeFirstValue int = 0 OUT
AS
set nocount on
DECLARE @SP varchar(100) = 'MergeResourcesBeginTransaction'
       ,@Mode varchar(200) = 'Cnt='+convert(varchar,@Count)
       ,@st datetime = getUTCdate()
       ,@FirstValueVar sql_variant
       ,@LastValueVar sql_variant
       ,@TransactionId bigint = NULL
       ,@RunTransactionCheck bit = (SELECT Number FROM dbo.Parameters WHERE Id = 'MergeResources.SurrogateIdRangeOverlapCheck.IsEnabled')

BEGIN TRY
  IF @@trancount > 0 RAISERROR('MergeResourcesBeginTransaction cannot be called inside outer transaction.', 18, 127)

  SET TRANSACTION ISOLATION LEVEL REPEATABLE READ
    
  WHILE @TransactionId IS NULL
  BEGIN
    SET @FirstValueVar = NULL
    WHILE @FirstValueVar IS NULL
    BEGIN
      EXECUTE sys.sp_sequence_get_range @sequence_name = 'dbo.ResourceSurrogateIdUniquifierSequence', @range_size = @Count, @range_first_value = @FirstValueVar OUT, @range_last_value = @LastValueVar OUT
      SET @SequenceRangeFirstValue = convert(int,@FirstValueVar)
      IF @SequenceRangeFirstValue > convert(int,@LastValueVar)
        SET @FirstValueVar = NULL
    END

    SET @SurrogateIdRangeFirstValue = datediff_big(millisecond,'0001-01-01',sysUTCdatetime()) * 80000 + @SequenceRangeFirstValue

    IF @RunTransactionCheck = 1
    BEGIN
      BEGIN TRANSACTION

      INSERT INTO dbo.Transactions
             (  SurrogateIdRangeFirstValue,                SurrogateIdRangeLastValue )
        SELECT @SurrogateIdRangeFirstValue, @SurrogateIdRangeFirstValue + @Count - 1

      IF isnull((SELECT TOP 1 SurrogateIdRangeLastValue FROM dbo.Transactions WHERE SurrogateIdRangeFirstValue < @SurrogateIdRangeFirstValue ORDER BY SurrogateIdRangeFirstValue DESC),0) < @SurrogateIdRangeFirstValue
      BEGIN
        COMMIT TRANSACTION
        SET @TransactionId = @SurrogateIdRangeFirstValue
      END
      ELSE
      BEGIN
        ROLLBACK TRANSACTION
        SET @TransactionId = NULL
        EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Warn',@Start=@st,@Rows=NULL,@Text=@SurrogateIdRangeFirstValue
      END
    END
    ELSE
      SET @TransactionId = @SurrogateIdRangeFirstValue
  END
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  IF @@trancount > 0 ROLLBACK TRANSACTION
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
--DECLARE @SurrogateIdRangeFirstValue bigint
--EXECUTE dbo.MergeResourcesBeginTransaction @Count = 100, @SurrogateIdRangeFirstValue = @SurrogateIdRangeFirstValue OUT
--SELECT @SurrogateIdRangeFirstValue
--SELECT TOP 10 * FROM Transactions ORDER BY SurrogateIdRangeFirstValue DESC
--SELECT TOP 100 * FROM EventLog WHERE EventDate > dateadd(minute,-60,getUTCdate()) AND Process = 'MergeResourcesBeginTransaction' ORDER BY EventDate DESC, EventId DESC
