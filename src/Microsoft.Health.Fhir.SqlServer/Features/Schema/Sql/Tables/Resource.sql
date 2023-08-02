CREATE TABLE dbo.Resource
(
    ResourceTypeId              smallint                NOT NULL,
    ResourceId                  varchar(64)             COLLATE Latin1_General_100_CS_AS NOT NULL,
    Version                     int                     NOT NULL,
    IsHistory                   bit                     NOT NULL,
    ResourceSurrogateId         bigint                  NOT NULL,
    IsDeleted                   bit                     NOT NULL,
    RequestMethod               varchar(10)             NULL,
    RawResource                 varbinary(max)          NOT NULL,
    IsRawResourceMetaSet        bit                     NOT NULL DEFAULT 0,
    SearchParamHash             varchar(64)             NULL,
    TransactionId bigint NULL,
    HistoryTransactionId bigint NULL

    CONSTRAINT PKC_Resource PRIMARY KEY CLUSTERED (ResourceTypeId, ResourceSurrogateId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId(ResourceTypeId),
    CONSTRAINT CH_Resource_RawResource_Length CHECK (RawResource > 0x0)
)

ALTER TABLE dbo.Resource SET ( LOCK_ESCALATION = AUTO )

CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_ResourceTypeId_ResourceId_Version ON dbo.Resource
(
    ResourceTypeId,
    ResourceId,
    Version
)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_ResourceTypeId_ResourceId ON dbo.Resource
(
    ResourceTypeId,
    ResourceId
)
INCLUDE -- We want the query in UpsertResource, which is done with UPDLOCK AND HOLDLOCK, to not require a key lookup
(
    Version,
    IsDeleted
)
WHERE IsHistory = 0
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_ResourceTypeId_ResourceSurrgateId ON dbo.Resource
(
    ResourceTypeId,
    ResourceSurrogateId
)
WHERE IsHistory = 0 AND IsDeleted = 0
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
GO
CREATE INDEX IX_ResourceTypeId_TransactionId ON dbo.Resource (ResourceTypeId, TransactionId) WHERE TransactionId IS NOT NULL ON PartitionScheme_ResourceTypeId (ResourceTypeId)
GO
CREATE INDEX IX_ResourceTypeId_HistoryTransactionId ON dbo.Resource (ResourceTypeId, HistoryTransactionId) WHERE HistoryTransactionId IS NOT NULL ON PartitionScheme_ResourceTypeId (ResourceTypeId)
GO
CREATE OR ALTER PROCEDURE dbo.GetResourcesByTransactionId @TransactionId bigint, @IncludeHistory bit = 0, @ReturnResourceKeysOnly bit = 0
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(100) = 'T='+convert(varchar,@TransactionId)+' H='+convert(varchar,@IncludeHistory)
       ,@st datetime = getUTCdate()
       ,@DummyTop bigint = 9223372036854775807
       ,@TypeId smallint

BEGIN TRY
  DECLARE @Types TABLE (TypeId smallint PRIMARY KEY, Name varchar(100))
  INSERT INTO @Types EXECUTE dbo.GetUsedResourceTypes

  DECLARE @Keys TABLE (TypeId smallint, SurrogateId bigint PRIMARY KEY (TypeId, SurrogateId))
  WHILE EXISTS (SELECT * FROM @Types)
  BEGIN
    SET @TypeId = (SELECT TOP 1 TypeId FROM @Types ORDER BY TypeId)

    INSERT INTO @Keys SELECT @TypeId, ResourceSurrogateId FROM dbo.Resource WHERE ResourceTypeId = @TypeId AND TransactionId = @TransactionId

    DELETE FROM @Types WHERE TypeId = @TypeId
  END

  IF @ReturnResourceKeysOnly = 0
    SELECT ResourceTypeId
          ,ResourceId
          ,ResourceSurrogateId
          ,Version
          ,IsDeleted
          ,IsHistory
          ,RawResource
          ,IsRawResourceMetaSet
          ,SearchParamHash
          ,RequestMethod
      FROM (SELECT TOP (@DummyTop) * FROM @Keys) A
           JOIN dbo.Resource B ON ResourceTypeId = TypeId AND ResourceSurrogateId = SurrogateId
      WHERE IsHistory = 0 OR @IncludeHistory = 1
      OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
  ELSE
    SELECT ResourceTypeId
          ,ResourceId
          ,ResourceSurrogateId
          ,Version
          ,IsDeleted
      FROM (SELECT TOP (@DummyTop) * FROM @Keys) A
           JOIN dbo.Resource B ON ResourceTypeId = TypeId AND ResourceSurrogateId = SurrogateId
      WHERE IsHistory = 0 OR @IncludeHistory = 1
      OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
GO
CREATE OR ALTER PROCEDURE dbo.MergeResourcesGetTimeoutTransactions @TimeoutSec int
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(100) = 'T='+convert(varchar,@TimeoutSec)
       ,@st datetime = getUTCdate()
       ,@MinTransactionId bigint

BEGIN TRY
  EXECUTE dbo.MergeResourcesGetTransactionVisibility @MinTransactionId OUT

  SELECT SurrogateIdRangeFirstValue
    FROM dbo.Transactions 
    WHERE SurrogateIdRangeFirstValue > @MinTransactionId
      AND IsCompleted = 0
      AND datediff(second, HeartbeatDate, getUTCdate()) > @TimeoutSec
    ORDER BY SurrogateIdRangeFirstValue

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
CREATE OR ALTER PROCEDURE dbo.MergeResourcesBeginTransaction @Count int, @TransactionId bigint = 0 OUT, @SurrogateIdRangeFirstValue bigint = 0 OUT, @SequenceRangeFirstValue int = 0 OUT, @HeartbeatDate datetime = NULL -- TODO: Remove @SurrogateIdRangeFirstValue
AS
set nocount on
DECLARE @SP varchar(100) = 'MergeResourcesBeginTransaction'
       ,@Mode varchar(200) = 'Cnt='+convert(varchar,@Count)
       ,@st datetime = getUTCdate()
       ,@FirstValueVar sql_variant
       ,@LastValueVar sql_variant

BEGIN TRY
  SET @TransactionId = NULL

  IF @@trancount > 0 RAISERROR('MergeResourcesBeginTransaction cannot be called inside outer transaction.', 18, 127)
 
  SET @FirstValueVar = NULL
  WHILE @FirstValueVar IS NULL
  BEGIN
    EXECUTE sys.sp_sequence_get_range @sequence_name = 'dbo.ResourceSurrogateIdUniquifierSequence', @range_size = @Count, @range_first_value = @FirstValueVar OUT, @range_last_value = @LastValueVar OUT
    SET @SequenceRangeFirstValue = convert(int,@FirstValueVar)
    IF @SequenceRangeFirstValue > convert(int,@LastValueVar)
      SET @FirstValueVar = NULL
  END

  SET @SurrogateIdRangeFirstValue = datediff_big(millisecond,'0001-01-01',sysUTCdatetime()) * 80000 + @SequenceRangeFirstValue

  INSERT INTO dbo.Transactions
         (  SurrogateIdRangeFirstValue,                SurrogateIdRangeLastValue,                      HeartbeatDate )
    SELECT @SurrogateIdRangeFirstValue, @SurrogateIdRangeFirstValue + @Count - 1, isnull(@HeartbeatDate,getUTCdate() )

  SET @TransactionId = @SurrogateIdRangeFirstValue
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  IF @@trancount > 0 ROLLBACK TRANSACTION
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
GO
CREATE OR ALTER PROCEDURE dbo.MergeResourcesAdvanceTransactionVisibility @AffectedRows int = 0 OUT
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(100) = ''
       ,@st datetime = getUTCdate()
       ,@msg varchar(1000)
       ,@MaxTransactionId bigint
       ,@MinTransactionId bigint
       ,@MinNotCompletedTransactionId bigint
       ,@CurrentTransactionId bigint

SET @AffectedRows = 0

BEGIN TRY
  EXECUTE dbo.MergeResourcesGetTransactionVisibility @MinTransactionId OUT
  SET @MinTransactionId += 1

  SET @CurrentTransactionId = (SELECT TOP 1 SurrogateIdRangeFirstValue FROM dbo.Transactions ORDER BY SurrogateIdRangeFirstValue DESC)

  SET @MinNotCompletedTransactionId = isnull((SELECT TOP 1 SurrogateIdRangeFirstValue FROM dbo.Transactions WHERE IsCompleted = 0 AND SurrogateIdRangeFirstValue BETWEEN @MinTransactionId AND @CurrentTransactionId ORDER BY SurrogateIdRangeFirstValue),@CurrentTransactionId + 1)

  SET @MaxTransactionId = (SELECT TOP 1 SurrogateIdRangeFirstValue FROM dbo.Transactions WHERE IsCompleted = 1 AND SurrogateIdRangeFirstValue BETWEEN @MinTransactionId AND @CurrentTransactionId AND SurrogateIdRangeFirstValue < @MinNotCompletedTransactionId ORDER BY SurrogateIdRangeFirstValue DESC)

  IF @MaxTransactionId >= @MinTransactionId
  BEGIN
    UPDATE A
      SET IsVisible = 1
         ,VisibleDate = getUTCdate()
      FROM dbo.Transactions A WITH (INDEX = 1)
      WHERE SurrogateIdRangeFirstValue BETWEEN @MinTransactionId AND @CurrentTransactionId 
        AND SurrogateIdRangeFirstValue <= @MaxTransactionId
    SET @AffectedRows += @@rowcount
  END

  SET @msg = 'Min='+convert(varchar,@MinTransactionId)+' C='+convert(varchar,@CurrentTransactionId)+' MinNC='+convert(varchar,@MinNotCompletedTransactionId)+' Max='+convert(varchar,@MaxTransactionId)
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@AffectedRows,@Text=@msg
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
CREATE OR ALTER PROCEDURE dbo.MergeResourcesGetTransactionVisibility @TransactionId bigint OUT
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(100) = ''
       ,@st datetime = getUTCdate()

SET @TransactionId = isnull((SELECT TOP 1 SurrogateIdRangeFirstValue FROM dbo.Transactions WHERE IsVisible = 1 ORDER BY SurrogateIdRangeFirstValue DESC),-1)

EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount,@Text=@TransactionId
GO
CREATE OR ALTER PROCEDURE dbo.MergeResourcesCommitTransaction @TransactionId bigint = NULL, @FailureReason varchar(max) = NULL, @OverrideIsControlledByClientCheck bit = 0, @SurrogateIdRangeFirstValue bigint = NULL -- TODO: Remove after deployment
AS
set nocount on
DECLARE @SP varchar(100) = 'MergeResourcesCommitTransaction'
       ,@st datetime = getUTCdate()
       ,@InitialTranCount int = @@trancount
       ,@IsCompletedBefore bit
       ,@Rows int
       ,@msg varchar(1000)

SET @TransactionId = isnull(@TransactionId,@SurrogateIdRangeFirstValue)

DECLARE @Mode varchar(200) = 'TR='+convert(varchar,@TransactionId)+' OC='+isnull(convert(varchar,@OverrideIsControlledByClientCheck),'NULL')

BEGIN TRY
  IF @InitialTranCount = 0 BEGIN TRANSACTION

  UPDATE dbo.Transactions
    SET IsCompleted = 1
       ,@IsCompletedBefore = IsCompleted
       ,EndDate = getUTCdate()
       ,IsSuccess = CASE WHEN @FailureReason IS NULL THEN 1 ELSE 0 END
       ,FailureReason = @FailureReason
    WHERE SurrogateIdRangeFirstValue = @TransactionId
      AND (IsControlledByClient = 1 OR @OverrideIsControlledByClientCheck = 1)
  SET @Rows = @@rowcount

  IF @Rows = 0
  BEGIN
    SET @msg = 'Transaction ['+convert(varchar(20),@TransactionId)+'] is not controlled by client or does not exist.'
    RAISERROR(@msg, 18, 127)
  END

  IF @IsCompletedBefore = 1
  BEGIN
    -- To make this call idempotent
    IF @InitialTranCount = 0 ROLLBACK TRANSACTION
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows,@Target='@IsCompletedBefore',@Text='=1'
    RETURN
  END

  IF @InitialTranCount = 0 COMMIT TRANSACTION
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows
END TRY
BEGIN CATCH
  IF @InitialTranCount = 0 AND @@trancount > 0 ROLLBACK TRANSACTION
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
CREATE OR ALTER PROCEDURE dbo.MergeResourcesDeleteInvisibleHistory @TransactionId bigint, @AffectedRows int = NULL OUT
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(100) = 'T='+convert(varchar,@TransactionId)
       ,@st datetime = getUTCdate()
       ,@TypeId smallint

SET @AffectedRows = 0

BEGIN TRY  
  DECLARE @Types TABLE (TypeId smallint PRIMARY KEY, Name varchar(100))
  INSERT INTO @Types EXECUTE dbo.GetUsedResourceTypes

  WHILE EXISTS (SELECT * FROM @Types)
  BEGIN
    SET @TypeId = (SELECT TOP 1 TypeId FROM @Types ORDER BY TypeId)

    DELETE FROM dbo.Resource WHERE ResourceTypeId = @TypeId AND HistoryTransactionId = @TransactionId AND IsHistory = 1 AND RawResource = 0xF
    SET @AffectedRows += @@rowcount

    DELETE FROM @Types WHERE TypeId = @TypeId
  END

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@AffectedRows
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
CREATE OR ALTER PROCEDURE dbo.GetTransactions @StartNotInclusiveTranId bigint, @EndInclusiveTranId bigint, @EndDate datetime = NULL
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(100) = 'ST='+convert(varchar,@StartNotInclusiveTranId)+' ET='+convert(varchar,@EndInclusiveTranId)+' ED='+isnull(convert(varchar,@EndDate,121),'NULL')
       ,@st datetime = getUTCdate()

IF @EndDate IS NULL
  SET @EndDate = getUTCdate()

SELECT SurrogateIdRangeFirstValue
      ,VisibleDate
      ,InvisibleHistoryRemovedDate
  FROM dbo.Transactions 
  WHERE SurrogateIdRangeFirstValue > @StartNotInclusiveTranId
    AND SurrogateIdRangeFirstValue <= @EndInclusiveTranId
    AND EndDate <= @EndDate
  ORDER BY SurrogateIdRangeFirstValue

EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
GO
CREATE OR ALTER PROCEDURE dbo.MergeResourcesPutTransactionInvisibleHistory @TransactionId bigint
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(100)= 'TR='+convert(varchar,@TransactionId)
       ,@st datetime = getUTCdate()

BEGIN TRY
  UPDATE dbo.Transactions
    SET InvisibleHistoryRemovedDate = getUTCdate()
    WHERE SurrogateIdRangeFirstValue = @TransactionId
      AND InvisibleHistoryRemovedDate IS NULL

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO

CREATE OR ALTER PROCEDURE dbo.HardDeleteResource
   @ResourceTypeId smallint
  ,@ResourceId varchar(64)
  ,@KeepCurrentVersion bit
  ,@IsResourceChangeCaptureEnabled bit
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(200) = 'RT='+convert(varchar,@ResourceTypeId)+' R='+@ResourceId+' V='+convert(varchar,@KeepCurrentVersion)+' CC='+convert(varchar,@IsResourceChangeCaptureEnabled)
       ,@st datetime = getUTCdate()
       ,@TransactionId bigint

BEGIN TRY
  IF @IsResourceChangeCaptureEnabled = 1 EXECUTE dbo.MergeResourcesBeginTransaction @Count = 1, @TransactionId = @TransactionId OUT

  IF @KeepCurrentVersion = 0
    BEGIN TRANSACTION

  DECLARE @SurrogateIds TABLE (ResourceSurrogateId bigint NOT NULL)

  IF @IsResourceChangeCaptureEnabled = 0
    DELETE dbo.Resource
      OUTPUT deleted.ResourceSurrogateId INTO @SurrogateIds
      WHERE ResourceTypeId = @ResourceTypeId
        AND ResourceId = @ResourceId
        AND (@KeepCurrentVersion = 0 OR IsHistory = 1)
        AND RawResource <> 0xF
  ELSE
    UPDATE dbo.Resource
      SET IsHistory = 1
         ,RawResource = 0xF -- invisible value
         ,SearchParamHash = NULL
         ,HistoryTransactionId = @TransactionId
      OUTPUT deleted.ResourceSurrogateId INTO @SurrogateIds
      WHERE ResourceTypeId = @ResourceTypeId
        AND ResourceId = @ResourceId
        AND (@KeepCurrentVersion = 0 OR IsHistory = 1)
        AND RawResource <> 0xF

  IF @KeepCurrentVersion = 0
  BEGIN
    -- PAGLOCK allows deallocation of empty page without waiting for ghost cleanup 
    DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.ResourceWriteClaim B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.ReferenceSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.TokenSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.TokenText B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.StringSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.UriSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.NumberSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.QuantitySearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.DateTimeSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.ReferenceTokenCompositeSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.TokenTokenCompositeSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.TokenDateTimeCompositeSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.TokenQuantityCompositeSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.TokenStringCompositeSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.TokenNumberNumberCompositeSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
  END
  
  IF @@trancount > 0 COMMIT TRANSACTION

  IF @IsResourceChangeCaptureEnabled = 1 EXECUTE dbo.MergeResourcesCommitTransaction @TransactionId

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO

