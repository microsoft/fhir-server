IF object_id('TokenSearchParamHighCard') IS NULL
CREATE TABLE dbo.TokenSearchParamHighCard
(
    ResourceTypeId              smallint                NOT NULL
   ,ResourceSurrogateId         bigint                  NOT NULL
   ,SearchParamId               smallint                NOT NULL
   ,SystemId                    int                     NULL
   ,Code                        varchar(256)            COLLATE Latin1_General_100_CS_AS NOT NULL
   ,CodeOverflow                varchar(max)            COLLATE Latin1_General_100_CS_AS NULL
)
GO
IF NOT EXISTS (SELECT * FROM sys.check_constraints WHERE parent_object_id = object_id('TokenSearchParamHighCard') AND name = 'CHK_TokenSearchParamHighCard_CodeOverflow')
ALTER TABLE dbo.TokenSearchParamHighCard ADD CONSTRAINT CHK_TokenSearchParamHighCard_CodeOverflow CHECK (LEN(Code) = 256 OR CodeOverflow IS NULL)
GO
ALTER TABLE dbo.TokenSearchParamHighCard SET (LOCK_ESCALATION = AUTO)
GO
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenSearchParamHighCard') AND name = 'IXC_ResourceTypeId_ResourceSurrogateId_SearchParamId')
CREATE CLUSTERED INDEX IXC_ResourceTypeId_ResourceSurrogateId_SearchParamId ON dbo.TokenSearchParamHighCard (ResourceTypeId, ResourceSurrogateId, SearchParamId)
  WITH (DATA_COMPRESSION = PAGE)
  ON PartitionScheme_ResourceTypeId (ResourceTypeId)
GO
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenSearchParamHighCard') AND name = 'IX_SearchParamId_Code_INCLUDE_SystemId')
CREATE INDEX IX_SearchParamId_Code_INCLUDE_SystemId ON dbo.TokenSearchParamHighCard (SearchParamId, Code) INCLUDE (SystemId)
  WITH (DATA_COMPRESSION = PAGE)
  ON PartitionScheme_ResourceTypeId (ResourceTypeId)
GO
ALTER PROCEDURE dbo.MergeResources
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
   ,@ReferenceSearchParams dbo.ReferenceSearchParamList READONLY
   ,@TokenSearchParamHighCards dbo.TokenSearchParamList READONLY
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
       ,@SP varchar(100) = object_name(@@procid)
       ,@DummyTop bigint = 9223372036854775807
       ,@InitialTranCount int = @@trancount
       ,@IsRetry bit = 0

DECLARE @Mode varchar(200) = isnull((SELECT 'RT=['+convert(varchar,min(ResourceTypeId))+','+convert(varchar,max(ResourceTypeId))+'] Sur=['+convert(varchar,min(ResourceSurrogateId))+','+convert(varchar,max(ResourceSurrogateId))+'] V='+convert(varchar,max(Version))+' Rows='+convert(varchar,count(*)) FROM @Resources),'Input=Empty')
SET @Mode += ' E='+convert(varchar,@RaiseExceptionOnConflict)+' CC='+convert(varchar,@IsResourceChangeCaptureEnabled)+' IT='+convert(varchar,@InitialTranCount)+' T='+isnull(convert(varchar,@TransactionId),'NULL')

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
                 --WHERE B.IsHistory = 0 -- With this clause wrong plans are created on empty/small database. Commented until resource separation is in place.
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

      IF @IsResourceChangeCaptureEnabled = 1 AND NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = 'InvisibleHistory.IsEnabled' AND Number = 0)
        UPDATE dbo.Resource
          SET IsHistory = 1
             ,RawResource = 0xF -- "invisible" value
             ,SearchParamHash = NULL
             ,HistoryTransactionId = @TransactionId
          WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId AND KeepHistory = 0)
      ELSE
        DELETE FROM dbo.Resource WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId AND KeepHistory = 0)
      SET @AffectedRows += @@rowcount

      DELETE FROM dbo.ResourceWriteClaim WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE SurrogateId = ResourceSurrogateId)
      SET @AffectedRows += @@rowcount
      DELETE FROM dbo.ReferenceSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
      SET @AffectedRows += @@rowcount
      DELETE FROM dbo.TokenSearchParamHighCard WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
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
           ( ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash,  TransactionId )
      SELECT ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash, @TransactionId
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

    IF EXISTS (SELECT * FROM @TokenSearchParamHighCards)
      INSERT INTO dbo.TokenSearchParamHighCard 
             ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow )
        SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow
          FROM @TokenSearchParamHighCards
    ELSE
      INSERT INTO dbo.TokenSearchParamHighCard 
             ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow )
        SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow
          FROM @TokenSearchParams
          WHERE SearchParamId IN (SELECT SearchParamId FROM dbo.SearchParam WHERE Uri LIKE '%identifier' OR Uri LIKE '%phone' OR Uri LIKE '%telecom')
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

    IF EXISTS (SELECT * FROM @TokenSearchParamHighCards)
     INSERT INTO dbo.TokenSearchParamHighCard 
            ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow )
       SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow
         FROM (SELECT TOP (@DummyTop) * FROM @TokenSearchParamHighCards) A
         WHERE EXISTS (SELECT * FROM @Existing B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.SurrogateId = A.ResourceSurrogateId)
           AND NOT EXISTS (SELECT * FROM dbo.TokenSearchParamHighCard C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
         OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
    ELSE
      INSERT INTO dbo.TokenSearchParamHighCard 
             ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow )
        SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow
          FROM (SELECT TOP (@DummyTop) * FROM @TokenSearchParams WHERE SearchParamId IN (SELECT SearchParamId FROM dbo.SearchParam WHERE Uri LIKE '%identifier' OR Uri LIKE '%phone' OR Uri LIKE '%telecom')) A
          WHERE EXISTS (SELECT * FROM @Existing B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.SurrogateId = A.ResourceSurrogateId)
            AND NOT EXISTS (SELECT * FROM dbo.TokenSearchParamHighCard C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
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

  IF @RaiseExceptionOnConflict = 1 AND error_number() IN (2601, 2627) AND error_message() LIKE '%''dbo.Resource''%'
    THROW 50409, 'Resource has been recently updated or added, please compare the resource content in code for any duplicate updates', 1;
  ELSE
    THROW
END CATCH
GO
ALTER PROCEDURE dbo.UpdateResourceSearchParams
    @FailedResources int = 0 OUT
   ,@Resources dbo.ResourceList READONLY
   ,@ResourceWriteClaims dbo.ResourceWriteClaimList READONLY
   ,@ReferenceSearchParams dbo.ReferenceSearchParamList READONLY
   ,@TokenSearchParamHighCards dbo.TokenSearchParamList READONLY
   ,@TokenSearchParams dbo.TokenSearchParamList READONLY
   ,@TokenTexts dbo.TokenTextList READONLY
   ,@StringSearchParams dbo.StringSearchParamList READONLY
   ,@UriSearchParams dbo.UriSearchParamList READONLY
   ,@NumberSearchParams dbo.NumberSearchParamList READONLY
   ,@QuantitySearchParams dbo.QuantitySearchParamList READONLY
   ,@DateTimeSearchParams dbo.DateTimeSearchParamList READONLY
   ,@ReferenceTokenCompositeSearchParams dbo.ReferenceTokenCompositeSearchParamList READONLY
   ,@TokenTokenCompositeSearchParams dbo.TokenTokenCompositeSearchParamList READONLY
   ,@TokenDateTimeCompositeSearchParams dbo.TokenDateTimeCompositeSearchParamList READONLY
   ,@TokenQuantityCompositeSearchParams dbo.TokenQuantityCompositeSearchParamList READONLY
   ,@TokenStringCompositeSearchParams dbo.TokenStringCompositeSearchParamList READONLY
   ,@TokenNumberNumberCompositeSearchParams dbo.TokenNumberNumberCompositeSearchParamList READONLY
AS
set nocount on
DECLARE @st datetime = getUTCdate()
       ,@SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(200) = isnull((SELECT 'RT=['+convert(varchar,min(ResourceTypeId))+','+convert(varchar,max(ResourceTypeId))+'] Sur=['+convert(varchar,min(ResourceSurrogateId))+','+convert(varchar,max(ResourceSurrogateId))+'] V='+convert(varchar,max(Version))+' Rows='+convert(varchar,count(*)) FROM @Resources),'Input=Empty')
       ,@Rows int

BEGIN TRY
  DECLARE @Ids TABLE (ResourceTypeId smallint NOT NULL, ResourceSurrogateId bigint NOT NULL)

  BEGIN TRANSACTION

  -- Update the search parameter hash value in the main resource table
  UPDATE B
    SET SearchParamHash = A.SearchParamHash
    OUTPUT deleted.ResourceTypeId, deleted.ResourceSurrogateId INTO @Ids 
    FROM @Resources A JOIN dbo.Resource B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
    WHERE B.IsHistory = 0
  SET @Rows = @@rowcount

  -- First, delete all the search params of the resources to reindex.
  DELETE FROM B FROM @Ids A JOIN dbo.ResourceWriteClaim B ON B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A JOIN dbo.ReferenceSearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A JOIN dbo.TokenSearchParamHighCard B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A JOIN dbo.TokenSearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A JOIN dbo.TokenText B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A JOIN dbo.StringSearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A JOIN dbo.UriSearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A JOIN dbo.NumberSearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A JOIN dbo.QuantitySearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A JOIN dbo.DateTimeSearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A JOIN dbo.ReferenceTokenCompositeSearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A JOIN dbo.TokenTokenCompositeSearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A JOIN dbo.TokenDateTimeCompositeSearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A JOIN dbo.TokenQuantityCompositeSearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A JOIN dbo.TokenStringCompositeSearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A JOIN dbo.TokenNumberNumberCompositeSearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId

  -- Next, insert all the new search params.
  INSERT INTO dbo.ResourceWriteClaim 
         ( ResourceSurrogateId, ClaimTypeId, ClaimValue )
    SELECT ResourceSurrogateId, ClaimTypeId, ClaimValue
      FROM @ResourceWriteClaims

  INSERT INTO dbo.ReferenceSearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion
      FROM @ReferenceSearchParams

  IF EXISTS (SELECT * FROM @TokenSearchParamHighCards)
    INSERT INTO dbo.TokenSearchParamHighCard 
           ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow )
      SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow
        FROM @TokenSearchParamHighCards
  ELSE
    INSERT INTO dbo.TokenSearchParamHighCard 
           ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow )
      SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow
        FROM @TokenSearchParams
        WHERE SearchParamId IN (SELECT SearchParamId FROM dbo.SearchParam WHERE Uri LIKE '%identifier' OR Uri LIKE '%phone' OR Uri LIKE '%telecom')

  INSERT INTO dbo.TokenSearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow
      FROM @TokenSearchParams

  INSERT INTO dbo.TokenText 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Text )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Text
      FROM @TokenTexts

  INSERT INTO dbo.StringSearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax
      FROM @StringSearchParams

  INSERT INTO dbo.UriSearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri
      FROM @UriSearchParams

  INSERT INTO dbo.NumberSearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue
      FROM @NumberSearchParams

  INSERT INTO dbo.QuantitySearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue
      FROM @QuantitySearchParams

  INSERT INTO dbo.DateTimeSearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax
      FROM @DateTimeSearchParams

  INSERT INTO dbo.ReferenceTokenCompositeSearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2 )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2
      FROM @ReferenceTokenCompositeSearchParams

  INSERT INTO dbo.TokenTokenCompositeSearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2 )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2
      FROM @TokenTokenCompositeSearchParams

  INSERT INTO dbo.TokenDateTimeCompositeSearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2 )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2
      FROM @TokenDateTimeCompositeSearchParams

  INSERT INTO dbo.TokenQuantityCompositeSearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2 )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2
      FROM @TokenQuantityCompositeSearchParams

  INSERT INTO dbo.TokenStringCompositeSearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2 )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2
      FROM @TokenStringCompositeSearchParams

  INSERT INTO dbo.TokenNumberNumberCompositeSearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange
      FROM @TokenNumberNumberCompositeSearchParams

  COMMIT TRANSACTION

  SET @FailedResources = (SELECT count(*) FROM @Resources) - @Rows

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
ALTER PROCEDURE dbo.HardDeleteResource
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

  DECLARE @SurrogateIds TABLE (ResourceSurrogateId BIGINT NOT NULL)

  IF @IsResourceChangeCaptureEnabled = 1 AND NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = 'InvisibleHistory.IsEnabled' AND Number = 0)
    UPDATE dbo.Resource
      SET IsDeleted = 1
         ,RawResource = 0xF -- invisible value
         ,SearchParamHash = NULL
         ,HistoryTransactionId = @TransactionId
      OUTPUT deleted.ResourceSurrogateId INTO @SurrogateIds
      WHERE ResourceTypeId = @ResourceTypeId
        AND ResourceId = @ResourceId
        AND (@KeepCurrentVersion = 0 OR IsHistory = 1)
        AND RawResource <> 0xF
  ELSE
    DELETE dbo.Resource
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
    DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.TokenSearchParamHardCard B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
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
IF object_id('tempdb..#SearchParams') IS NOT NULL DROP TABLE #SearchParams
GO
set nocount on

INSERT INTO dbo.Parameters (Id, Char) SELECT 'TokenSearchParamHighCardPopulation', 'LogEvent'
EXECUTE dbo.LogEvent @Process='TokenSearchParamHighCardPopulation',@Status='Start'

SELECT SearchParamId 
  INTO #SearchParams
  FROM dbo.SearchParam 
  WHERE Uri LIKE '%identifier' OR Uri LIKE '%phone' OR Uri LIKE '%telecom'

DECLARE @Types TABLE (ResourceTypeId smallint PRIMARY KEY, Name varchar(100))
DECLARE @MaxSurrogateId bigint = 0
       ,@ResourceTypeId smallint
IF NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = 'TokenSearchParamHighCardPopulation.MaxSurrogateId') -- DELETE FROM dbo.Parameters WHERE Id = 'TokenSearchParamHighCardPopulation.MaxSurrogateId'
BEGIN
  DECLARE @MaxSurrogateIdTmp bigint

  INSERT INTO @Types EXECUTE dbo.GetUsedResourceTypes
  WHILE EXISTS (SELECT * FROM @Types)
  BEGIN
    SET @ResourceTypeId = (SELECT TOP 1 ResourceTypeId FROM @Types)
    SET @MaxSurrogateIdTmp = (SELECT max(ResourceSurrogateId) FROM Resource WHERE ResourceTypeId = @ResourceTypeId)
    IF @MaxSurrogateIdTmp > @MaxSurrogateId SET @MaxSurrogateId = @MaxSurrogateIdTmp
    DELETE FROM @Types WHERE ResourceTypeId = @ResourceTypeId
  END
  INSERT INTO dbo.Parameters (Id, Bigint) SELECT 'TokenSearchParamHighCardPopulation.MaxSurrogateId', @MaxSurrogateId
END

SET @MaxSurrogateId = (SELECT Bigint FROM dbo.Parameters WHERE Id = 'TokenSearchParamHighCardPopulation.MaxSurrogateId')
EXECUTE dbo.LogEvent @Process='TokenSearchParamHighCardPopulation',@Status='Run',@Target='@MaxSurrogateId',@Action='Select',@Text=@MaxSurrogateId

DECLARE @Process varchar(100) = 'TokenSearchParamHighCardPopulation'
       ,@Id varchar(100) = 'TokenSearchParamHighCardPopulation.LastProcessed.TypeId.SurrogateId'
       ,@SurrogateId bigint
       ,@RowsToProcess int
       ,@ProcessedResources int
       ,@ReportDate datetime = getUTCdate()
       ,@DummyTop bigint = 9223372036854775807
       ,@Rows int
       ,@CurrentMaxSurrogateId bigint
       ,@LastProcessed varchar(100)
       ,@st datetime

BEGIN TRY
  INSERT INTO dbo.Parameters (Id, Char) SELECT @Id, '0.0' WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = @Id)

  SET @LastProcessed = (SELECT Char FROM dbo.Parameters WHERE Id = @Id)

  INSERT INTO @Types EXECUTE dbo.GetUsedResourceTypes
  EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Target='@Types',@Action='Insert',@Rows=@@rowcount

  SET @ResourceTypeId = substring(@LastProcessed, 1, charindex('.', @LastProcessed) - 1) -- (SELECT value FROM string_split(@LastProcessed, '.', 1) WHERE ordinal = 1)
  SET @SurrogateId = substring(@LastProcessed, charindex('.', @LastProcessed) + 1, 255) -- (SELECT value FROM string_split(@LastProcessed, '.', 1) WHERE ordinal = 2)

  DELETE FROM @Types WHERE ResourceTypeId < @ResourceTypeId
  EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Target='@Types',@Action='Delete',@Rows=@@rowcount

  WHILE EXISTS (SELECT * FROM @Types) -- Processing in ASC order
  BEGIN
    SET @ResourceTypeId = (SELECT TOP 1 ResourceTypeId FROM @Types ORDER BY ResourceTypeId)

    SET @ProcessedResources = 0
    SET @CurrentMaxSurrogateId = 0
    IF NOT EXISTS 
        (SELECT * 
           FROM #SearchParams A
                INNER LOOP JOIN dbo.TokenSearchParam B ON B.ResourceTypeId = @ResourceTypeId AND B.SearchParamId = A.SearchParamId
        )
    BEGIN
      SET @CurrentMaxSurrogateId = NULL
      EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Mode=@ResourceTypeId,@Target='TokenSearchParam',@Action='Select',@Rows=0,@Text='Check high card values'
    END

    WHILE @CurrentMaxSurrogateId IS NOT NULL
    BEGIN
      BEGIN TRANSACTION

      SET @CurrentMaxSurrogateId = NULL
      SELECT @CurrentMaxSurrogateId = max(ResourceSurrogateId), @RowsToProcess = count(*)
        FROM (SELECT TOP 5000 ResourceSurrogateId -- 5000 is max to avoid lock escalation
                FROM dbo.Resource (HOLDLOCK) -- Hold locks so other write transactions cannot change data
                WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId > @SurrogateId AND ResourceSurrogateId <= @MaxSurrogateId 
                ORDER BY ResourceSurrogateId
             ) A

      IF @CurrentMaxSurrogateId IS NOT NULL
      BEGIN
        SET @LastProcessed = convert(varchar,@ResourceTypeId)+'.'+convert(varchar,@CurrentMaxSurrogateId)

        SET @st = getUTCdate()
        INSERT INTO dbo.TokenSearchParamHighCard 
               ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow )
          SELECT TOP (@DummyTop) 
                 ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow
            FROM dbo.TokenSearchParam A
            WHERE ResourceTypeId = @ResourceTypeId
              AND ResourceSurrogateId > @SurrogateId
              AND ResourceSurrogateId <= @CurrentMaxSurrogateId
              AND SearchParamId IN (SELECT SearchParamId FROM #SearchParams)
              AND NOT EXISTS (SELECT * FROM dbo.TokenSearchParamHighCard B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId)
            OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
        EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Mode=@LastProcessed,@Target='TokenSearchParamHighCard',@Action='Insert',@Rows=@@rowcount,@Text=@RowsToProcess,@Start=@st

        UPDATE dbo.Parameters SET Char = @LastProcessed WHERE Id = @Id

        COMMIT TRANSACTION

        SET @SurrogateId = @CurrentMaxSurrogateId

        SET @ProcessedResources += @RowsToProcess

        IF datediff(second, @ReportDate, getUTCdate()) > 60
        BEGIN
          EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Mode=@LastProcessed,@Target='Resource',@Action='Select',@Rows=@ProcessedResources
          SET @ReportDate = getUTCdate()
          SET @ProcessedResources = 0
        END
      END
      ELSE
      BEGIN
        COMMIT TRANSACTION
        SET @LastProcessed = convert(varchar,@ResourceTypeId)+'.'+convert(varchar,@MaxSurrogateId)
        UPDATE dbo.Parameters SET Char = @LastProcessed WHERE Id = @Id
      END
    END

    IF @ProcessedResources > 0
      EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Mode=@LastProcessed,@Target='Resource',@Action='Select',@Rows=@ProcessedResources

    DELETE FROM @Types WHERE ResourceTypeId = @ResourceTypeId

    SET @SurrogateId = 0
  END
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@Process,@Status='Error';
  THROW
END CATCH

EXECUTE dbo.LogEvent @Process='TokenSearchParamHighCardPopulation',@Status='End'
