﻿IF NOT EXISTS (SELECT * FROM sys.default_constraints WHERE name = 'DF_TokenTokenCompositeSearchParam_IsHistory')
ALTER TABLE dbo.TokenTokenCompositeSearchParam ADD CONSTRAINT DF_TokenTokenCompositeSearchParam_IsHistory DEFAULT 0 FOR IsHistory
GO
IF NOT EXISTS (SELECT * FROM sys.default_constraints WHERE name = 'DF_StringSearchParam_IsHistory')
ALTER TABLE dbo.StringSearchParam ADD CONSTRAINT DF_StringSearchParam_IsHistory DEFAULT 0 FOR IsHistory
GO
IF NOT EXISTS (SELECT * FROM sys.default_constraints WHERE name = 'DF_TokenText_IsHistory')
ALTER TABLE dbo.TokenText ADD CONSTRAINT DF_TokenText_IsHistory DEFAULT 0 FOR IsHistory
GO
IF NOT EXISTS (SELECT * FROM sys.default_constraints WHERE name = 'DF_TokenStringCompositeSearchParam_IsHistory')
ALTER TABLE dbo.TokenStringCompositeSearchParam ADD CONSTRAINT DF_TokenStringCompositeSearchParam_IsHistory DEFAULT 0 FOR IsHistory
GO
IF NOT EXISTS (SELECT * FROM sys.default_constraints WHERE name = 'DF_TokenSearchParam_IsHistory')
ALTER TABLE dbo.TokenSearchParam ADD CONSTRAINT DF_TokenSearchParam_IsHistory DEFAULT 0 FOR IsHistory
GO
IF NOT EXISTS (SELECT * FROM sys.default_constraints WHERE name = 'DF_TokenQuantityCompositeSearchParam_IsHistory')
ALTER TABLE dbo.TokenQuantityCompositeSearchParam ADD CONSTRAINT DF_TokenQuantityCompositeSearchParam_IsHistory DEFAULT 0 FOR IsHistory
GO
IF NOT EXISTS (SELECT * FROM sys.default_constraints WHERE name = 'DF_TokenNumberNumberCompositeSearchParam_IsHistory')
ALTER TABLE dbo.TokenNumberNumberCompositeSearchParam ADD CONSTRAINT DF_TokenNumberNumberCompositeSearchParam_IsHistory DEFAULT 0 FOR IsHistory
GO
IF NOT EXISTS (SELECT * FROM sys.default_constraints WHERE name = 'DF_TokenDateTimeCompositeSearchParam_IsHistory')
ALTER TABLE dbo.TokenDateTimeCompositeSearchParam ADD CONSTRAINT DF_TokenDateTimeCompositeSearchParam_IsHistory DEFAULT 0 FOR IsHistory
GO
IF NOT EXISTS (SELECT * FROM sys.default_constraints WHERE name = 'DF_ReferenceTokenCompositeSearchParam_IsHistory')
ALTER TABLE dbo.ReferenceTokenCompositeSearchParam ADD CONSTRAINT DF_ReferenceTokenCompositeSearchParam_IsHistory DEFAULT 0 FOR IsHistory
GO
IF NOT EXISTS (SELECT * FROM sys.default_constraints WHERE name = 'DF_ReferenceSearchParam_IsHistory')
ALTER TABLE dbo.ReferenceSearchParam ADD CONSTRAINT DF_ReferenceSearchParam_IsHistory DEFAULT 0 FOR IsHistory
GO
IF NOT EXISTS (SELECT * FROM sys.default_constraints WHERE name = 'DF_QuantitySearchParam_IsHistory')
ALTER TABLE dbo.QuantitySearchParam ADD CONSTRAINT DF_QuantitySearchParam_IsHistory DEFAULT 0 FOR IsHistory
GO
IF NOT EXISTS (SELECT * FROM sys.default_constraints WHERE name = 'DF_NumberSearchParam_IsHistory')
ALTER TABLE dbo.NumberSearchParam ADD CONSTRAINT DF_NumberSearchParam_IsHistory DEFAULT 0 FOR IsHistory
GO
IF NOT EXISTS (SELECT * FROM sys.default_constraints WHERE name = 'DF_DateTimeSearchParam_IsHistory')
ALTER TABLE dbo.DateTimeSearchParam ADD CONSTRAINT DF_DateTimeSearchParam_IsHistory DEFAULT 0 FOR IsHistory
GO
IF NOT EXISTS (SELECT * FROM sys.default_constraints WHERE name = 'DF_CompartmentAssignment_IsHistory')
ALTER TABLE dbo.CompartmentAssignment ADD CONSTRAINT DF_CompartmentAssignment_IsHistory DEFAULT 0 FOR IsHistory
GO
IF NOT EXISTS (SELECT * FROM sys.default_constraints WHERE name = 'DF_UriSearchParam_IsHistory')
ALTER TABLE dbo.UriSearchParam ADD CONSTRAINT DF_UriSearchParam_IsHistory DEFAULT 0 FOR IsHistory
GO
IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'ResourceKeyList')
CREATE TYPE dbo.ResourceKeyList AS TABLE
(
    ResourceTypeId       smallint            NOT NULL
   ,ResourceId           varchar(64)         COLLATE Latin1_General_100_CS_AS NOT NULL
   ,Version              int                 NULL

    UNIQUE (ResourceTypeId, ResourceId, Version)
)
GO
IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'UriSearchParamList')
CREATE TYPE dbo.UriSearchParamList AS TABLE
(
    ResourceTypeId           smallint NOT NULL
   ,ResourceSurrogateId      bigint   NOT NULL
   ,SearchParamId            smallint NOT NULL
   ,Uri                      varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL

   PRIMARY KEY (ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri)
)
GO
IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'TokenTokenCompositeSearchParamList')
CREATE TYPE dbo.TokenTokenCompositeSearchParamList AS TABLE
(
    ResourceTypeId            smallint NOT NULL
   ,ResourceSurrogateId       bigint   NOT NULL
   ,SearchParamId             smallint NOT NULL
   ,SystemId1                 int      NULL
   ,Code1                     varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL
   ,CodeOverflow1             varchar(max) COLLATE Latin1_General_100_CS_AS NULL
   ,SystemId2                 int      NULL
   ,Code2                     varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL
   ,CodeOverflow2             varchar(max) COLLATE Latin1_General_100_CS_AS NULL
)
GO
IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'TokenTextList')
CREATE TYPE dbo.TokenTextList AS TABLE
(
    ResourceTypeId           smallint NOT NULL
   ,ResourceSurrogateId      bigint   NOT NULL
   ,SearchParamId            smallint NOT NULL
   ,Text                     nvarchar(400) COLLATE Latin1_General_CI_AI NOT NULL

   --PRIMARY KEY (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text)
)
GO
IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'TokenStringCompositeSearchParamList')
CREATE TYPE dbo.TokenStringCompositeSearchParamList AS TABLE
(
    ResourceTypeId            smallint NOT NULL
   ,ResourceSurrogateId       bigint   NOT NULL
   ,SearchParamId             smallint NOT NULL
   ,SystemId1                 int      NULL
   ,Code1                     varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL
   ,CodeOverflow1             varchar(max) COLLATE Latin1_General_100_CS_AS NULL
   ,Text2                     nvarchar(256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL
   ,TextOverflow2             nvarchar(max) COLLATE Latin1_General_100_CI_AI_SC NULL
)
GO
IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'TokenSearchParamList')
CREATE TYPE dbo.TokenSearchParamList AS TABLE
(
    ResourceTypeId           smallint NOT NULL
   ,ResourceSurrogateId      bigint   NOT NULL
   ,SearchParamId            smallint NOT NULL
   ,SystemId                 int      NULL
   ,Code                     varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL
   ,CodeOverflow             varchar(max) COLLATE Latin1_General_100_CS_AS NULL
)
GO
IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'TokenQuantityCompositeSearchParamList')
CREATE TYPE dbo.TokenQuantityCompositeSearchParamList AS TABLE
(
    ResourceTypeId           smallint NOT NULL
   ,ResourceSurrogateId      bigint   NOT NULL
   ,SearchParamId            smallint NOT NULL
   ,SystemId1                int      NULL
   ,Code1                    varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL
   ,CodeOverflow1            varchar(max) COLLATE Latin1_General_100_CS_AS NULL
   ,SystemId2                int      NULL
   ,QuantityCodeId2          int      NULL
   ,SingleValue2             decimal(18,6) NULL
   ,LowValue2                decimal(18,6) NULL
   ,HighValue2               decimal(18,6) NULL
)
GO
IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'TokenNumberNumberCompositeSearchParamList')
CREATE TYPE dbo.TokenNumberNumberCompositeSearchParamList AS TABLE
(
    ResourceTypeId           smallint NOT NULL
   ,ResourceSurrogateId      bigint   NOT NULL
   ,SearchParamId            smallint NOT NULL
   ,SystemId1 int NULL
   ,Code1 varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL
   ,CodeOverflow1 varchar(max) COLLATE Latin1_General_100_CS_AS NULL
   ,SingleValue2 decimal(18,6) NULL
   ,LowValue2 decimal(18,6) NULL
   ,HighValue2 decimal(18,6) NULL
   ,SingleValue3 decimal(18,6) NULL
   ,LowValue3 decimal(18,6) NULL
   ,HighValue3 decimal(18,6) NULL
   ,HasRange bit NOT NULL
)
GO
IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'TokenDateTimeCompositeSearchParamList')
CREATE TYPE dbo.TokenDateTimeCompositeSearchParamList AS TABLE
(
    ResourceTypeId            smallint NOT NULL
   ,ResourceSurrogateId       bigint   NOT NULL
   ,SearchParamId             smallint NOT NULL
   ,SystemId1                 int      NULL
   ,Code1                     varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL
   ,CodeOverflow1             varchar(max) COLLATE Latin1_General_100_CS_AS NULL
   ,StartDateTime2            datetimeoffset(7) NOT NULL
   ,EndDateTime2              datetimeoffset(7) NOT NULL
   ,IsLongerThanADay2         bit      NOT NULL
)
GO
IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'StringSearchParamList')
CREATE TYPE dbo.StringSearchParamList AS TABLE
(
    ResourceTypeId           smallint NOT NULL
   ,ResourceSurrogateId      bigint   NOT NULL
   ,SearchParamId            smallint NOT NULL
   ,Text                     nvarchar(256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL
   ,TextOverflow             nvarchar(max) COLLATE Latin1_General_100_CI_AI_SC NULL
   ,IsMin                    bit      NOT NULL
   ,IsMax                    bit      NOT NULL
)
GO
IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'ResourceWriteClaimList')
CREATE TYPE dbo.ResourceWriteClaimList AS TABLE
(
    ResourceSurrogateId      bigint        NOT NULL
   ,ClaimTypeId              tinyint       NOT NULL
   ,ClaimValue               nvarchar(128) NOT NULL

   --PRIMARY KEY (ResourceSurrogateId, ClaimTypeId, ClaimValue)
)
GO
IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'ReferenceTokenCompositeSearchParamList')
CREATE TYPE dbo.ReferenceTokenCompositeSearchParamList AS TABLE
(
    ResourceTypeId            smallint NOT NULL
   ,ResourceSurrogateId       bigint   NOT NULL
   ,SearchParamId             smallint NOT NULL
   ,BaseUri1                  varchar(128) COLLATE Latin1_General_100_CS_AS NULL
   ,ReferenceResourceTypeId1  smallint NULL
   ,ReferenceResourceId1      varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL
   ,ReferenceResourceVersion1 int      NULL
   ,SystemId2                 int      NULL
   ,Code2                     varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL
   ,CodeOverflow2             varchar(max) COLLATE Latin1_General_100_CS_AS NULL
)
GO
IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'QuantitySearchParamList')
CREATE TYPE dbo.QuantitySearchParamList AS TABLE
(
    ResourceTypeId           smallint NOT NULL
   ,ResourceSurrogateId      bigint   NOT NULL
   ,SearchParamId            smallint NOT NULL
   ,SystemId                 int      NULL
   ,QuantityCodeId           int      NULL
   ,SingleValue              decimal(18,6) NULL
   ,LowValue                 decimal(18,6) NULL
   ,HighValue                decimal(18,6) NULL

   UNIQUE (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue)
)
GO
IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'NumberSearchParamList')
CREATE TYPE dbo.NumberSearchParamList AS TABLE
(
    ResourceTypeId           smallint NOT NULL
   ,ResourceSurrogateId      bigint   NOT NULL
   ,SearchParamId            smallint NOT NULL
   ,SingleValue              decimal(18,6) NULL
   ,LowValue                 decimal(18,6) NULL
   ,HighValue                decimal(18,6) NULL

   UNIQUE (ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue)
)
GO
IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'DateTimeSearchParamList')
CREATE TYPE dbo.DateTimeSearchParamList AS TABLE
(
    ResourceTypeId           smallint NOT NULL
   ,ResourceSurrogateId      bigint   NOT NULL
   ,SearchParamId            smallint NOT NULL
   ,StartDateTime            datetimeoffset(7) NOT NULL
   ,EndDateTime              datetimeoffset(7) NOT NULL
   ,IsLongerThanADay         bit      NOT NULL
   ,IsMin                    bit      NOT NULL
   ,IsMax                    bit      NOT NULL

   UNIQUE (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax)
)
GO
IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'CompartmentAssignmentList')
CREATE TYPE dbo.CompartmentAssignmentList AS TABLE
(
    ResourceTypeId           smallint NOT NULL
   ,ResourceSurrogateId      bigint   NOT NULL
   ,CompartmentTypeId        tinyint  NOT NULL
   ,ReferenceResourceId      varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL

   PRIMARY KEY (ResourceTypeId, ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId)
)
GO
IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'ReferenceSearchParamList')
CREATE TYPE dbo.ReferenceSearchParamList AS TABLE
(
    ResourceTypeId           smallint NOT NULL
   ,ResourceSurrogateId      bigint   NOT NULL
   ,SearchParamId            smallint NOT NULL
   ,BaseUri                  varchar(128) COLLATE Latin1_General_100_CS_AS NULL
   ,ReferenceResourceTypeId  smallint NULL
   ,ReferenceResourceId      varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL
   ,ReferenceResourceVersion int      NULL

   UNIQUE (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId) 
)
GO
IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'ResourceList')
CREATE TYPE dbo.ResourceList AS TABLE
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

    PRIMARY KEY (ResourceTypeId, ResourceSurrogateId)
   ,UNIQUE (ResourceTypeId, ResourceId, Version)
)
GO
CREATE OR ALTER PROCEDURE dbo.CaptureResourceIdsForChanges @Resources dbo.ResourceList READONLY
AS
set nocount on
-- This procedure is intended to be called from the MergeResources procedure and relies on its transaction logic
INSERT INTO dbo.ResourceChangeData 
       ( ResourceId, ResourceTypeId, ResourceVersion,                                              ResourceChangeTypeId )
  SELECT ResourceId, ResourceTypeId,         Version, CASE WHEN IsDeleted = 1 THEN 2 WHEN Version > 1 THEN 1 ELSE 0 END
    FROM @Resources
    WHERE IsHistory = 0
GO

IF object_id('dbo.Transactions') IS NULL
CREATE TABLE dbo.Transactions
(
     SurrogateIdRangeFirstValue   bigint NOT NULL
    ,SurrogateIdRangeLastValue    bigint NOT NULL
    ,Definition                   varchar(2000) NULL
    ,IsCompleted                  bit NOT NULL CONSTRAINT DF_Transactions_IsCompleted DEFAULT 0 -- is set at the end of commit process
    ,IsSuccess                    bit NOT NULL CONSTRAINT DF_Transactions_IsSuccess DEFAULT 0 -- is set at the end of commit process on success
    ,IsVisible                    bit NOT NULL CONSTRAINT DF_Transactions_IsVisible DEFAULT 0
    ,IsHistoryMoved               bit NOT NULL CONSTRAINT DF_Transactions_IsHistoryMoved DEFAULT 0
    ,CreateDate                   datetime NOT NULL CONSTRAINT DF_Transactions_CreateDate DEFAULT getUTCdate()
    ,EndDate                      datetime NULL -- is populated only for commit change sets at the end of commit process on success or failure
    ,VisibleDate                  datetime NULL -- indicates when transaction data became visible
    ,HistoryMovedDate             datetime NULL
    ,HeartbeatDate                datetime NOT NULL CONSTRAINT DF_Transactions_HeartbeatDate DEFAULT getUTCdate()
    ,FailureReason                varchar(max) NULL -- is populated at the end of data load on failure
    ,IsControlledByClient         bit NOT NULL CONSTRAINT DF_Transactions_IsControlledByClient DEFAULT 1

     CONSTRAINT PKC_Transactions_SurrogateIdRangeFirstValue PRIMARY KEY CLUSTERED (SurrogateIdRangeFirstValue)
)
GO
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('dbo.Transactions') AND name = 'IX_IsVisible')
CREATE INDEX IX_IsVisible ON dbo.Transactions (IsVisible)
GO

--DROP PROCEDURE dbo.MergeResourcesCommitTransaction
GO
CREATE OR ALTER PROCEDURE dbo.MergeResourcesCommitTransaction @SurrogateIdRangeFirstValue bigint, @FailureReason varchar(max) = NULL
AS
set nocount on
DECLARE @SP varchar(100) = 'MergeResourcesCommitTransaction'
       ,@Mode varchar(200) = 'TR='+convert(varchar,@SurrogateIdRangeFirstValue)
       ,@st datetime = getUTCdate()

BEGIN TRY
  UPDATE dbo.Transactions 
    SET IsCompleted = 1
       ,IsSuccess = CASE WHEN @FailureReason IS NULL THEN 1 ELSE 0 END
       ,EndDate = getUTCdate()
       ,IsVisible = 1 -- this will change in future
       ,VisibleDate = getUTCdate()
       ,FailureReason = @FailureReason
    WHERE SurrogateIdRangeFirstValue = @SurrogateIdRangeFirstValue
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO

INSERT INTO dbo.Parameters (Id, Number) SELECT 'MergeResources.SurrogateIdRangeOverlapCheck.IsEnabled', 0

--DROP PROCEDURE dbo.MergeResourcesBeginTransaction
GO
CREATE OR ALTER PROCEDURE dbo.MergeResourcesBeginTransaction @Count int, @SurrogateIdRangeFirstValue bigint = 0 OUT
AS
set nocount on
DECLARE @SP varchar(100) = 'MergeResourcesBeginTransaction'
       ,@Mode varchar(200) = 'Cnt='+convert(varchar,@Count)
       ,@st datetime = getUTCdate()
       ,@FirstValueVar sql_variant
       ,@TransactionId bigint = NULL
       ,@RunTransactionCheck bit = (SELECT Number FROM dbo.Parameters WHERE Id = 'MergeResources.SurrogateIdRangeOverlapCheck.IsEnabled')

BEGIN TRY
  IF @@trancount > 0 RAISERROR('MergeResourcesBeginTransaction cannot be called inside outer transaction.', 18, 127)

  SET TRANSACTION ISOLATION LEVEL REPEATABLE READ
    
  WHILE @TransactionId IS NULL
  BEGIN
    EXECUTE sys.sp_sequence_get_range @sequence_name = 'dbo.ResourceSurrogateIdUniquifierSequence', @range_size = @Count, @range_first_value = @FirstValueVar OUT, @range_last_value = NULL

    SET @SurrogateIdRangeFirstValue = datediff_big(millisecond,'0001-01-01',sysUTCdatetime()) * 80000 + convert(int,@FirstValueVar)

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
--EXECUTE dbo.MergeResourcesBeginTransaction @Count = 100, @TransactionId = 0, @SurrogateIdRangeFirstValue = @SurrogateIdRangeFirstValue OUT
--SELECT @SurrogateIdRangeFirstValue
--SELECT TOP 10 * FROM Transactions ORDER BY SurrogateIdRangeFirstValue DESC
--SELECT TOP 100 * FROM EventLog WHERE EventDate > dateadd(minute,-60,getUTCdate()) AND Process = 'MergeResourcesBeginTransaction' ORDER BY EventDate DESC, EventId DESC

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
   ,@Resources dbo.ResourceList READONLY
   ,@ResourceWriteClaims dbo.ResourceWriteClaimList READONLY
   ,@CompartmentAssignments dbo.CompartmentAssignmentList READONLY
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

DECLARE @Mode varchar(200) = isnull((SELECT 'RT=['+convert(varchar,min(ResourceTypeId))+','+convert(varchar,max(ResourceTypeId))+'] MinSur='+convert(varchar,min(ResourceSurrogateId))+' Rows='+convert(varchar,count(*)) FROM @Resources),'Input=Empty')
SET @Mode += ' ITC='+convert(varchar,@InitialTranCount)+' E='+convert(varchar,@RaiseExceptionOnConflict)+' CC='+convert(varchar,@IsResourceChangeCaptureEnabled)

SET @AffectedRows = 0

BEGIN TRY
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

  IF @InitialTranCount = 0 BEGIN TRANSACTION
  
  INSERT INTO @ResourceInfos
      (
           ResourceTypeId
          ,SurrogateId
          ,Version
          ,KeepHistory
          ,PreviousVersion
          ,PreviousSurrogateId
      )
    SELECT A.ResourceTypeId
          ,A.ResourceSurrogateId
          ,A.Version
          ,A.KeepHistory
          ,B.Version
          ,B.ResourceSurrogateId
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
    DELETE FROM dbo.CompartmentAssignment WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
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

  INSERT INTO dbo.CompartmentAssignment 
         ( ResourceTypeId, ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId )
    SELECT ResourceTypeId, ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId
      FROM @CompartmentAssignments
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

  IF @IsResourceChangeCaptureEnabled = 1 --If the resource change capture feature is enabled, to execute a stored procedure called CaptureResourceChanges to insert resource change data.
    EXECUTE dbo.CaptureResourceIdsForChanges @Resources

  IF @InitialTranCount = 0 COMMIT TRANSACTION

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@AffectedRows
END TRY
BEGIN CATCH
  IF @InitialTranCount = 0 AND @@trancount > 0 ROLLBACK TRANSACTION
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;

  IF @RaiseExceptionOnConflict = 1 AND error_number() = 2601 AND error_message() LIKE '%''dbo.Resource''%version%'
    THROW 50409, 'Resource has been recently updated or added, please compare the resource content in code for any duplicate updates', 1;
  ELSE
    THROW
END CATCH
GO

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

SELECT @InputRows = count(*), @NotNullVersionExists = max(CASE WHEN Version IS NOT NULL THEN 1 ELSE 0 END), @NullVersionExists = max(CASE WHEN Version IS NULL THEN 1 ELSE 0 END) FROM @ResourceKeys

DECLARE @Mode varchar(100) = 'Cnt='+convert(varchar,@InputRows)+' NNVE='+convert(varchar,@NotNullVersionExists)+' NVE='+convert(varchar,@NullVersionExists)

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
