--DROP PROCEDURE dbo.MergeResourcesBeginTransaction
GO
CREATE OR ALTER PROCEDURE dbo.MergeResourcesBeginTransaction @Count int, @TransactionId bigint = 0 OUT, @SurrogateIdRangeFirstValue bigint = 0 OUT, @SequenceRangeFirstValue int = 0 OUT -- TODO: Remove @SurrogateIdRangeFirstValue
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
         (  SurrogateIdRangeFirstValue,                SurrogateIdRangeLastValue )
    SELECT @SurrogateIdRangeFirstValue, @SurrogateIdRangeFirstValue + @Count - 1

  SET @TransactionId = @SurrogateIdRangeFirstValue
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  IF @@trancount > 0 ROLLBACK TRANSACTION
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
--DECLARE @SurrogateIdRangeFirstValue bigint
--EXECUTE dbo.MergeResourcesBeginTransaction @Count = 100, @TransactionId = 0, @SurrogateIdRangeFirstValue = @SurrogateIdRangeFirstValue OUT
--SELECT @SurrogateIdRangeFirstValue
--SELECT TOP 10 * FROM Transactions ORDER BY SurrogateIdRangeFirstValue DESC
--SELECT TOP 100 * FROM EventLog WHERE EventDate > dateadd(minute,-60,getUTCdate()) AND Process = 'MergeResourcesBeginTransaction' ORDER BY EventDate DESC, EventId DESC
--DROP PROCEDURE MergeResourcesAdvanceTransactionVisibility
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

  SET @MinNotCompletedTransactionId = (SELECT TOP 1 SurrogateIdRangeFirstValue FROM dbo.Transactions WHERE IsCompleted = 0 AND SurrogateIdRangeFirstValue BETWEEN @MinTransactionId AND @CurrentTransactionId ORDER BY SurrogateIdRangeFirstValue)

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
--DROP PROCEDURE dbo.MergeResourcesGetTransactionVisibility
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
--DECLARE @TransactionId bigint
--EXECUTE MergeResourcesGetTransactionVisibility @TransactionId OUT
--SELECT @TransactionId
--DROP PROCEDURE dbo.MergeResourcesCommitTransaction
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
--DROP PROCEDURE dbo.MergeResources
GO
CREATE OR ALTER PROCEDURE dbo.MergeResources
-- This stored procedure can be used for:
-- 1. Ordinary put with single version per resource in input
-- 2. Put with history preservation (multiple input versions per resource)
-- 3. Copy from one gen2 store to another with ResourceSurrogateId preserved.
    @AffectedRows int = 0 OUT
   ,@RaiseExceptionOnConflict bit = 1
   ,@IsResourceChangeCaptureEnabled bit = 0
   ,@TransactionId bigint = NULL
   ,@SingleTransaction bit = 1
   ,@Resources dbo.ResourceList READONLY
   ,@ResourceWriteClaims dbo.ResourceWriteClaimList READONLY
   ,@CompartmentAssignments dbo.CompartmentAssignmentList READONLY -- TODO: Remove after version 57 got deployed
   ,@ReferenceSearchParams dbo.ReferenceSearchParamList READONLY
   ,@TokenSearchParams dbo.TokenSearchParamList READONLY
   ,@TokenTexts dbo.TokenTextList READONLY
   ,@StringSearchParams dbo.StringSearchParamList READONLY
   ,@UriSearchParams dbo.UriSearchParamList READONLY
   ,@NumberSearchParams dbo.NumberSearchParamList READONLY
   ,@QuantitySearchParams dbo.QuantitySearchParamList READONLY
   ,@DateTimeSearchParms dbo.DateTimeSearchParamList READONLY
   ,@ReferenceTokenCompositeSearchParams dbo.ReferenceTokenCompositeSearchParamList READONLY
   ,@TokenTokenCompositeSearchParams dbo.TokenTokenCompositeSearchParamList READONLY
   ,@TokenDateTimeCompositeSearchParams dbo.TokenDateTimeCompositeSearchParamList READONLY
   ,@TokenQuantityCompositeSearchParams dbo.TokenQuantityCompositeSearchParamList READONLY
   ,@TokenStringCompositeSearchParams dbo.TokenStringCompositeSearchParamList READONLY
   ,@TokenNumberNumberCompositeSearchParams dbo.TokenNumberNumberCompositeSearchParamList READONLY
AS
set nocount on
DECLARE @st datetime = getUTCdate()
       ,@SP varchar(100) = 'MergeResources'
       ,@DummyTop bigint = 9223372036854775807
       ,@InitialTranCount int = @@trancount
       ,@IsRetry bit = 0

DECLARE @Mode varchar(200) = isnull((SELECT 'RT=['+convert(varchar,min(ResourceTypeId))+','+convert(varchar,max(ResourceTypeId))+'] Sur=['+convert(varchar,min(ResourceSurrogateId))+','+convert(varchar,max(ResourceSurrogateId))+'] V='+convert(varchar,max(Version))+' Rows='+convert(varchar,count(*)) FROM @Resources),'Input=Empty')
SET @Mode += ' E='+convert(varchar,@RaiseExceptionOnConflict)+' CC='+convert(varchar,@IsResourceChangeCaptureEnabled)+' IT='+convert(varchar,@InitialTranCount)

SET @AffectedRows = 0

BEGIN TRY
  DECLARE @Existing AS TABLE (ResourceTypeId smallint NOT NULL, SurrogateId bigint NOT NULL PRIMARY KEY (ResourceTypeId, SurrogateId))

  DECLARE @ResourceInfos AS TABLE
    (
       ResourceTypeId       smallint       NOT NULL
      ,SurrogateId          bigint         NOT NULL
      ,Version              int            NOT NULL
      ,KeepHistory          bit            NOT NULL
      ,PreviousVersion      int            NULL
      ,PreviousSurrogateId  bigint         NULL

      PRIMARY KEY (ResourceTypeId, SurrogateId)
    )

  DECLARE @PreviousSurrogateIds AS TABLE (TypeId smallint NOT NULL, SurrogateId bigint NOT NULL PRIMARY KEY (TypeId, SurrogateId), KeepHistory bit)

  IF @SingleTransaction = 0 AND isnull((SELECT Number FROM dbo.Parameters WHERE Id = 'MergeResources.NoTransaction.IsEnabled'),0) = 0
    SET @SingleTransaction = 1
  
  SET @Mode += ' ST='+convert(varchar,@SingleTransaction)

  -- perform retry check in transaction to hold locks
  IF @InitialTranCount = 0
  BEGIN
    IF EXISTS (SELECT * -- This extra statement avoids putting range locks when we don't need them
                 FROM @Resources A JOIN dbo.Resource B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
                 WHERE B.IsHistory = 0
              )
    BEGIN
      BEGIN TRANSACTION

      INSERT INTO @Existing
              (  ResourceTypeId,           SurrogateId )
        SELECT B.ResourceTypeId, B.ResourceSurrogateId
          FROM (SELECT TOP (@DummyTop) * FROM @Resources) A
               JOIN dbo.Resource B WITH (ROWLOCK, HOLDLOCK) ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
          WHERE B.IsHistory = 0
          OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
    
      IF @@rowcount > 0 SET @IsRetry = 1

      IF @IsRetry = 0 COMMIT TRANSACTION -- commit check transaction 
    END
  END

  SET @Mode += ' R='+convert(varchar,@IsRetry)

  IF @SingleTransaction = 1 AND @@trancount = 0 BEGIN TRANSACTION
  
  IF @IsRetry = 0
  BEGIN
    INSERT INTO @ResourceInfos
            (  ResourceTypeId,           SurrogateId,   Version,   KeepHistory, PreviousVersion,   PreviousSurrogateId )
      SELECT A.ResourceTypeId, A.ResourceSurrogateId, A.Version, A.KeepHistory,       B.Version, B.ResourceSurrogateId
        FROM (SELECT TOP (@DummyTop) * FROM @Resources WHERE HasVersionToCompare = 1) A
             LEFT OUTER JOIN dbo.Resource B -- WITH (UPDLOCK, HOLDLOCK) These locking hints cause deadlocks and are not needed. Racing might lead to tries to insert dups in unique index (with version key), but it will fail anyway, and in no case this will cause incorrect data saved.
               ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId AND B.IsHistory = 0
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))

    IF @RaiseExceptionOnConflict = 1 AND EXISTS (SELECT * FROM @ResourceInfos WHERE PreviousVersion IS NOT NULL AND Version <> PreviousVersion + 1)
      THROW 50409, 'Resource has been recently updated or added, please compare the resource content in code for any duplicate updates', 1

    INSERT INTO @PreviousSurrogateIds
      SELECT ResourceTypeId, PreviousSurrogateId, KeepHistory
        FROM @ResourceInfos 
        WHERE PreviousSurrogateId IS NOT NULL

    IF @@rowcount > 0
    BEGIN
      UPDATE dbo.Resource
        SET IsHistory = 1
        WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId AND KeepHistory = 1)
      SET @AffectedRows += @@rowcount
    
      DELETE FROM dbo.Resource WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId AND KeepHistory = 0)
      SET @AffectedRows += @@rowcount

      DELETE FROM dbo.ResourceWriteClaim WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE SurrogateId = ResourceSurrogateId)
      SET @AffectedRows += @@rowcount
      DELETE FROM dbo.ReferenceSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
      SET @AffectedRows += @@rowcount
      DELETE FROM dbo.TokenSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
      SET @AffectedRows += @@rowcount
      DELETE FROM dbo.TokenText WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
      SET @AffectedRows += @@rowcount
      DELETE FROM dbo.StringSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
      SET @AffectedRows += @@rowcount
      DELETE FROM dbo.UriSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
      SET @AffectedRows += @@rowcount
      DELETE FROM dbo.NumberSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
      SET @AffectedRows += @@rowcount
      DELETE FROM dbo.QuantitySearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
      SET @AffectedRows += @@rowcount
      DELETE FROM dbo.DateTimeSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
      SET @AffectedRows += @@rowcount
      DELETE FROM dbo.ReferenceTokenCompositeSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
      SET @AffectedRows += @@rowcount
      DELETE FROM dbo.TokenTokenCompositeSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
      SET @AffectedRows += @@rowcount
      DELETE FROM dbo.TokenDateTimeCompositeSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
      SET @AffectedRows += @@rowcount
      DELETE FROM dbo.TokenQuantityCompositeSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
      SET @AffectedRows += @@rowcount
      DELETE FROM dbo.TokenStringCompositeSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
      SET @AffectedRows += @@rowcount
      DELETE FROM dbo.TokenNumberNumberCompositeSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
      SET @AffectedRows += @@rowcount

      --EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Start=@st,@Rows=@AffectedRows,@Text='Old rows'
    END

    INSERT INTO dbo.Resource 
           ( ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash )
      SELECT ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash
        FROM @Resources
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.ResourceWriteClaim 
           ( ResourceSurrogateId, ClaimTypeId, ClaimValue )
      SELECT ResourceSurrogateId, ClaimTypeId, ClaimValue
        FROM @ResourceWriteClaims
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.ReferenceSearchParam 
           ( ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion )
      SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion
        FROM @ReferenceSearchParams
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.TokenSearchParam 
           ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow )
      SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow
        FROM @TokenSearchParams
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.TokenText 
           ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Text )
      SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Text
        FROM @TokenTexts
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.StringSearchParam 
           ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax )
      SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax
        FROM @StringSearchParams
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.UriSearchParam 
           ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri )
      SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri
        FROM @UriSearchParams
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.NumberSearchParam 
           ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue )
      SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue
        FROM @NumberSearchParams
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.QuantitySearchParam 
           ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue )
      SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue
        FROM @QuantitySearchParams
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.DateTimeSearchParam 
           ( ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax )
      SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax
        FROM @DateTimeSearchParms
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.ReferenceTokenCompositeSearchParam 
           ( ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2 )
      SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2
        FROM @ReferenceTokenCompositeSearchParams
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.TokenTokenCompositeSearchParam 
           ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2 )
      SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2
        FROM @TokenTokenCompositeSearchParams
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.TokenDateTimeCompositeSearchParam 
           ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2 )
      SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2
        FROM @TokenDateTimeCompositeSearchParams
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.TokenQuantityCompositeSearchParam 
           ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2 )
      SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2
        FROM @TokenQuantityCompositeSearchParams
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.TokenStringCompositeSearchParam 
           ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2 )
      SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2
        FROM @TokenStringCompositeSearchParams
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.TokenNumberNumberCompositeSearchParam 
           ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange )
      SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange
        FROM @TokenNumberNumberCompositeSearchParams
    SET @AffectedRows += @@rowcount
  END -- @IsRetry = 0
  ELSE
  BEGIN -- @IsRetry = 1
    INSERT INTO dbo.ResourceWriteClaim 
           ( ResourceSurrogateId, ClaimTypeId, ClaimValue )
      SELECT ResourceSurrogateId, ClaimTypeId, ClaimValue
        FROM (SELECT TOP (@DummyTop) * FROM @ResourceWriteClaims) A
        WHERE EXISTS (SELECT * FROM @Existing B WHERE B.SurrogateId = A.ResourceSurrogateId)
          AND NOT EXISTS (SELECT * FROM dbo.ResourceWriteClaim C WHERE C.ResourceSurrogateId = A.ResourceSurrogateId)
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.ReferenceSearchParam 
           ( ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion )
      SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion
        FROM (SELECT TOP (@DummyTop) * FROM @ReferenceSearchParams) A
        WHERE EXISTS (SELECT * FROM @Existing B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.SurrogateId = A.ResourceSurrogateId)
          AND NOT EXISTS (SELECT * FROM dbo.ReferenceSearchParam C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.TokenSearchParam 
           ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow )
      SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow
        FROM (SELECT TOP (@DummyTop) * FROM @TokenSearchParams) A
        WHERE EXISTS (SELECT * FROM @Existing B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.SurrogateId = A.ResourceSurrogateId)
          AND NOT EXISTS (SELECT * FROM dbo.TokenSearchParam C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.TokenText 
           ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Text )
      SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Text
        FROM (SELECT TOP (@DummyTop) * FROM @TokenTexts) A
        WHERE EXISTS (SELECT * FROM @Existing B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.SurrogateId = A.ResourceSurrogateId)
          AND NOT EXISTS (SELECT * FROM dbo.TokenSearchParam C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.StringSearchParam 
           ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax )
      SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax
        FROM (SELECT TOP (@DummyTop) * FROM @StringSearchParams) A
        WHERE EXISTS (SELECT * FROM @Existing B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.SurrogateId = A.ResourceSurrogateId)
          AND NOT EXISTS (SELECT * FROM dbo.TokenText C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.UriSearchParam 
           ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri )
      SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri
        FROM (SELECT TOP (@DummyTop) * FROM @UriSearchParams) A
        WHERE EXISTS (SELECT * FROM @Existing B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.SurrogateId = A.ResourceSurrogateId)
          AND NOT EXISTS (SELECT * FROM dbo.UriSearchParam C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.NumberSearchParam 
           ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue )
      SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue
        FROM (SELECT TOP (@DummyTop) * FROM @NumberSearchParams) A
        WHERE EXISTS (SELECT * FROM @Existing B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.SurrogateId = A.ResourceSurrogateId)
          AND NOT EXISTS (SELECT * FROM dbo.NumberSearchParam C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.QuantitySearchParam 
           ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue )
      SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue
        FROM (SELECT TOP (@DummyTop) * FROM @QuantitySearchParams) A
        WHERE EXISTS (SELECT * FROM @Existing B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.SurrogateId = A.ResourceSurrogateId)
          AND NOT EXISTS (SELECT * FROM dbo.QuantitySearchParam C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.DateTimeSearchParam 
           ( ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax )
      SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax
        FROM (SELECT TOP (@DummyTop) * FROM @DateTimeSearchParms) A
        WHERE EXISTS (SELECT * FROM @Existing B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.SurrogateId = A.ResourceSurrogateId)
          AND NOT EXISTS (SELECT * FROM dbo.TokenSearchParam C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.ReferenceTokenCompositeSearchParam 
           ( ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2 )
      SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2
        FROM (SELECT TOP (@DummyTop) * FROM @ReferenceTokenCompositeSearchParams) A
        WHERE EXISTS (SELECT * FROM @Existing B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.SurrogateId = A.ResourceSurrogateId)
          AND NOT EXISTS (SELECT * FROM dbo.DateTimeSearchParam C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.TokenTokenCompositeSearchParam 
           ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2 )
      SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2
        FROM (SELECT TOP (@DummyTop) * FROM @TokenTokenCompositeSearchParams) A
        WHERE EXISTS (SELECT * FROM @Existing B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.SurrogateId = A.ResourceSurrogateId)
          AND NOT EXISTS (SELECT * FROM dbo.TokenTokenCompositeSearchParam C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.TokenDateTimeCompositeSearchParam 
           ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2 )
      SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2
        FROM (SELECT TOP (@DummyTop) * FROM @TokenDateTimeCompositeSearchParams) A
        WHERE EXISTS (SELECT * FROM @Existing B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.SurrogateId = A.ResourceSurrogateId)
          AND NOT EXISTS (SELECT * FROM dbo.TokenDateTimeCompositeSearchParam C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.TokenQuantityCompositeSearchParam 
           ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2 )
      SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2
        FROM (SELECT TOP (@DummyTop) * FROM @TokenQuantityCompositeSearchParams) A
        WHERE EXISTS (SELECT * FROM @Existing B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.SurrogateId = A.ResourceSurrogateId)
          AND NOT EXISTS (SELECT * FROM dbo.TokenQuantityCompositeSearchParam C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.TokenStringCompositeSearchParam 
           ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2 )
      SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2
        FROM (SELECT TOP (@DummyTop) * FROM @TokenStringCompositeSearchParams) A
        WHERE EXISTS (SELECT * FROM @Existing B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.SurrogateId = A.ResourceSurrogateId)
          AND NOT EXISTS (SELECT * FROM dbo.TokenStringCompositeSearchParam C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.TokenNumberNumberCompositeSearchParam 
           ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange )
      SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange
        FROM (SELECT TOP (@DummyTop) * FROM @TokenNumberNumberCompositeSearchParams) A
        WHERE EXISTS (SELECT * FROM @Existing B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.SurrogateId = A.ResourceSurrogateId)
          AND NOT EXISTS (SELECT * FROM dbo.TokenNumberNumberCompositeSearchParam C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
    SET @AffectedRows += @@rowcount
  END

  IF @IsResourceChangeCaptureEnabled = 1 --If the resource change capture feature is enabled, to execute a stored procedure called CaptureResourceChanges to insert resource change data.
    EXECUTE dbo.CaptureResourceIdsForChanges @Resources

  IF @TransactionId IS NOT NULL
    EXECUTE dbo.MergeResourcesCommitTransaction @TransactionId

  IF @InitialTranCount = 0 AND @@trancount > 0 COMMIT TRANSACTION

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@AffectedRows
END TRY
BEGIN CATCH
  IF @InitialTranCount = 0 AND @@trancount > 0 ROLLBACK TRANSACTION
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;

  IF @RaiseExceptionOnConflict = 1 AND (error_number() = 2601 AND error_message() LIKE '%''dbo.Resource''%version%' OR error_number() = 2627 AND error_message() LIKE '%''dbo.Resource''%')
    THROW 50409, 'Resource has been recently updated or added, please compare the resource content in code for any duplicate updates', 1;
  ELSE
    THROW
END CATCH
GO
