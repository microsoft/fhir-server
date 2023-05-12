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
--DROP PROCEDURE dbo.GetResourceVersions
GO
CREATE OR ALTER PROCEDURE dbo.GetResourceVersions @ResourceDateKeys dbo.ResourceDateKeyList READONLY
AS
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
