﻿IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'UriSearchParamList')
CREATE TYPE dbo.UriSearchParamList AS TABLE
(
    ResourceTypeId           smallint NOT NULL
   ,ResourceSurrogateId      bigint   NOT NULL
   ,SearchParamId            smallint NOT NULL
   ,Uri                      varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL

   --PRIMARY KEY (ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri)
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

   --TODO: Code generates dups. Remove.
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

   --UNIQUE (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue)
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

   --UNIQUE (ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue)
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

   --UNIQUE (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax)
)
GO
IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'CompartmentAssignmentList')
CREATE TYPE dbo.CompartmentAssignmentList AS TABLE
(
    ResourceTypeId           smallint NOT NULL
   ,ResourceSurrogateId      bigint   NOT NULL
   ,CompartmentTypeId        tinyint  NOT NULL
   ,ReferenceResourceId      varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL

   --PRIMARY KEY (ResourceTypeId, ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId)
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

   --UNIQUE (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId) 
)
GO
IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'ResourceIdForChangesList')
CREATE TYPE dbo.ResourceIdForChangesList AS TABLE
(
    ResourceTypeId     smallint            NOT NULL
   ,ResourceId         varchar(64)         COLLATE Latin1_General_100_CS_AS NOT NULL
   ,Version            int                 NOT NULL
   ,IsDeleted          bit                 NOT NULL

    PRIMARY KEY (ResourceTypeId, ResourceId)
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
CREATE OR ALTER PROCEDURE dbo.CaptureResourceIdsForChanges @Ids dbo.ResourceIdForChangesList READONLY
AS
set nocount on
-- This procedure is intended to be called from the MergeResources procedure and relies on its transaction logic
INSERT INTO dbo.ResourceChangeData 
       ( ResourceId, ResourceTypeId, ResourceVersion,                                              ResourceChangeTypeId )
  SELECT ResourceId, ResourceTypeId,         Version, CASE WHEN IsDeleted = 1 THEN 2 WHEN Version > 1 THEN 1 ELSE 0 END
    FROM @Ids
GO

--DROP PROCEDURE dbo.MergeResourcesBeginTransaction
GO
CREATE OR ALTER PROCEDURE dbo.MergeResourcesBeginTransaction @Count int, @TransactionId bigint = 0 OUT, @MinResourceSurrogateId bigint = 0 OUT 
AS
set nocount on
DECLARE @SP varchar(100) = 'MergeResourcesBeginTransaction'
       ,@Mode varchar(100) = 'Cnt='+convert(varchar,@Count)
       ,@st datetime2 = sysUTCdatetime()
       ,@LastValueVar sql_variant

BEGIN TRY
  -- Below logic is SQL implementation of current C# surrogate id helper extended for a batch
  -- I don't like it because it is not full proof, and can produce identical ids for different calls.
  EXECUTE sys.sp_sequence_get_range @sequence_name = 'dbo.ResourceSurrogateIdUniquifierSequence', @range_size = @Count, @range_first_value = NULL, @range_last_value = @LastValueVar OUT
  SET @MinResourceSurrogateId = datediff_big(millisecond,'0001-01-01',@st) * 80000 + convert(int,@LastValueVar) - @Count
  
  -- This is a placeholder. It will change in future.
  SET @TransactionId = @MinResourceSurrogateId

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=NULL,@Text=@TransactionId
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
--DECLARE @TransactionId bigint
--EXECUTE dbo.MergeResourcesBeginTransaction @Count = 500, @TransactionId = @TransactionId OUT
--SELECT @TransactionId

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
       ,@MaxSequence bigint
       ,@SurrBase bigint
       ,@DummyTop bigint = 9223372036854775807
       ,@InitialTranCount int = @@trancount
       ,@InputRows int = (SELECT count(*) FROM @Resources)

SET @AffectedRows = 0

DECLARE @Mode varchar(100) = 'Input='+convert(varchar,@InputRows)+' TR='+convert(varchar,@InitialTranCount)
                           +' E='+convert(varchar,@RaiseExceptionOnConflict)+' CC='+convert(varchar,@IsResourceChangeCaptureEnabled)

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
           LEFT OUTER JOIN dbo.Resource B WITH (UPDLOCK, HOLDLOCK) 
             ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId AND B.IsHistory = 0
      OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))

  IF @RaiseExceptionOnConflict = 1 AND EXISTS (SELECT * FROM @ResourceInfos WHERE Version <> isnull(PreviousVersion, 0) + 1)
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
         (          ResourceSurrogateId, ClaimTypeId, ClaimValue )
    SELECT DISTINCT ResourceSurrogateId, ClaimTypeId, ClaimValue
      FROM @ResourceWriteClaims
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.CompartmentAssignment 
         (          ResourceTypeId, ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId, IsHistory )
    SELECT DISTINCT ResourceTypeId, ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId,         0
      FROM @CompartmentAssignments
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.ReferenceSearchParam 
         (          ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion, IsHistory )
    SELECT DISTINCT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion,         0
      FROM @ReferenceSearchParams
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.TokenSearchParam 
         (          ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow, IsHistory )
    SELECT DISTINCT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow,         0
      FROM @TokenSearchParams
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.TokenText 
         (          ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, IsHistory )
    SELECT DISTINCT ResourceTypeId, ResourceSurrogateId, SearchParamId, Text,         0
      FROM @TokenTexts
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.StringSearchParam 
         (          ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsHistory, IsMin, IsMax )
    SELECT DISTINCT ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow,         0, IsMin, IsMax
      FROM @StringSearchParams
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.UriSearchParam 
         (          ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri, IsHistory )
    SELECT DISTINCT ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri,         0
      FROM @UriSearchParams
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.NumberSearchParam 
         (          ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue, IsHistory )
    SELECT DISTINCT ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue,         0
      FROM @NumberSearchParams
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.QuantitySearchParam 
         (          ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue, IsHistory )
    SELECT DISTINCT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue,         0
      FROM @QuantitySearchParams
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.DateTimeSearchParam 
         (          ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsHistory, IsMin, IsMax )
    SELECT DISTINCT ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay,         0, IsMin, IsMax
      FROM @DateTimeSearchParms
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.ReferenceTokenCompositeSearchParam 
         (          ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2, IsHistory )
    SELECT DISTINCT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2,         0
      FROM @ReferenceTokenCompositeSearchParams
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.TokenTokenCompositeSearchParam 
         (          ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2, IsHistory )
    SELECT DISTINCT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2,         0
      FROM @TokenTokenCompositeSearchParams
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.TokenDateTimeCompositeSearchParam 
         (          ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2, IsHistory )
    SELECT DISTINCT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2,         0
      FROM @TokenDateTimeCompositeSearchParams
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.TokenQuantityCompositeSearchParam 
         (          ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2, IsHistory )
    SELECT DISTINCT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2,         0
      FROM @TokenQuantityCompositeSearchParams
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.TokenStringCompositeSearchParam 
         (          ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2, IsHistory )
    SELECT DISTINCT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2,         0
      FROM @TokenStringCompositeSearchParams
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.TokenNumberNumberCompositeSearchParam 
         (          ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange, IsHistory )
    SELECT DISTINCT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange,         0
      FROM @TokenNumberNumberCompositeSearchParams
  SET @AffectedRows += @@rowcount

  IF @IsResourceChangeCaptureEnabled = 1 --If the resource change capture feature is enabled, to execute a stored procedure called CaptureResourceChanges to insert resource change data.
  BEGIN
    DECLARE @Ids dbo.ResourceIdForChangesList
    INSERT INTO @Ids
           ( ResourceTypeId, ResourceId, Version, IsDeleted )
      SELECT ResourceTypeId, ResourceId, Version, IsDeleted
        FROM @Resources
        WHERE IsHistory = 0
    EXECUTE dbo.CaptureResourceIdsForChanges @Ids
  END

  IF @InitialTranCount = 0 COMMIT TRANSACTION

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@AffectedRows
END TRY
BEGIN CATCH
  IF @InitialTranCount = 0 AND @@trancount > 0 ROLLBACK TRANSACTION
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO

