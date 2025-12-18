/***************************************************************
* Migration: 102
* Description: Add new column DecompressedLength to Resource table to store the size of decompressed resource data.

To avoid impacting existing functionality, the new column is added as nullable.
To make this change both backward and forward compatible,
A new type ResourceList_Temp will be created to include this new column.
MergeResources, UpdateResourceSearchParams and CaptureResourceIdsForChanges SPs will be changed to use both new and old types in iteration1.
DecompressedLength for existing resources will get backfilled in next iteration.
***************************/

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = object_id('Resource') AND name = 'DecompressedLength')
ALTER TABLE Resource ADD DecompressedLength INT NULL;
GO

IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'ResourceList_Temp')
CREATE TYPE dbo.ResourceList_Temp AS TABLE
(
    ResourceTypeId       smallint            NOT NULL
   ,ResourceSurrogateId  bigint              NOT NULL
   ,ResourceId           varchar(64)         COLLATE Latin1_General_100_CS_AS NOT NULL
   ,Version              int                 NOT NULL
   ,HasVersionToCompare  bit                 NOT NULL -- in case of multiple versions per resource indicates that row contains (existing version + 1) value
   ,IsDeleted            bit                 NOT NULL
   ,IsHistory            bit                 NOT NULL
   ,KeepHistory          bit                 NOT NULL
   ,RawResource          varbinary(max)      NOT NULL
   ,IsRawResourceMetaSet bit                 NOT NULL
   ,RequestMethod        varchar(10)         NULL
   ,SearchParamHash      varchar(64)         NULL
   ,DecompressedLength     INT                 NULL

    PRIMARY KEY (ResourceTypeId, ResourceSurrogateId)
   ,UNIQUE (ResourceTypeId, ResourceId, Version)
)
GO

CREATE or ALTER PROCEDURE dbo.MergeResources
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
   ,@Resources_Temp dbo.ResourceList_Temp READONLY
   ,@ResourceWriteClaims dbo.ResourceWriteClaimList READONLY
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
       ,@SP varchar(100) = object_name(@@procid)
       ,@DummyTop bigint = 9223372036854775807
       ,@InitialTranCount int = @@trancount
       ,@IsRetry bit = 0
       ,@HasDecompressedLength bit = 0

-- Create working table and populate from appropriate source
DECLARE @WorkingResources TABLE
(
    ResourceTypeId       smallint            NOT NULL
   ,ResourceSurrogateId  bigint              NOT NULL
   ,ResourceId           varchar(64)         COLLATE Latin1_General_100_CS_AS NOT NULL
   ,Version              int                 NOT NULL
   ,HasVersionToCompare  bit                 NOT NULL -- in case of multiple versions per resource indicates that row contains (existing version + 1) value
   ,IsDeleted            bit                 NOT NULL
   ,IsHistory            bit                 NOT NULL
   ,KeepHistory          bit                 NOT NULL
   ,RawResource          varbinary(max)      NOT NULL
   ,IsRawResourceMetaSet bit                 NOT NULL
   ,RequestMethod        varchar(10)         NULL
   ,SearchParamHash      varchar(64)         NULL
   ,DecompressedLength     INT                 NULL
)

IF EXISTS (SELECT 1 FROM @Resources_Temp)
BEGIN
    SET @HasDecompressedLength = 1
    INSERT INTO @WorkingResources
        (ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash, HasVersionToCompare, KeepHistory, DecompressedLength)
    SELECT ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash, HasVersionToCompare, KeepHistory, DecompressedLength
    FROM @Resources_Temp
END
ELSE
BEGIN
    INSERT INTO @WorkingResources
        (ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash, HasVersionToCompare, KeepHistory, DecompressedLength)
    SELECT ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash, HasVersionToCompare, KeepHistory, NULL
    FROM @Resources
END

DECLARE @Mode varchar(200) = isnull((SELECT 'RT=['+convert(varchar,min(ResourceTypeId))+','+convert(varchar,max(ResourceTypeId))+'] Sur=['+convert(varchar,min(ResourceSurrogateId))+','+convert(varchar,max(ResourceSurrogateId))+'] V='+convert(varchar,max(Version))+' Rows='+convert(varchar,count(*)) FROM @WorkingResources),'Input=Empty')
SET @Mode += ' E='+convert(varchar,@RaiseExceptionOnConflict)+' CC='+convert(varchar,@IsResourceChangeCaptureEnabled)+' IT='+convert(varchar,@InitialTranCount)+' T='+isnull(convert(varchar,@TransactionId),'NULL')+' ST='+convert(varchar,@SingleTransaction)

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

  -- perform retry check in transaction to hold locks
  IF @InitialTranCount = 0
  BEGIN
    IF EXISTS (SELECT * -- This extra statement avoids putting range locks when we don't need them
                 FROM @WorkingResources A JOIN dbo.Resource B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
                 --WHERE B.IsHistory = 0 -- With this clause wrong plans are created on empty/small database. Commented until resource separation is in place.
              )
    BEGIN
      BEGIN TRANSACTION

      INSERT INTO @Existing
              (  ResourceTypeId,           SurrogateId )
        SELECT B.ResourceTypeId, B.ResourceSurrogateId
          FROM (SELECT TOP (@DummyTop) * FROM @WorkingResources) A
               JOIN dbo.Resource B WITH (ROWLOCK, HOLDLOCK) ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
          WHERE B.IsHistory = 0
            AND B.ResourceId = A.ResourceId
            AND B.Version = A.Version
          OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
    
      -- If all resources being merged are already in the resource table with updated versions this is a retry and only search parameters need to be updated.
      IF @@rowcount = (SELECT count(*) FROM @WorkingResources) SET @IsRetry = 1

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
        FROM (SELECT TOP (@DummyTop) * FROM @WorkingResources WHERE HasVersionToCompare = 1) A
             LEFT OUTER JOIN dbo.Resource B -- WITH (UPDLOCK, HOLDLOCK) These locking hints cause deadlocks and are not needed. Racing might lead to tries to insert dups in unique index (with version key), but it will fail anyway, and in no case this will cause incorrect data saved.
               ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId AND B.IsHistory = 0
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))

    -- Consider surrogate id out of allignment as a conflict
    IF @RaiseExceptionOnConflict = 1 AND EXISTS (SELECT * FROM @ResourceInfos WHERE (PreviousVersion IS NOT NULL AND Version <= PreviousVersion) OR (PreviousSurrogateId IS NOT NULL AND SurrogateId <= PreviousSurrogateId))
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
             ,DecompressedLength = 0
          WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId AND KeepHistory = 0)
      ELSE
        DELETE FROM dbo.Resource WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId AND KeepHistory = 0)
      SET @AffectedRows += @@rowcount

      -- Cleanup search parameters for historical versions
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

    IF @HasDecompressedLength = 1
    BEGIN
        INSERT INTO dbo.Resource 
               ( ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash,  TransactionId, DecompressedLength )
        SELECT ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash, @TransactionId, DecompressedLength
            FROM @WorkingResources
    END
    ELSE
    BEGIN
        INSERT INTO dbo.Resource 
               ( ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash,  TransactionId )
        SELECT ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash, @TransactionId
            FROM @WorkingResources
    END
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
          AND NOT EXISTS (SELECT * FROM dbo.TokenText C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.StringSearchParam 
           ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax )
      SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax
        FROM (SELECT TOP (@DummyTop) * FROM @StringSearchParams) A
        WHERE EXISTS (SELECT * FROM @Existing B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.SurrogateId = A.ResourceSurrogateId)
          AND NOT EXISTS (SELECT * FROM dbo.StringSearchParam C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
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
          AND NOT EXISTS (SELECT * FROM dbo.DateTimeSearchParam C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.ReferenceTokenCompositeSearchParam 
           ( ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2 )
      SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2
        FROM (SELECT TOP (@DummyTop) * FROM @ReferenceTokenCompositeSearchParams) A
        WHERE EXISTS (SELECT * FROM @Existing B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.SurrogateId = A.ResourceSurrogateId)
          AND NOT EXISTS (SELECT * FROM dbo.ReferenceTokenCompositeSearchParam C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
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
  EXECUTE dbo.CaptureResourceIdsForChanges @Resources = @Resources, @Resources_Temp = @Resources_Temp
  
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

CREATE or ALTER PROCEDURE dbo.CaptureResourceIdsForChanges 
    @Resources dbo.ResourceList READONLY,
    @Resources_Temp dbo.ResourceList_Temp READONLY
AS
set nocount on
-- This procedure is intended to be called from the MergeResources procedure and relies on its transaction logic

IF EXISTS (SELECT 1 FROM @Resources_Temp)
BEGIN
    INSERT INTO dbo.ResourceChangeData 
           ( ResourceId, ResourceTypeId, ResourceVersion, ResourceChangeTypeId )
      SELECT ResourceId, ResourceTypeId, Version, CASE WHEN IsDeleted = 1 THEN 2 WHEN Version > 1 THEN 1 ELSE 0 END
        FROM @Resources_Temp
        WHERE IsHistory = 0
END
ELSE
BEGIN
    INSERT INTO dbo.ResourceChangeData 
           ( ResourceId, ResourceTypeId, ResourceVersion, ResourceChangeTypeId )
      SELECT ResourceId, ResourceTypeId, Version, CASE WHEN IsDeleted = 1 THEN 2 WHEN Version > 1 THEN 1 ELSE 0 END
        FROM @Resources
        WHERE IsHistory = 0
END
GO

CREATE OR ALTER PROCEDURE dbo.UpdateResourceSearchParams
    @FailedResources int = 0 OUT
   ,@Resources dbo.ResourceList READONLY
   ,@Resources_Temp dbo.ResourceList_Temp READONLY
   ,@ResourceWriteClaims dbo.ResourceWriteClaimList READONLY
   ,@ReferenceSearchParams dbo.ReferenceSearchParamList READONLY
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

-- Create working table and populate from appropriate source
DECLARE @WorkingResources TABLE
(
    ResourceTypeId       smallint            NOT NULL
   ,ResourceSurrogateId  bigint              NOT NULL
   ,ResourceId           varchar(64)         COLLATE Latin1_General_100_CS_AS NOT NULL
   ,Version              int                 NOT NULL
   ,HasVersionToCompare  bit                 NOT NULL -- in case of multiple versions per resource indicates that row contains (existing version + 1) value
   ,IsDeleted            bit                 NOT NULL
   ,IsHistory            bit                 NOT NULL
   ,KeepHistory          bit                 NOT NULL
   ,RawResource          varbinary(max)      NOT NULL
   ,IsRawResourceMetaSet bit                 NOT NULL
   ,RequestMethod        varchar(10)         NULL
   ,SearchParamHash      varchar(64)         NULL
   ,DecompressedLength     INT                 NULL
)

IF EXISTS (SELECT 1 FROM @Resources_Temp)
BEGIN
    INSERT INTO @WorkingResources
        (ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash, HasVersionToCompare, KeepHistory, DecompressedLength)
    SELECT ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash, HasVersionToCompare, KeepHistory, DecompressedLength
    FROM @Resources_Temp
END
ELSE
BEGIN
    INSERT INTO @WorkingResources
        (ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash, HasVersionToCompare, KeepHistory, DecompressedLength)
    SELECT ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash, HasVersionToCompare, KeepHistory, NULL
    FROM @Resources
END

DECLARE @st datetime = getUTCdate()
       ,@SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(200) = isnull((SELECT 'RT=['+convert(varchar,min(ResourceTypeId))+','+convert(varchar,max(ResourceTypeId))+'] Sur=['+convert(varchar,min(ResourceSurrogateId))+','+convert(varchar,max(ResourceSurrogateId))+'] V='+convert(varchar,max(Version))+' Rows='+convert(varchar,count(*)) FROM @WorkingResources),'Input=Empty')
       ,@Rows int
       ,@ReferenceSearchParamsCurrent dbo.ReferenceSearchParamList
       ,@ReferenceSearchParamsDelete dbo.ReferenceSearchParamList
       ,@ReferenceSearchParamsInsert dbo.ReferenceSearchParamList
       ,@TokenSearchParamsCurrent dbo.TokenSearchParamList
       ,@TokenSearchParamsDelete dbo.TokenSearchParamList
       ,@TokenSearchParamsInsert dbo.TokenSearchParamList
       ,@TokenTextsCurrent dbo.TokenTextList
       ,@TokenTextsDelete dbo.TokenTextList
       ,@TokenTextsInsert dbo.TokenTextList
       ,@StringSearchParamsCurrent dbo.StringSearchParamList
       ,@StringSearchParamsDelete dbo.StringSearchParamList
       ,@StringSearchParamsInsert dbo.StringSearchParamList
       ,@UriSearchParamsCurrent dbo.UriSearchParamList
       ,@UriSearchParamsDelete dbo.UriSearchParamList
       ,@UriSearchParamsInsert dbo.UriSearchParamList
       ,@NumberSearchParamsCurrent dbo.NumberSearchParamList
       ,@NumberSearchParamsDelete dbo.NumberSearchParamList
       ,@NumberSearchParamsInsert dbo.NumberSearchParamList
       ,@QuantitySearchParamsCurrent dbo.QuantitySearchParamList
       ,@QuantitySearchParamsDelete dbo.QuantitySearchParamList
       ,@QuantitySearchParamsInsert dbo.QuantitySearchParamList
       ,@DateTimeSearchParamsCurrent dbo.DateTimeSearchParamList
       ,@DateTimeSearchParamsDelete dbo.DateTimeSearchParamList
       ,@DateTimeSearchParamsInsert dbo.DateTimeSearchParamList
       ,@ReferenceTokenCompositeSearchParamsCurrent dbo.ReferenceTokenCompositeSearchParamList
       ,@ReferenceTokenCompositeSearchParamsDelete dbo.ReferenceTokenCompositeSearchParamList
       ,@ReferenceTokenCompositeSearchParamsInsert dbo.ReferenceTokenCompositeSearchParamList
       ,@TokenTokenCompositeSearchParamsCurrent dbo.TokenTokenCompositeSearchParamList
       ,@TokenTokenCompositeSearchParamsDelete dbo.TokenTokenCompositeSearchParamList
       ,@TokenTokenCompositeSearchParamsInsert dbo.TokenTokenCompositeSearchParamList
       ,@TokenDateTimeCompositeSearchParamsCurrent dbo.TokenDateTimeCompositeSearchParamList
       ,@TokenDateTimeCompositeSearchParamsDelete dbo.TokenDateTimeCompositeSearchParamList
       ,@TokenDateTimeCompositeSearchParamsInsert dbo.TokenDateTimeCompositeSearchParamList
       ,@TokenQuantityCompositeSearchParamsCurrent dbo.TokenQuantityCompositeSearchParamList
       ,@TokenQuantityCompositeSearchParamsDelete dbo.TokenQuantityCompositeSearchParamList
       ,@TokenQuantityCompositeSearchParamsInsert dbo.TokenQuantityCompositeSearchParamList
       ,@TokenStringCompositeSearchParamsCurrent dbo.TokenStringCompositeSearchParamList
       ,@TokenStringCompositeSearchParamsDelete dbo.TokenStringCompositeSearchParamList
       ,@TokenStringCompositeSearchParamsInsert dbo.TokenStringCompositeSearchParamList
       ,@TokenNumberNumberCompositeSearchParamsCurrent dbo.TokenNumberNumberCompositeSearchParamList
       ,@TokenNumberNumberCompositeSearchParamsDelete dbo.TokenNumberNumberCompositeSearchParamList
       ,@TokenNumberNumberCompositeSearchParamsInsert dbo.TokenNumberNumberCompositeSearchParamList
       ,@ResourceWriteClaimsCurrent dbo.ResourceWriteClaimList
       ,@ResourceWriteClaimsDelete dbo.ResourceWriteClaimList
       ,@ResourceWriteClaimsInsert dbo.ResourceWriteClaimList

BEGIN TRY
  DECLARE @Ids TABLE (ResourceTypeId smallint NOT NULL, ResourceSurrogateId bigint NOT NULL)

  BEGIN TRANSACTION

  -- Update the search parameter hash value in the main resource table
  UPDATE B
    SET SearchParamHash = A.SearchParamHash
    OUTPUT deleted.ResourceTypeId, deleted.ResourceSurrogateId INTO @Ids 
    FROM @WorkingResources A JOIN dbo.Resource B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
    WHERE B.IsHistory = 0
  SET @Rows = @@rowcount

  -- ResourceWriteClaim - Incremental update pattern
  INSERT INTO @ResourceWriteClaimsCurrent
         ( ResourceSurrogateId, ClaimTypeId, ClaimValue )
    SELECT A.ResourceSurrogateId, ClaimTypeId, ClaimValue
      FROM dbo.ResourceWriteClaim A
           JOIN @Ids B ON B.ResourceSurrogateId = A.ResourceSurrogateId
  
  INSERT INTO @ResourceWriteClaimsDelete
         ( ResourceSurrogateId, ClaimTypeId, ClaimValue )
    SELECT ResourceSurrogateId, ClaimTypeId, ClaimValue
      FROM @ResourceWriteClaimsCurrent A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @ResourceWriteClaims B 
                  WHERE B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.ClaimTypeId = A.ClaimTypeId
                    AND B.ClaimValue = A.ClaimValue
              )
      OPTION (HASH JOIN)

  DELETE FROM A
    FROM dbo.ResourceWriteClaim A
    WHERE EXISTS 
            (SELECT * 
               FROM @ResourceWriteClaimsDelete B 
                WHERE B.ResourceSurrogateId = A.ResourceSurrogateId
                  AND B.ClaimTypeId = A.ClaimTypeId
                  AND B.ClaimValue = A.ClaimValue
            )

  INSERT INTO @ResourceWriteClaimsInsert
         ( ResourceSurrogateId, ClaimTypeId, ClaimValue )
    SELECT ResourceSurrogateId, ClaimTypeId, ClaimValue
      FROM @ResourceWriteClaims A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @ResourceWriteClaimsCurrent B 
                  WHERE B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.ClaimTypeId = A.ClaimTypeId
                    AND B.ClaimValue = A.ClaimValue
              )
      OPTION (HASH JOIN)

  -- ReferenceSearchParam - Incremental update pattern
  INSERT INTO @ReferenceSearchParamsCurrent
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion )
    SELECT A.ResourceTypeId, A.ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion
      FROM dbo.ReferenceSearchParam A
           JOIN @Ids B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  
  INSERT INTO @ReferenceSearchParamsDelete
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion
      FROM @ReferenceSearchParamsCurrent A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @ReferenceSearchParams B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.BaseUri = A.BaseUri OR B.BaseUri IS NULL AND A.BaseUri IS NULL)
                    AND (B.ReferenceResourceTypeId = A.ReferenceResourceTypeId OR B.ReferenceResourceTypeId IS NULL AND A.ReferenceResourceTypeId IS NULL)
                    AND B.ReferenceResourceId = A.ReferenceResourceId
              )
      OPTION (HASH JOIN)

  DELETE FROM A
    FROM dbo.ReferenceSearchParam A WITH (INDEX = 1)
    WHERE EXISTS 
            (SELECT * 
               FROM @ReferenceSearchParamsDelete B 
               WHERE B.ResourceTypeId = A.ResourceTypeId 
                 AND B.ResourceSurrogateId = A.ResourceSurrogateId
                 AND B.SearchParamId = A.SearchParamId
                 AND (B.BaseUri = A.BaseUri OR B.BaseUri IS NULL AND A.BaseUri IS NULL)
                 AND (B.ReferenceResourceTypeId = A.ReferenceResourceTypeId OR B.ReferenceResourceTypeId IS NULL AND A.ReferenceResourceTypeId IS NULL)
                 AND B.ReferenceResourceId = A.ReferenceResourceId
             )

  INSERT INTO @ReferenceSearchParamsInsert
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion
      FROM @ReferenceSearchParams A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @ReferenceSearchParamsCurrent B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.BaseUri = A.BaseUri OR B.BaseUri IS NULL AND A.BaseUri IS NULL)
                    AND (B.ReferenceResourceTypeId = A.ReferenceResourceTypeId OR B.ReferenceResourceTypeId IS NULL AND A.ReferenceResourceTypeId IS NULL)
                    AND B.ReferenceResourceId = A.ReferenceResourceId
              )
      OPTION (HASH JOIN)

  -- TokenSearchParam - Incremental update pattern
  INSERT INTO @TokenSearchParamsCurrent
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow )
    SELECT A.ResourceTypeId, A.ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow
      FROM dbo.TokenSearchParam A
           JOIN @Ids B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  
  INSERT INTO @TokenSearchParamsDelete
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow
      FROM @TokenSearchParamsCurrent A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @TokenSearchParams B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.SystemId = A.SystemId OR B.SystemId IS NULL AND A.SystemId IS NULL)
                    AND B.Code = A.Code
                    AND (B.CodeOverflow = A.CodeOverflow OR B.CodeOverflow IS NULL AND A.CodeOverflow IS NULL)
              )
      OPTION (HASH JOIN)

  DELETE FROM A
    FROM dbo.TokenSearchParam A WITH (INDEX = 1)
    WHERE EXISTS 
            (SELECT * 
               FROM @TokenSearchParamsDelete B 
                WHERE B.ResourceTypeId = A.ResourceTypeId 
                  AND B.ResourceSurrogateId = A.ResourceSurrogateId
                  AND B.SearchParamId = A.SearchParamId
                  AND (B.SystemId = A.SystemId OR B.SystemId IS NULL AND A.SystemId IS NULL)
                  AND B.Code = A.Code
                  AND (B.CodeOverflow = A.CodeOverflow OR B.CodeOverflow IS NULL AND A.CodeOverflow IS NULL)
            )

  INSERT INTO @TokenSearchParamsInsert
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow
      FROM @TokenSearchParams A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @TokenSearchParamsCurrent B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.SystemId = A.SystemId OR B.SystemId IS NULL AND A.SystemId IS NULL)
                    AND B.Code = A.Code
                    AND (B.CodeOverflow = A.CodeOverflow OR B.CodeOverflow IS NULL AND A.CodeOverflow IS NULL)
              )
      OPTION (HASH JOIN)

  -- TokenStringCompositeSearchParam - Incremental update pattern
  INSERT INTO @TokenStringCompositeSearchParamsCurrent
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2 )
    SELECT A.ResourceTypeId, A.ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2
      FROM dbo.TokenStringCompositeSearchParam A
           JOIN @Ids B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  
  INSERT INTO @TokenStringCompositeSearchParamsDelete
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2 )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2
      FROM @TokenStringCompositeSearchParamsCurrent A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @TokenStringCompositeSearchParams B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.SystemId1 = A.SystemId1 OR B.SystemId1 IS NULL AND A.SystemId1 IS NULL)
                    AND B.Code1 = A.Code1
                    AND (B.CodeOverflow1 = A.CodeOverflow1 OR B.CodeOverflow1 IS NULL AND A.CodeOverflow1 IS NULL)
                    AND B.Text2 COLLATE Latin1_General_CI_AI = A.Text2
                    AND (B.TextOverflow2 COLLATE Latin1_General_CI_AI = A.TextOverflow2 OR B.TextOverflow2 IS NULL AND A.TextOverflow2 IS NULL)
              )
      OPTION (HASH JOIN)

  DELETE FROM A
    FROM dbo.TokenStringCompositeSearchParam A WITH (INDEX = 1)
    WHERE EXISTS 
            (SELECT * 
               FROM @TokenStringCompositeSearchParamsDelete B 
                WHERE B.ResourceTypeId = A.ResourceTypeId 
                  AND B.ResourceSurrogateId = A.ResourceSurrogateId
                  AND B.SearchParamId = A.SearchParamId
                  AND (B.SystemId1 = A.SystemId1 OR B.SystemId1 IS NULL AND A.SystemId1 IS NULL)
                  AND B.Code1 = A.Code1
                  AND (B.CodeOverflow1 = A.CodeOverflow1 OR B.CodeOverflow1 IS NULL AND A.CodeOverflow1 IS NULL)
                  AND B.Text2 COLLATE Latin1_General_CI_AI = A.Text2
                  AND (B.TextOverflow2 COLLATE Latin1_General_CI_AI = A.TextOverflow2 OR B.TextOverflow2 IS NULL AND A.TextOverflow2 IS NULL)
            )

  INSERT INTO @TokenStringCompositeSearchParamsInsert
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2 )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2
      FROM @TokenStringCompositeSearchParams A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @TokenStringCompositeSearchParamsCurrent B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.SystemId1 = A.SystemId1 OR B.SystemId1 IS NULL AND A.SystemId1 IS NULL)
                    AND B.Code1 = A.Code1
                    AND (B.CodeOverflow1 = A.CodeOverflow1 OR B.CodeOverflow1 IS NULL AND A.CodeOverflow1 IS NULL)
                    AND B.Text2 = A.Text2
                    AND (B.TextOverflow2 = A.TextOverflow2 OR B.TextOverflow2 IS NULL AND A.TextOverflow2 IS NULL)
              )
      OPTION (HASH JOIN)
			  
  -- TokenText - Incremental update pattern
  INSERT INTO @TokenTextsCurrent
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Text )
    SELECT A.ResourceTypeId, A.ResourceSurrogateId, SearchParamId, Text
      FROM dbo.TokenText A
           JOIN @Ids B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  
  INSERT INTO @TokenTextsDelete
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Text )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Text
      FROM @TokenTextsCurrent A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @TokenTexts B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND B.Text = A.Text
              )
      OPTION (HASH JOIN)

  DELETE FROM A
    FROM dbo.TokenText A WITH (INDEX = 1)
    WHERE EXISTS 
            (SELECT * 
               FROM @TokenTextsDelete B 
                WHERE B.ResourceTypeId = A.ResourceTypeId 
                  AND B.ResourceSurrogateId = A.ResourceSurrogateId
                  AND B.SearchParamId = A.SearchParamId
                  AND B.Text = A.Text
            )

  INSERT INTO @TokenTextsInsert
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Text )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Text
      FROM @TokenTexts A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @TokenTextsCurrent B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND B.Text = A.Text
              )
      OPTION (HASH JOIN)

  -- StringSearchParam - Incremental update pattern
  INSERT INTO @StringSearchParamsCurrent
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax )
    SELECT A.ResourceTypeId, A.ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax
      FROM dbo.StringSearchParam A
           JOIN @Ids B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  
  INSERT INTO @StringSearchParamsDelete
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax
      FROM @StringSearchParamsCurrent A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @StringSearchParams B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND B.Text = A.Text
                    AND (B.TextOverflow = A.TextOverflow OR B.TextOverflow IS NULL AND A.TextOverflow IS NULL)
                    AND B.IsMin = A.IsMin
                    AND B.IsMax = A.IsMax
              )
      OPTION (HASH JOIN)

  DELETE FROM A
    FROM dbo.StringSearchParam A WITH (INDEX = 1)
    WHERE EXISTS 
            (SELECT * 
               FROM @StringSearchParamsDelete B 
                WHERE B.ResourceTypeId = A.ResourceTypeId 
                  AND B.ResourceSurrogateId = A.ResourceSurrogateId
                  AND B.SearchParamId = A.SearchParamId
                  AND B.Text = A.Text
                  AND (B.TextOverflow = A.TextOverflow OR B.TextOverflow IS NULL AND A.TextOverflow IS NULL)
                  AND B.IsMin = A.IsMin
                  AND B.IsMax = A.IsMax
            )

  INSERT INTO @StringSearchParamsInsert
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax
      FROM @StringSearchParams A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @StringSearchParamsCurrent B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND B.Text = A.Text
                    AND (B.TextOverflow = A.TextOverflow OR B.TextOverflow IS NULL AND A.TextOverflow IS NULL)
                    AND B.IsMin = A.IsMin
                    AND B.IsMax = A.IsMax
              )
      OPTION (HASH JOIN)

  -- UriSearchParam - Incremental update pattern
  INSERT INTO @UriSearchParamsCurrent
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri )
    SELECT A.ResourceTypeId, A.ResourceSurrogateId, SearchParamId, Uri
      FROM dbo.UriSearchParam A
           JOIN @Ids B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  
  INSERT INTO @UriSearchParamsDelete
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri
      FROM @UriSearchParamsCurrent A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @UriSearchParams B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND B.Uri = A.Uri
              )
      OPTION (HASH JOIN)

  DELETE FROM A
    FROM dbo.UriSearchParam A WITH (INDEX = 1)
    WHERE EXISTS 
            (SELECT * 
               FROM @UriSearchParamsDelete B 
                WHERE B.ResourceTypeId = A.ResourceTypeId 
                  AND B.ResourceSurrogateId = A.ResourceSurrogateId
                  AND B.SearchParamId = A.SearchParamId
                  AND B.Uri = A.Uri
            )

  INSERT INTO @UriSearchParamsInsert
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri
      FROM @UriSearchParams A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @UriSearchParamsCurrent B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND B.Uri = A.Uri
              )
      OPTION (HASH JOIN)
              
  -- NumberSearchParam - Incremental update pattern
  INSERT INTO @NumberSearchParamsCurrent
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue )
    SELECT A.ResourceTypeId, A.ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue
      FROM dbo.NumberSearchParam A
           JOIN @Ids B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  
  INSERT INTO @NumberSearchParamsDelete
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue
      FROM @NumberSearchParamsCurrent A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @NumberSearchParams B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.SingleValue = A.SingleValue OR B.SingleValue IS NULL AND A.SingleValue IS NULL)
                    AND (B.LowValue = A.LowValue OR B.LowValue IS NULL AND A.LowValue IS NULL)
                    AND (B.HighValue = A.HighValue OR B.HighValue IS NULL AND A.HighValue IS NULL)
              )
      OPTION (HASH JOIN)

  DELETE FROM A
    FROM dbo.NumberSearchParam A WITH (INDEX = 1)
    WHERE EXISTS 
            (SELECT * 
               FROM @NumberSearchParamsDelete B 
                WHERE B.ResourceTypeId = A.ResourceTypeId 
                  AND B.ResourceSurrogateId = A.ResourceSurrogateId
                  AND B.SearchParamId = A.SearchParamId
                  AND (B.SingleValue = A.SingleValue OR B.SingleValue IS NULL AND A.SingleValue IS NULL)
                  AND (B.LowValue = A.LowValue OR B.LowValue IS NULL AND A.LowValue IS NULL)
                  AND (B.HighValue = A.HighValue OR B.HighValue IS NULL AND A.HighValue IS NULL)
            )

  INSERT INTO @NumberSearchParamsInsert
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue
      FROM @NumberSearchParams A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @NumberSearchParamsCurrent B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.SingleValue = A.SingleValue OR B.SingleValue IS NULL AND A.SingleValue IS NULL)
                    AND (B.LowValue = A.LowValue OR B.LowValue IS NULL AND A.LowValue IS NULL)
                    AND (B.HighValue = A.HighValue OR B.HighValue IS NULL AND A.HighValue IS NULL)
              )
      OPTION (HASH JOIN)
              
  -- QuantitySearchParam - Incremental update pattern
  INSERT INTO @QuantitySearchParamsCurrent
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue )
    SELECT A.ResourceTypeId, A.ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue
      FROM dbo.QuantitySearchParam A
           JOIN @Ids B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  
  INSERT INTO @QuantitySearchParamsDelete
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue
      FROM @QuantitySearchParamsCurrent A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @QuantitySearchParams B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.SystemId = A.SystemId OR B.SystemId IS NULL AND A.SystemId IS NULL)
                    AND (B.QuantityCodeId = A.QuantityCodeId OR B.QuantityCodeId IS NULL AND A.QuantityCodeId IS NULL)
                    AND (B.SingleValue = A.SingleValue OR B.SingleValue IS NULL AND A.SingleValue IS NULL)
                    AND (B.LowValue = A.LowValue OR B.LowValue IS NULL AND A.LowValue IS NULL)
                    AND (B.HighValue = A.HighValue OR B.HighValue IS NULL AND A.HighValue IS NULL)
              )
      OPTION (HASH JOIN)

  DELETE FROM A
    FROM dbo.QuantitySearchParam A WITH (INDEX = 1)
    WHERE EXISTS 
            (SELECT * 
               FROM @QuantitySearchParamsDelete B 
                WHERE B.ResourceTypeId = A.ResourceTypeId 
                  AND B.ResourceSurrogateId = A.ResourceSurrogateId
                  AND B.SearchParamId = A.SearchParamId
                  AND (B.SystemId = A.SystemId OR B.SystemId IS NULL AND A.SystemId IS NULL)
                  AND (B.QuantityCodeId = A.QuantityCodeId OR B.QuantityCodeId IS NULL AND A.QuantityCodeId IS NULL)
                  AND (B.SingleValue = A.SingleValue OR B.SingleValue IS NULL AND A.SingleValue IS NULL)
                  AND (B.LowValue = A.LowValue OR B.LowValue IS NULL AND A.LowValue IS NULL)
                  AND (B.HighValue = A.HighValue OR B.HighValue IS NULL AND A.HighValue IS NULL)
            )

  INSERT INTO @QuantitySearchParamsInsert
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue
      FROM @QuantitySearchParams A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @QuantitySearchParamsCurrent B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.SystemId = A.SystemId OR B.SystemId IS NULL AND A.SystemId IS NULL)
                    AND (B.QuantityCodeId = A.QuantityCodeId OR B.QuantityCodeId IS NULL AND A.QuantityCodeId IS NULL)
                    AND (B.SingleValue = A.SingleValue OR B.SingleValue IS NULL AND A.SingleValue IS NULL)
                    AND (B.LowValue = A.LowValue OR B.LowValue IS NULL AND A.LowValue IS NULL)
                    AND (B.HighValue = A.HighValue OR B.HighValue IS NULL AND A.HighValue IS NULL)
              )
      OPTION (HASH JOIN)

  -- DateTimeSearchParam - Incremental update pattern
  INSERT INTO @DateTimeSearchParamsCurrent
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax )
    SELECT A.ResourceTypeId, A.ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax
      FROM dbo.DateTimeSearchParam A
           JOIN @Ids B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  
  INSERT INTO @DateTimeSearchParamsDelete
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax
      FROM @DateTimeSearchParamsCurrent A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @DateTimeSearchParams B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND B.StartDateTime = A.StartDateTime
                    AND B.EndDateTime = A.EndDateTime
                    AND B.IsLongerThanADay = A.IsLongerThanADay
                    AND B.IsMin = A.IsMin
                    AND B.IsMax = A.IsMax
              )
      OPTION (HASH JOIN)

  DELETE FROM A
    FROM dbo.DateTimeSearchParam A WITH (INDEX = 1)
    WHERE EXISTS 
            (SELECT * 
               FROM @DateTimeSearchParamsDelete B 
                WHERE B.ResourceTypeId = A.ResourceTypeId 
                  AND B.ResourceSurrogateId = A.ResourceSurrogateId
                  AND B.SearchParamId = A.SearchParamId
                  AND B.StartDateTime = A.StartDateTime
                  AND B.EndDateTime = A.EndDateTime
                  AND B.IsLongerThanADay = A.IsLongerThanADay
                  AND B.IsMin = A.IsMin
                  AND B.IsMax = A.IsMax
            )

  INSERT INTO @DateTimeSearchParamsInsert
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax
      FROM @DateTimeSearchParams A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @DateTimeSearchParamsCurrent B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND B.StartDateTime = A.StartDateTime
                    AND B.EndDateTime = A.EndDateTime
                    AND B.IsLongerThanADay = A.IsLongerThanADay
                    AND B.IsMin = A.IsMin
                    AND B.IsMax = A.IsMax
              )
      OPTION (HASH JOIN)

  -- ReferenceTokenCompositeSearchParam - Incremental update pattern
  INSERT INTO @ReferenceTokenCompositeSearchParamsCurrent
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2 )
    SELECT A.ResourceTypeId, A.ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2
      FROM dbo.ReferenceTokenCompositeSearchParam A
           JOIN @Ids B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  
  INSERT INTO @ReferenceTokenCompositeSearchParamsDelete
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2 )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2
      FROM @ReferenceTokenCompositeSearchParamsCurrent A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @ReferenceTokenCompositeSearchParams B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.BaseUri1 = A.BaseUri1 OR B.BaseUri1 IS NULL AND A.BaseUri1 IS NULL)
                    AND (B.ReferenceResourceTypeId1 = A.ReferenceResourceTypeId1 OR B.ReferenceResourceTypeId1 IS NULL AND A.ReferenceResourceTypeId1 IS NULL)
                    AND B.ReferenceResourceId1 = A.ReferenceResourceId1
                    AND (B.SystemId2 = A.SystemId2 OR B.SystemId2 IS NULL AND A.SystemId2 IS NULL)
                    AND B.Code2 = A.Code2
                    AND (B.CodeOverflow2 = A.CodeOverflow2 OR B.CodeOverflow2 IS NULL AND A.CodeOverflow2 IS NULL)
              )
      OPTION (HASH JOIN)

  DELETE FROM A
    FROM dbo.ReferenceTokenCompositeSearchParam A WITH (INDEX = 1)
    WHERE EXISTS 
            (SELECT * 
               FROM @ReferenceTokenCompositeSearchParamsDelete B 
                WHERE B.ResourceTypeId = A.ResourceTypeId 
                  AND B.ResourceSurrogateId = A.ResourceSurrogateId
                  AND B.SearchParamId = A.SearchParamId
                  AND (B.BaseUri1 = A.BaseUri1 OR B.BaseUri1 IS NULL AND A.BaseUri1 IS NULL)
                  AND (B.ReferenceResourceTypeId1 = A.ReferenceResourceTypeId1 OR B.ReferenceResourceTypeId1 IS NULL AND A.ReferenceResourceTypeId1 IS NULL)
                  AND B.ReferenceResourceId1 = A.ReferenceResourceId1
                  AND (B.SystemId2 = A.SystemId2 OR B.SystemId2 IS NULL AND A.SystemId2 IS NULL)
                  AND B.Code2 = A.Code2
                  AND (B.CodeOverflow2 = A.CodeOverflow2 OR B.CodeOverflow2 IS NULL AND A.CodeOverflow2 IS NULL)
            )

  INSERT INTO @ReferenceTokenCompositeSearchParamsInsert
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2 )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2
      FROM @ReferenceTokenCompositeSearchParams A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @ReferenceTokenCompositeSearchParamsCurrent B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.BaseUri1 = A.BaseUri1 OR B.BaseUri1 IS NULL AND A.BaseUri1 IS NULL)
                    AND (B.ReferenceResourceTypeId1 = A.ReferenceResourceTypeId1 OR B.ReferenceResourceTypeId1 IS NULL AND A.ReferenceResourceTypeId1 IS NULL)
                    AND B.ReferenceResourceId1 = A.ReferenceResourceId1
                    AND (B.SystemId2 = A.SystemId2 OR B.SystemId2 IS NULL AND A.SystemId2 IS NULL)
                    AND B.Code2 = A.Code2
                    AND (B.CodeOverflow2 = A.CodeOverflow2 OR B.CodeOverflow2 IS NULL AND A.CodeOverflow2 IS NULL)
              )
      OPTION (HASH JOIN)

  -- TokenTokenCompositeSearchParam - Incremental update pattern
  INSERT INTO @TokenTokenCompositeSearchParamsCurrent
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2 )
    SELECT A.ResourceTypeId, A.ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2
      FROM dbo.TokenTokenCompositeSearchParam A
           JOIN @Ids B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  
  INSERT INTO @TokenTokenCompositeSearchParamsDelete
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2 )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2
      FROM @TokenTokenCompositeSearchParamsCurrent A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @TokenTokenCompositeSearchParams B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.SystemId1 = A.SystemId1 OR B.SystemId1 IS NULL AND A.SystemId1 IS NULL)
                    AND B.Code1 = A.Code1
                    AND (B.CodeOverflow1 = A.CodeOverflow1 OR B.CodeOverflow1 IS NULL AND A.CodeOverflow1 IS NULL)
                    AND (B.SystemId2 = A.SystemId2 OR B.SystemId2 IS NULL AND A.SystemId2 IS NULL)
                    AND B.Code2 = A.Code2
                    AND (B.CodeOverflow2 = A.CodeOverflow2 OR B.CodeOverflow2 IS NULL AND A.CodeOverflow2 IS NULL)
              )
      OPTION (HASH JOIN)

  DELETE FROM A
    FROM dbo.TokenTokenCompositeSearchParam A WITH (INDEX = 1)
    WHERE EXISTS 
            (SELECT * 
               FROM @TokenTokenCompositeSearchParamsDelete B 
                WHERE B.ResourceTypeId = A.ResourceTypeId 
                  AND B.ResourceSurrogateId = A.ResourceSurrogateId
                  AND B.SearchParamId = A.SearchParamId
                  AND (B.SystemId1 = A.SystemId1 OR B.SystemId1 IS NULL AND A.SystemId1 IS NULL)
                  AND B.Code1 = A.Code1
                  AND (B.CodeOverflow1 = A.CodeOverflow1 OR B.CodeOverflow1 IS NULL AND A.CodeOverflow1 IS NULL)
                  AND (B.SystemId2 = A.SystemId2 OR B.SystemId2 IS NULL AND A.SystemId2 IS NULL)
                  AND B.Code2 = A.Code2
                  AND (B.CodeOverflow2 = A.CodeOverflow2 OR B.CodeOverflow2 IS NULL AND A.CodeOverflow2 IS NULL)
            )

  INSERT INTO @TokenTokenCompositeSearchParamsInsert
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2 )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2
      FROM @TokenTokenCompositeSearchParams A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @TokenTokenCompositeSearchParamsCurrent B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.SystemId1 = A.SystemId1 OR B.SystemId1 IS NULL AND A.SystemId1 IS NULL)
                    AND B.Code1 = A.Code1
                    AND (B.CodeOverflow1 = A.CodeOverflow1 OR B.CodeOverflow1 IS NULL AND A.CodeOverflow1 IS NULL)
                    AND (B.SystemId2 = A.SystemId2 OR B.SystemId2 IS NULL AND A.SystemId2 IS NULL)
                    AND B.Code2 = A.Code2
                    AND (B.CodeOverflow2 = A.CodeOverflow2 OR B.CodeOverflow2 IS NULL AND A.CodeOverflow2 IS NULL)
              )
      OPTION (HASH JOIN)
              
  -- TokenDateTimeCompositeSearchParam - Incremental update pattern
  INSERT INTO @TokenDateTimeCompositeSearchParamsCurrent
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2 )
    SELECT A.ResourceTypeId, A.ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2
      FROM dbo.TokenDateTimeCompositeSearchParam A
           JOIN @Ids B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  
  INSERT INTO @TokenDateTimeCompositeSearchParamsDelete
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2 )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2
      FROM @TokenDateTimeCompositeSearchParamsCurrent A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @TokenDateTimeCompositeSearchParams B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.SystemId1 = A.SystemId1 OR B.SystemId1 IS NULL AND A.SystemId1 IS NULL)
                    AND B.Code1 = A.Code1
                    AND (B.CodeOverflow1 = A.CodeOverflow1 OR B.CodeOverflow1 IS NULL AND A.CodeOverflow1 IS NULL)
                    AND B.StartDateTime2 = A.StartDateTime2
                    AND B.EndDateTime2 = A.EndDateTime2
                    AND B.IsLongerThanADay2 = A.IsLongerThanADay2
              )
      OPTION (HASH JOIN)

  DELETE FROM A
    FROM dbo.TokenDateTimeCompositeSearchParam A WITH (INDEX = 1)
    WHERE EXISTS 
            (SELECT * 
               FROM @TokenDateTimeCompositeSearchParamsDelete B 
                WHERE B.ResourceTypeId = A.ResourceTypeId 
                  AND B.ResourceSurrogateId = A.ResourceSurrogateId
                  AND B.SearchParamId = A.SearchParamId
                  AND (B.SystemId1 = A.SystemId1 OR B.SystemId1 IS NULL AND A.SystemId1 IS NULL)
                  AND B.Code1 = A.Code1
                  AND (B.CodeOverflow1 = A.CodeOverflow1 OR B.CodeOverflow1 IS NULL AND A.CodeOverflow1 IS NULL)
                  AND B.StartDateTime2 = A.StartDateTime2
                  AND B.EndDateTime2 = A.EndDateTime2
                  AND B.IsLongerThanADay2 = A.IsLongerThanADay2
            )

  INSERT INTO @TokenDateTimeCompositeSearchParamsInsert
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2 )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2
      FROM @TokenDateTimeCompositeSearchParams A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @TokenDateTimeCompositeSearchParamsCurrent B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.SystemId1 = A.SystemId1 OR B.SystemId1 IS NULL AND A.SystemId1 IS NULL)
                    AND B.Code1 = A.Code1
                    AND (B.CodeOverflow1 = A.CodeOverflow1 OR B.CodeOverflow1 IS NULL AND A.CodeOverflow1 IS NULL)
                    AND B.StartDateTime2 = A.StartDateTime2
                    AND B.EndDateTime2 = A.EndDateTime2
                    AND B.IsLongerThanADay2 = A.IsLongerThanADay2
              )
      OPTION (HASH JOIN)

  -- TokenQuantityCompositeSearchParam - Incremental update pattern
  INSERT INTO @TokenQuantityCompositeSearchParamsCurrent
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2 )
    SELECT A.ResourceTypeId, A.ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2
      FROM dbo.TokenQuantityCompositeSearchParam A
           JOIN @Ids B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  
  INSERT INTO @TokenQuantityCompositeSearchParamsDelete
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2 )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2
      FROM @TokenQuantityCompositeSearchParamsCurrent A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @TokenQuantityCompositeSearchParams B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.SystemId1 = A.SystemId1 OR B.SystemId1 IS NULL AND A.SystemId1 IS NULL)
                    AND B.Code1 = A.Code1
                    AND (B.CodeOverflow1 = A.CodeOverflow1 OR B.CodeOverflow1 IS NULL AND A.CodeOverflow1 IS NULL)
                    AND (B.SingleValue2 = A.SingleValue2 OR B.SingleValue2 IS NULL AND A.SingleValue2 IS NULL)
                    AND (B.SystemId2 = A.SystemId2 OR B.SystemId2 IS NULL AND A.SystemId2 IS NULL)
                    AND (B.QuantityCodeId2 = A.QuantityCodeId2 OR B.QuantityCodeId2 IS NULL AND A.QuantityCodeId2 IS NULL)
                    AND (B.LowValue2 = A.LowValue2 OR B.LowValue2 IS NULL AND A.LowValue2 IS NULL)
                    AND (B.HighValue2 = A.HighValue2 OR B.HighValue2 IS NULL AND A.HighValue2 IS NULL)
              )
      OPTION (HASH JOIN)

  DELETE FROM A
    FROM dbo.TokenQuantityCompositeSearchParam A WITH (INDEX = 1)
    WHERE EXISTS 
            (SELECT * 
               FROM @TokenQuantityCompositeSearchParamsDelete B 
                WHERE B.ResourceTypeId = A.ResourceTypeId 
                  AND B.ResourceSurrogateId = A.ResourceSurrogateId
                  AND B.SearchParamId = A.SearchParamId
                  AND (B.SystemId1 = A.SystemId1 OR B.SystemId1 IS NULL AND A.SystemId1 IS NULL)
                  AND B.Code1 = A.Code1
                  AND (B.CodeOverflow1 = A.CodeOverflow1 OR B.CodeOverflow1 IS NULL AND A.CodeOverflow1 IS NULL)
                  AND (B.SingleValue2 = A.SingleValue2 OR B.SingleValue2 IS NULL AND A.SingleValue2 IS NULL)
                  AND (B.SystemId2 = A.SystemId2 OR B.SystemId2 IS NULL AND A.SystemId2 IS NULL)
                  AND (B.QuantityCodeId2 = A.QuantityCodeId2 OR B.QuantityCodeId2 IS NULL AND A.QuantityCodeId2 IS NULL)
                  AND (B.LowValue2 = A.LowValue2 OR B.LowValue2 IS NULL AND A.LowValue2 IS NULL)
                  AND (B.HighValue2 = A.HighValue2 OR B.HighValue2 IS NULL AND A.HighValue2 IS NULL)
            )

  INSERT INTO @TokenQuantityCompositeSearchParamsInsert
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2 )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2
      FROM @TokenQuantityCompositeSearchParams A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @TokenQuantityCompositeSearchParamsCurrent B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.SystemId1 = A.SystemId1 OR B.SystemId1 IS NULL AND A.SystemId1 IS NULL)
                    AND B.Code1 = A.Code1
                    AND (B.CodeOverflow1 = A.CodeOverflow1 OR B.CodeOverflow1 IS NULL AND A.CodeOverflow1 IS NULL)
                    AND (B.SingleValue2 = A.SingleValue2 OR B.SingleValue2 IS NULL AND A.SingleValue2 IS NULL)
                    AND (B.SystemId2 = A.SystemId2 OR B.SystemId2 IS NULL AND A.SystemId2 IS NULL)
                    AND (B.QuantityCodeId2 = A.QuantityCodeId2 OR B.QuantityCodeId2 IS NULL AND A.QuantityCodeId2 IS NULL)
                    AND (B.LowValue2 = A.LowValue2 OR B.LowValue2 IS NULL AND A.LowValue2 IS NULL)
                    AND (B.HighValue2 = A.HighValue2 OR B.HighValue2 IS NULL AND A.HighValue2 IS NULL)
              )
      OPTION (HASH JOIN)
  
    -- TokenNumberNumberCompositeSearchParam - Incremental update pattern
  INSERT INTO @TokenNumberNumberCompositeSearchParamsCurrent
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange )
    SELECT A.ResourceTypeId, A.ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange
      FROM dbo.TokenNumberNumberCompositeSearchParam A
           JOIN @Ids B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  
  INSERT INTO @TokenNumberNumberCompositeSearchParamsDelete
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange
      FROM @TokenNumberNumberCompositeSearchParamsCurrent A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @TokenNumberNumberCompositeSearchParams B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.SystemId1 = A.SystemId1 OR B.SystemId1 IS NULL AND A.SystemId1 IS NULL)
                    AND B.Code1 = A.Code1
                    AND (B.CodeOverflow1 = A.CodeOverflow1 OR B.CodeOverflow1 IS NULL AND A.CodeOverflow1 IS NULL)
                    AND (B.SingleValue2 = A.SingleValue2 OR B.SingleValue2 IS NULL AND A.SingleValue2 IS NULL)
                    AND (B.LowValue2 = A.LowValue2 OR B.LowValue2 IS NULL AND A.LowValue2 IS NULL)
                    AND (B.HighValue2 = A.HighValue2 OR B.HighValue2 IS NULL AND A.HighValue2 IS NULL)
                    AND (B.SingleValue3 = A.SingleValue3 OR B.SingleValue3 IS NULL AND A.SingleValue3 IS NULL)
                    AND (B.LowValue3 = A.LowValue3 OR B.LowValue3 IS NULL AND A.LowValue3 IS NULL)
                    AND (B.HighValue3 = A.HighValue3 OR B.HighValue3 IS NULL AND A.HighValue3 IS NULL)
                    AND B.HasRange = A.HasRange
              )
      OPTION (HASH JOIN)

  DELETE FROM A
    FROM dbo.TokenNumberNumberCompositeSearchParam A WITH (INDEX = 1)
    WHERE EXISTS 
            (SELECT * 
               FROM @TokenNumberNumberCompositeSearchParamsDelete B 
                WHERE B.ResourceTypeId = A.ResourceTypeId 
                  AND B.ResourceSurrogateId = A.ResourceSurrogateId
                  AND B.SearchParamId = A.SearchParamId
                  AND (B.SystemId1 = A.SystemId1 OR B.SystemId1 IS NULL AND A.SystemId1 IS NULL)
                  AND B.Code1 = A.Code1
                  AND (B.CodeOverflow1 = A.CodeOverflow1 OR B.CodeOverflow1 IS NULL AND A.CodeOverflow1 IS NULL)
                  AND (B.SingleValue2 = A.SingleValue2 OR B.SingleValue2 IS NULL AND A.SingleValue2 IS NULL)
                  AND (B.LowValue2 = A.LowValue2 OR B.LowValue2 IS NULL AND A.LowValue2 IS NULL)
                  AND (B.HighValue2 = A.HighValue2 OR B.HighValue2 IS NULL AND A.HighValue2 IS NULL)
                  AND (B.SingleValue3 = A.SingleValue3 OR B.SingleValue3 IS NULL AND A.SingleValue3 IS NULL)
                  AND (B.LowValue3 = A.LowValue3 OR B.LowValue3 IS NULL AND A.LowValue3 IS NULL)
                  AND (B.HighValue3 = A.HighValue3 OR B.HighValue3 IS NULL AND A.HighValue3 IS NULL)
                  AND B.HasRange = A.HasRange
            )

  INSERT INTO @TokenNumberNumberCompositeSearchParamsInsert
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange
      FROM @TokenNumberNumberCompositeSearchParams A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @TokenNumberNumberCompositeSearchParamsCurrent B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.SystemId1 = A.SystemId1 OR B.SystemId1 IS NULL AND A.SystemId1 IS NULL)
                    AND B.Code1 = A.Code1
                    AND (B.CodeOverflow1 = A.CodeOverflow1 OR B.CodeOverflow1 IS NULL AND A.CodeOverflow1 IS NULL)
                    AND (B.SingleValue2 = A.SingleValue2 OR B.SingleValue2 IS NULL AND A.SingleValue2 IS NULL)
                    AND (B.LowValue2 = A.LowValue2 OR B.LowValue2 IS NULL AND A.LowValue2 IS NULL)
                    AND (B.HighValue2 = A.HighValue2 OR B.HighValue2 IS NULL AND A.HighValue2 IS NULL)
                    AND (B.SingleValue3 = A.SingleValue3 OR B.SingleValue3 IS NULL AND A.SingleValue3 IS NULL)
                    AND (B.LowValue3 = A.LowValue3 OR B.LowValue3 IS NULL AND A.LowValue3 IS NULL)
                    AND (B.HighValue3 = A.HighValue3 OR B.HighValue3 IS NULL AND A.HighValue3 IS NULL)
                    AND B.HasRange = A.HasRange
              )
      OPTION (HASH JOIN)

  -- Next, insert all the new search params.
  INSERT INTO dbo.ResourceWriteClaim 
         ( ResourceSurrogateId, ClaimTypeId, ClaimValue )
    SELECT ResourceSurrogateId, ClaimTypeId, ClaimValue
      FROM @ResourceWriteClaimsInsert

  INSERT INTO dbo.ReferenceSearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion
      FROM @ReferenceSearchParamsInsert

  INSERT INTO dbo.TokenSearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow
      FROM @TokenSearchParamsInsert

  INSERT INTO dbo.TokenStringCompositeSearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2 )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2
      FROM @TokenStringCompositeSearchParamsInsert
  
  INSERT INTO dbo.TokenText 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Text )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Text
      FROM @TokenTextsInsert

  INSERT INTO dbo.StringSearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax
      FROM @StringSearchParamsInsert

  INSERT INTO dbo.UriSearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri
      FROM @UriSearchParamsInsert

  INSERT INTO dbo.NumberSearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue
      FROM @NumberSearchParamsInsert

  INSERT INTO dbo.QuantitySearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue
      FROM @QuantitySearchParamsInsert

  INSERT INTO dbo.DateTimeSearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax
      FROM @DateTimeSearchParamsInsert

  INSERT INTO dbo.ReferenceTokenCompositeSearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2 )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2
      FROM @ReferenceTokenCompositeSearchParamsInsert

  INSERT INTO dbo.TokenTokenCompositeSearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2 )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2
      FROM @TokenTokenCompositeSearchParamsInsert

  INSERT INTO dbo.TokenDateTimeCompositeSearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2 )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2
      FROM @TokenDateTimeCompositeSearchParamsInsert
      
  INSERT INTO dbo.TokenQuantityCompositeSearchParam
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2 )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2
      FROM @TokenQuantityCompositeSearchParamsInsert

  INSERT INTO dbo.TokenNumberNumberCompositeSearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange
      FROM @TokenNumberNumberCompositeSearchParamsInsert

  COMMIT TRANSACTION

  SET @FailedResources = (SELECT count(*) FROM @WorkingResources) - @Rows

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO

