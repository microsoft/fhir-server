IF object_id('dbo.ResourceCurrent') IS NULL
BEGIN
  BEGIN TRY
    BEGIN TRANSACTION

    CREATE TABLE dbo.ResourceCurrent
    (
        ResourceTypeId              smallint                NOT NULL
       ,ResourceSurrogateId         bigint                  NOT NULL
       ,ResourceId                  varchar(64)             COLLATE Latin1_General_100_CS_AS NOT NULL
       ,Version                     int                     NOT NULL
       ,IsHistory                   bit                     NOT NULL CONSTRAINT DF_ResourceCurrent_IsHistory DEFAULT 0, CONSTRAINT CH_ResourceCurrent_IsHistory CHECK (IsHistory = 0)
       ,IsDeleted                   bit                     NOT NULL
       ,RequestMethod               varchar(10)             NULL
       ,RawResource                 varbinary(max)          NOT NULL CONSTRAINT CH_ResourceCurrent_RawResource_Length CHECK (RawResource > 0x0)
       ,IsRawResourceMetaSet        bit                     NOT NULL CONSTRAINT DF_ResourceCurrent_IsRawResourceMetaSet DEFAULT 0
       ,SearchParamHash             varchar(64)             NULL
       ,TransactionId               bigint                  NULL      -- used for main CRUD operation 

        CONSTRAINT PKC_ResourceCurrent_ResourceTypeId_ResourceSurrogateId PRIMARY KEY CLUSTERED (ResourceTypeId, ResourceSurrogateId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
       ,CONSTRAINT U_ResourceCurrent_ResourceTypeId_ResourceId UNIQUE (ResourceTypeId, ResourceId) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
    )

    ALTER TABLE dbo.ResourceCurrent SET ( LOCK_ESCALATION = AUTO )

    CREATE UNIQUE INDEX IXU_ResourceTypeId_ResourceSurrgateId_WHERE_IsDeleted_0 ON dbo.ResourceCurrent (ResourceTypeId, ResourceId) WHERE IsDeleted = 0 ON PartitionScheme_ResourceTypeId (ResourceTypeId)
    CREATE INDEX IX_ResourceTypeId_TransactionId_WHERE_TransactionId_NOT_NULL ON dbo.ResourceCurrent (ResourceTypeId, TransactionId) WHERE TransactionId IS NOT NULL ON PartitionScheme_ResourceTypeId (ResourceTypeId)

    CREATE TABLE dbo.ResourceHistory
    (
        ResourceTypeId              smallint                NOT NULL
       ,ResourceSurrogateId         bigint                  NOT NULL
       ,ResourceId                  varchar(64)             COLLATE Latin1_General_100_CS_AS NOT NULL
       ,Version                     int                     NOT NULL
       ,IsHistory                   bit                     NOT NULL CONSTRAINT DF_ResourceHistory_IsHistory DEFAULT 1, CONSTRAINT CH_ResourceHistory_IsHistory CHECK (IsHistory = 1)
       ,IsDeleted                   bit                     NOT NULL
       ,RequestMethod               varchar(10)             NULL
       ,RawResource                 varbinary(max)          NOT NULL CONSTRAINT CH_ResourceHistory_RawResource_Length CHECK (RawResource > 0x0)
       ,IsRawResourceMetaSet        bit                     NOT NULL CONSTRAINT DF_ResourceHistory_IsRawResourceMetaSet DEFAULT 0
       ,SearchParamHash             varchar(64)             NULL
       ,TransactionId               bigint                  NULL      -- used for main CRUD operation 
       ,HistoryTransactionId        bigint                  NULL      -- used by CRUD operation that moved resource version in invisible state 

        CONSTRAINT PKC_ResourceHistory_ResourceTypeId_ResourceSurrogateId PRIMARY KEY CLUSTERED (ResourceTypeId, ResourceSurrogateId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
       ,CONSTRAINT U_ResourceHistory_ResourceTypeId_ResourceId_Version UNIQUE (ResourceTypeId, ResourceId, Version) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
    )

    ALTER TABLE dbo.ResourceHistory SET ( LOCK_ESCALATION = AUTO )

    CREATE INDEX IX_ResourceTypeId_TransactionId_WHERE_TransactionId_NOT_NULL ON dbo.ResourceHistory (ResourceTypeId, TransactionId) WHERE TransactionId IS NOT NULL ON PartitionScheme_ResourceTypeId (ResourceTypeId)
    CREATE INDEX IX_ResourceTypeId_HistoryTransactionId_WHERE_HistoryTransactionId_NOT_NULL ON dbo.ResourceHistory (ResourceTypeId, HistoryTransactionId) WHERE HistoryTransactionId IS NOT NULL ON PartitionScheme_ResourceTypeId (ResourceTypeId)

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    ROLLBACK TRANSACTION
    EXECUTE dbo.LogEvent @Process='CreateCurrentAndHistory',@Status='Error';
    THROW 
  END CATCH
END
GO
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = object_id('dbo.Resource') AND type = 'u')
  DROP TABLE dbo.Resource
GO
CREATE OR ALTER VIEW dbo.Resource
AS
SELECT ResourceTypeId
      ,ResourceSurrogateId
      ,ResourceId
      ,Version
      ,IsHistory
      ,IsDeleted
      ,RequestMethod
      ,RawResource
      ,IsRawResourceMetaSet
      ,SearchParamHash
      ,TransactionId
      ,HistoryTransactionId
  FROM dbo.ResourceHistory
UNION ALL
SELECT ResourceTypeId
      ,ResourceSurrogateId
      ,ResourceId
      ,Version
      ,IsHistory
      ,IsDeleted
      ,RequestMethod
      ,RawResource
      ,IsRawResourceMetaSet
      ,SearchParamHash
      ,TransactionId
      ,NULL
  FROM dbo.ResourceCurrent
GO
CREATE OR ALTER TRIGGER dbo.ResourceIns ON dbo.Resource INSTEAD OF INSERT
AS
BEGIN
  INSERT INTO dbo.ResourceCurrent
      (
           ResourceTypeId
          ,ResourceSurrogateId
          ,ResourceId
          ,Version
          ,IsDeleted
          ,RequestMethod
          ,RawResource
          ,IsRawResourceMetaSet
          ,SearchParamHash
          ,TransactionId
      )
    SELECT ResourceTypeId
          ,ResourceSurrogateId
          ,ResourceId
          ,Version
          ,IsDeleted
          ,RequestMethod
          ,RawResource
          ,IsRawResourceMetaSet
          ,SearchParamHash
          ,TransactionId
      FROM Inserted
      WHERE IsHistory = 0

  INSERT INTO dbo.ResourceHistory
      (
           ResourceTypeId
          ,ResourceSurrogateId
          ,ResourceId
          ,Version
          ,IsDeleted
          ,RequestMethod
          ,RawResource
          ,IsRawResourceMetaSet
          ,SearchParamHash
          ,TransactionId
          ,HistoryTransactionId
      )
    SELECT ResourceTypeId
          ,ResourceSurrogateId
          ,ResourceId
          ,Version
          ,IsDeleted
          ,RequestMethod
          ,RawResource
          ,IsRawResourceMetaSet
          ,SearchParamHash
          ,TransactionId
          ,HistoryTransactionId
      FROM Inserted
      WHERE IsHistory = 1
END
GO
CREATE OR ALTER TRIGGER dbo.ResourceUpd ON dbo.Resource INSTEAD OF UPDATE
AS
BEGIN
  IF NOT UPDATE(IsHistory)
    RAISERROR('Only history updates are supported via Resource view',18,127)

  DELETE FROM A
    FROM dbo.ResourceCurrent A
    WHERE EXISTS (SELECT * FROM Inserted B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId AND B.IsHistory = 1)

  INSERT INTO dbo.ResourceHistory
      (
           ResourceTypeId
          ,ResourceSurrogateId
          ,ResourceId
          ,Version
          ,IsDeleted
          ,RequestMethod
          ,RawResource
          ,IsRawResourceMetaSet
          ,SearchParamHash
          ,TransactionId
          ,HistoryTransactionId
      )
    SELECT ResourceTypeId
          ,ResourceSurrogateId
          ,ResourceId
          ,Version
          ,IsDeleted
          ,RequestMethod
          ,RawResource
          ,IsRawResourceMetaSet
          ,SearchParamHash
          ,TransactionId
          ,HistoryTransactionId
      FROM Inserted
      WHERE IsHistory = 1
END
GO
CREATE OR ALTER TRIGGER dbo.ResourceDel ON dbo.Resource INSTEAD OF DELETE
AS
BEGIN
  DELETE FROM A
    FROM dbo.ResourceCurrent A
    WHERE EXISTS (SELECT * FROM Deleted B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId AND B.IsHistory = 0)

  DELETE FROM A
    FROM dbo.ResourceHistory A
    WHERE EXISTS (SELECT * FROM Deleted B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId AND B.IsHistory = 1)
END
GO
CREATE OR ALTER PROCEDURE dbo.UpdateResourceSearchParams
    @FailedResources int = 0 OUT
   ,@Resources dbo.ResourceList READONLY
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
    FROM @Resources A JOIN dbo.ResourceCurrent B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
    WHERE B.IsHistory = 0
  SET @Rows = @@rowcount

  -- First, delete all the search params of the resources to reindex.
  DELETE FROM B FROM @Ids A JOIN dbo.ResourceWriteClaim B ON B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A JOIN dbo.ReferenceSearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
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
                FROM (SELECT TOP (@DummyTop) * FROM @ResourceKeys) A
                     JOIN dbo.ResourceCurrent B WITH (INDEX = U_ResourceCurrent_ResourceTypeId_ResourceId) ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId AND B.Version = A.Version
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
                FROM (SELECT TOP (@DummyTop) * FROM @ResourceKeys) A
                     JOIN dbo.ResourceHistory B WITH (INDEX = U_ResourceHistory_ResourceTypeId_ResourceId_Version) ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId AND B.Version = A.Version
             ) A
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
                FROM (SELECT TOP (@DummyTop) * FROM @ResourceKeys) A
                     JOIN dbo.ResourceCurrent B WITH (INDEX = U_ResourceCurrent_ResourceTypeId_ResourceId) ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId AND B.Version = A.Version
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
                FROM (SELECT TOP (@DummyTop) * FROM @ResourceKeys) A
                     JOIN dbo.ResourceHistory B WITH (INDEX = U_ResourceHistory_ResourceTypeId_ResourceId_Version) ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId AND B.Version = A.Version
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
                     JOIN dbo.ResourceCurrent B WITH (INDEX = U_ResourceCurrent_ResourceTypeId_ResourceId) ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
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
           JOIN dbo.ResourceCurrent B WITH (INDEX = U_ResourceCurrent_ResourceTypeId_ResourceId) ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
      OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
--DROP PROCEDURE dbo.DisableIndexes
GO
CREATE OR ALTER PROCEDURE dbo.DisableIndexes
WITH EXECUTE AS 'dbo'
AS
set nocount on
DECLARE @SP varchar(100) = 'DisableIndexes'
       ,@Mode varchar(200) = ''
       ,@st datetime = getUTCdate()
       ,@Tbl varchar(100)
       ,@Ind varchar(200)
       ,@Txt varchar(4000)

BEGIN TRY
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Start'

  DECLARE @Tables TABLE (Tbl varchar(100) PRIMARY KEY, Supported bit)
  INSERT INTO @Tables EXECUTE dbo.GetPartitionedTables @IncludeNotDisabled = 1, @IncludeNotSupported = 0
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target='@Tables',@Action='Insert',@Rows=@@rowcount

  DECLARE @Indexes TABLE (Tbl varchar(100), Ind varchar(200), TblId int, IndId int PRIMARY KEY (Tbl, Ind))
  INSERT INTO @Indexes
    SELECT Tbl
          ,I.Name
          ,TblId
          ,I.index_id
      FROM (SELECT TblId = object_id(Tbl), Tbl FROM @Tables) O
           JOIN sys.indexes I ON I.object_id = TblId
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target='@Indexes',@Action='Insert',@Rows=@@rowcount

  INSERT INTO dbo.IndexProperties 
         ( TableName, IndexName,       PropertyName, PropertyValue ) 
    SELECT       Tbl,       Ind, 'DATA_COMPRESSION',     data_comp
      FROM (SELECT Tbl
                  ,Ind
                  ,data_comp = isnull((SELECT TOP 1 CASE WHEN data_compression_desc = 'PAGE' THEN 'PAGE' END FROM sys.partitions WHERE object_id = TblId AND index_id = IndId),'NONE')
              FROM @Indexes
           ) A
      WHERE NOT EXISTS (SELECT * FROM dbo.IndexProperties WHERE TableName = Tbl AND IndexName = Ind)
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target='IndexProperties',@Action='Insert',@Rows=@@rowcount

  DELETE FROM @Indexes WHERE Tbl IN ('ResourceCurrent','ResourceHistory') OR IndId = 1
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target='@Indexes',@Action='Delete',@Rows=@@rowcount

  WHILE EXISTS (SELECT * FROM @Indexes)
  BEGIN
    SELECT TOP 1 @Tbl = Tbl, @Ind = Ind FROM @Indexes

    SET @Txt = 'ALTER INDEX '+@Ind+' ON dbo.'+@Tbl+' DISABLE'
    EXECUTE(@Txt)
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@Ind,@Action='Disable',@Text=@Txt

    DELETE FROM @Indexes WHERE Tbl = @Tbl AND Ind = @Ind
  END

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
--INSERT INTO Parameters (Id,Char) SELECT name,'LogEvent' FROM sys.objects WHERE type = 'p'
--SELECT TOP 100 * FROM EventLog ORDER BY EventDate DESC

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

      IF @IsResourceChangeCaptureEnabled = 1 AND EXISTS (SELECT * FROM dbo.Parameters WHERE Id = 'InvisibleHistory.IsEnabled' AND Number = 1)
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

  IF @RaiseExceptionOnConflict = 1 AND error_number() IN (2601, 2627) AND (error_message() LIKE '%''dbo.ResourceCurrent''%' OR error_message() LIKE '%''dbo.ResourceHistory''%')
    THROW 50409, 'Resource has been recently updated or added, please compare the resource content in code for any duplicate updates', 1;
  ELSE
    THROW
END CATCH
GO