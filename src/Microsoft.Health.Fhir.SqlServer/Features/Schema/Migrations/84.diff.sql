IF NOT EXISTS (SELECT * FROM sys.sequences WHERE name = 'ResourceIdIntMapSequence')
CREATE SEQUENCE dbo.ResourceIdIntMapSequence
        AS int
        START WITH 0
        INCREMENT BY 1
        MINVALUE 0
        MAXVALUE 79999
        CYCLE
        CACHE 1000000
GO
CREATE OR ALTER PROCEDURE dbo.AssignResourceIdInts @Count int, @FirstIdInt bigint OUT
AS
set nocount on
DECLARE @SP varchar(100) = 'AssignResourceIdInts'
       ,@Mode varchar(200) = 'Cnt='+convert(varchar,@Count)
       ,@st datetime = getUTCdate()
       ,@FirstValueVar sql_variant
       ,@LastValueVar sql_variant
       ,@SequenceRangeFirstValue int

BEGIN TRY
  SET @FirstValueVar = NULL
  WHILE @FirstValueVar IS NULL
  BEGIN
    EXECUTE sys.sp_sequence_get_range @sequence_name = 'dbo.ResourceIdIntMapSequence', @range_size = @Count, @range_first_value = @FirstValueVar OUT, @range_last_value = @LastValueVar OUT
    SET @SequenceRangeFirstValue = convert(int,@FirstValueVar)
    IF @SequenceRangeFirstValue > convert(int,@LastValueVar)
      SET @FirstValueVar = NULL
  END

  SET @FirstIdInt = datediff_big(millisecond,'0001-01-01',sysUTCdatetime()) * 80000 + @SequenceRangeFirstValue
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  IF @@trancount > 0 ROLLBACK TRANSACTION
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'ResourceListLake')
CREATE TYPE dbo.ResourceListLake AS TABLE
(
    ResourceTypeId       smallint            NOT NULL
   ,ResourceSurrogateId  bigint              NOT NULL
   ,ResourceId           varchar(64)         COLLATE Latin1_General_100_CS_AS NOT NULL
   ,Version              int                 NOT NULL
   ,HasVersionToCompare  bit                 NOT NULL -- in case of multiple versions per resource indicates that row contains (existing version + 1) value
   ,IsDeleted            bit                 NOT NULL
   ,IsHistory            bit                 NOT NULL
   ,KeepHistory          bit                 NOT NULL
   ,RawResource          varbinary(max)      NULL
   ,IsRawResourceMetaSet bit                 NOT NULL
   ,RequestMethod        varchar(10)         NULL
   ,SearchParamHash      varchar(64)         NULL
   ,OffsetInFile         int                 NULL

    PRIMARY KEY (ResourceTypeId, ResourceSurrogateId)
   ,UNIQUE (ResourceTypeId, ResourceId, Version)
)
GO
IF EXISTS (SELECT * FROM sys.objects WHERE name = 'Resource' AND type = 'u') 
BEGIN
  BEGIN TRANSACTION

  EXECUTE sp_rename 'Resource', 'ResourceTbl'

  CREATE TABLE dbo.ResourceIdIntMap
  (
      ResourceTypeId  smallint    NOT NULL
     ,ResourceIdInt   bigint      NOT NULL
     ,ResourceId      varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL
    
      CONSTRAINT PKC_ResourceIdIntMap_ResourceIdInt_ResourceTypeId PRIMARY KEY CLUSTERED (ResourceIdInt, ResourceTypeId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
     ,CONSTRAINT U_ResourceIdIntMap_ResourceId_ResourceTypeId UNIQUE (ResourceId, ResourceTypeId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
  )

  ALTER TABLE dbo.ResourceIdIntMap SET ( LOCK_ESCALATION = AUTO )

  CREATE TABLE dbo.RawResources
  (
      ResourceTypeId              smallint                NOT NULL
     ,ResourceSurrogateId         bigint                  NOT NULL
     ,RawResource                 varbinary(max)          NULL

      CONSTRAINT PKC_RawResources_ResourceTypeId_ResourceSurrogateId PRIMARY KEY CLUSTERED (ResourceTypeId, ResourceSurrogateId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
  )

  ALTER TABLE dbo.RawResources SET ( LOCK_ESCALATION = AUTO )

  CREATE TABLE dbo.CurrentResources
  (
      ResourceTypeId              smallint                NOT NULL
     ,ResourceSurrogateId         bigint                  NOT NULL
     ,ResourceIdInt               bigint                  NOT NULL
     ,Version                     int                     NOT NULL
     ,IsHistory                   bit                     NOT NULL CONSTRAINT DF_CurrentResources_IsHistory DEFAULT 0, CONSTRAINT CH_ResourceCurrent_IsHistory CHECK (IsHistory = 0)
     ,IsDeleted                   bit                     NOT NULL
     ,RequestMethod               varchar(10)             NULL
     ,IsRawResourceMetaSet        bit                     NOT NULL CONSTRAINT DF_CurrentResources_IsRawResourceMetaSet DEFAULT 0
     ,SearchParamHash             varchar(64)             NULL
     ,TransactionId               bigint                  NULL
     ,HistoryTransactionId        bigint                  NULL
     ,FileId                      bigint                  NULL
     ,OffsetInFile                int                     NULL

      CONSTRAINT PKC_CurrentResources_ResourceSurrogateId_ResourceTypeId PRIMARY KEY CLUSTERED (ResourceSurrogateId, ResourceTypeId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
     ,CONSTRAINT U_CurrentResources_ResourceIdInt_ResourceTypeId UNIQUE (ResourceIdInt, ResourceTypeId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
  )

  ALTER TABLE dbo.CurrentResources ADD CONSTRAINT FK_CurrentResources_ResourceIdInt_ResourceTypeId_ResourceIdIntMap FOREIGN KEY (ResourceIdInt, ResourceTypeId) REFERENCES dbo.ResourceIdIntMap (ResourceIdInt, ResourceTypeId)

  ALTER TABLE dbo.CurrentResources SET ( LOCK_ESCALATION = AUTO )

  CREATE INDEX IX_TransactionId_ResourceTypeId_WHERE_TransactionId_NOT_NULL ON dbo.CurrentResources (TransactionId, ResourceTypeId) WHERE TransactionId IS NOT NULL WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
  CREATE INDEX IX_HistoryTransactionId_ResourceTypeId_WHERE_HistoryTransactionId_NOT_NULL ON dbo.CurrentResources (HistoryTransactionId, ResourceTypeId) WHERE HistoryTransactionId IS NOT NULL WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)

  CREATE TABLE dbo.HistoryResources
  (
      ResourceTypeId              smallint                NOT NULL
     ,ResourceSurrogateId         bigint                  NOT NULL
     ,ResourceIdInt               bigint                  NOT NULL
     ,Version                     int                     NOT NULL
     ,IsHistory                   bit                     NOT NULL CONSTRAINT DF_HistoryResources_IsHistory DEFAULT 1, CONSTRAINT CH_HistoryResources_IsHistory CHECK (IsHistory = 1)
     ,IsDeleted                   bit                     NOT NULL
     ,RequestMethod               varchar(10)             NULL
     ,IsRawResourceMetaSet        bit                     NOT NULL CONSTRAINT DF_HistoryResources_IsRawResourceMetaSet DEFAULT 0
     ,SearchParamHash             varchar(64)             NULL
     ,TransactionId               bigint                  NULL
     ,HistoryTransactionId        bigint                  NULL
     ,FileId                      bigint                  NULL
     ,OffsetInFile                int                     NULL

      CONSTRAINT PKC_HistoryResources_ResourceSurrogateId_ResourceTypeId PRIMARY KEY CLUSTERED (ResourceSurrogateId, ResourceTypeId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
     ,CONSTRAINT U_HistoryResources_ResourceIdInt_Version_ResourceTypeId UNIQUE (ResourceIdInt, Version, ResourceTypeId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
  )

  ALTER TABLE dbo.HistoryResources ADD CONSTRAINT FK_HistoryResources_ResourceIdInt_ResourceTypeId_ResourceIdIntMap FOREIGN KEY (ResourceIdInt, ResourceTypeId) REFERENCES dbo.ResourceIdIntMap (ResourceIdInt, ResourceTypeId)

  ALTER TABLE dbo.HistoryResources SET ( LOCK_ESCALATION = AUTO )

  CREATE INDEX IX_TransactionId_ResourceTypeId_WHERE_TransactionId_NOT_NULL ON dbo.HistoryResources (TransactionId, ResourceTypeId) WHERE TransactionId IS NOT NULL WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
  CREATE INDEX IX_HistoryTransactionId_ResourceTypeId_WHERE_HistoryTransactionId_NOT_NULL ON dbo.HistoryResources (HistoryTransactionId, ResourceTypeId) WHERE HistoryTransactionId IS NOT NULL WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
  
  EXECUTE('
CREATE VIEW dbo.Resource
AS
SELECT A.ResourceTypeId
      ,A.ResourceSurrogateId
      ,ResourceId
      ,A.ResourceIdInt
      ,Version
      ,IsHistory
      ,IsDeleted
      ,RequestMethod
      ,B.RawResource
      ,IsRawResourceMetaSet
      ,SearchParamHash
      ,TransactionId 
      ,HistoryTransactionId
      ,FileId
      ,OffsetInFile
  FROM dbo.CurrentResources A
       LEFT OUTER JOIN dbo.RawResources B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
       LEFT OUTER JOIN dbo.ResourceIdIntMap C ON C.ResourceTypeId = A.ResourceTypeId AND C.ResourceIdInt = A.ResourceIdInt
UNION ALL
SELECT A.ResourceTypeId
      ,A.ResourceSurrogateId
      ,ResourceId
      ,A.ResourceIdInt
      ,Version
      ,IsHistory
      ,IsDeleted
      ,RequestMethod
      ,B.RawResource
      ,IsRawResourceMetaSet
      ,SearchParamHash
      ,TransactionId 
      ,HistoryTransactionId
      ,FileId
      ,OffsetInFile
  FROM dbo.HistoryResources A
       LEFT OUTER JOIN dbo.RawResources B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
       LEFT OUTER JOIN dbo.ResourceIdIntMap C ON C.ResourceTypeId = A.ResourceTypeId AND C.ResourceIdInt = A.ResourceIdInt
UNION ALL
SELECT ResourceTypeId
      ,ResourceSurrogateId
      ,ResourceId
      ,NULL
      ,Version
      ,IsHistory
      ,IsDeleted
      ,RequestMethod
      ,RawResource
      ,IsRawResourceMetaSet
      ,SearchParamHash
      ,TransactionId 
      ,HistoryTransactionId
      ,NULL
      ,NULL
  FROM dbo.ResourceTbl
  ')

  COMMIT TRANSACTION
END
GO
CREATE OR ALTER TRIGGER dbo.ResourceIns ON dbo.Resource INSTEAD OF INSERT
AS
BEGIN
  INSERT INTO dbo.RawResources
         ( ResourceTypeId, ResourceSurrogateId, RawResource )
    SELECT ResourceTypeId, ResourceSurrogateId, RawResource
      FROM Inserted
      WHERE RawResource IS NOT NULL

  INSERT INTO dbo.CurrentResources
         ( ResourceTypeId, ResourceSurrogateId, ResourceIdInt, Version, IsDeleted, RequestMethod, IsRawResourceMetaSet, SearchParamHash, TransactionId, HistoryTransactionId, FileId, OffsetInFile )
    SELECT ResourceTypeId, ResourceSurrogateId, ResourceIdInt, Version, IsDeleted, RequestMethod, IsRawResourceMetaSet, SearchParamHash, TransactionId, HistoryTransactionId, FileId, OffsetInFile
      FROM Inserted
      WHERE IsHistory = 0

  INSERT INTO dbo.HistoryResources
         ( ResourceTypeId, ResourceSurrogateId, ResourceIdInt, Version, IsDeleted, RequestMethod, IsRawResourceMetaSet, SearchParamHash, TransactionId, HistoryTransactionId, FileId, OffsetInFile )
    SELECT ResourceTypeId, ResourceSurrogateId, ResourceIdInt, Version, IsDeleted, RequestMethod, IsRawResourceMetaSet, SearchParamHash, TransactionId, HistoryTransactionId, FileId, OffsetInFile
      FROM Inserted
      WHERE IsHistory = 1
END
GO
CREATE OR ALTER TRIGGER dbo.ResourceUpd ON dbo.Resource INSTEAD OF UPDATE
AS
BEGIN
  IF UPDATE(IsDeleted) AND UPDATE(RawResource) AND UPDATE(SearchParamHash) AND UPDATE(HistoryTransactionId) AND NOT UPDATE(IsHistory) -- hard delete resource
  BEGIN
    UPDATE B
      SET RawResource = A.RawResource
      FROM Inserted A
           JOIN dbo.RawResources B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
    
    IF @@rowcount = 0
      INSERT INTO dbo.RawResources
             ( ResourceTypeId, ResourceSurrogateId, RawResource )
        SELECT ResourceTypeId, ResourceSurrogateId, RawResource
          FROM Inserted
          WHERE RawResource IS NOT NULL

    UPDATE B
      SET IsDeleted = A.IsDeleted
         ,SearchParamHash = A.SearchParamHash
         ,HistoryTransactionId = A.HistoryTransactionId
      FROM Inserted A
           JOIN dbo.CurrentResources B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  
    RETURN
  END

  IF UPDATE(SearchParamHash) AND NOT UPDATE(IsHistory) -- reindex
  BEGIN
    UPDATE B
      SET SearchParamHash = A.SearchParamHash
      FROM Inserted A
           JOIN dbo.CurrentResources B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
      WHERE A.IsHistory = 0
    
    RETURN
  END

  IF UPDATE(TransactionId) AND NOT UPDATE(IsHistory) -- cleanup trans
  BEGIN
    UPDATE B
      SET TransactionId = A.TransactionId
      FROM Inserted A
           JOIN dbo.CurrentResources B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId AND B.IsHistory = 0

    UPDATE B
      SET TransactionId = A.TransactionId
      FROM Inserted A
           JOIN dbo.HistoryResources B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId AND B.IsHistory = 1
    
    RETURN
  END

  IF UPDATE(RawResource) -- invisible records
  BEGIN
    UPDATE B
      SET RawResource = A.RawResource
      FROM Inserted A
           JOIN dbo.RawResources B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId

    IF @@rowcount = 0
      INSERT INTO dbo.RawResources
             ( ResourceTypeId, ResourceSurrogateId, RawResource )
        SELECT ResourceTypeId, ResourceSurrogateId, RawResource
          FROM Inserted
          WHERE RawResource IS NOT NULL
  END

  IF NOT UPDATE(IsHistory)
    RAISERROR('Generic updates are not supported via Resource view',18,127)

  DELETE FROM A
    FROM dbo.CurrentResources A
    WHERE EXISTS (SELECT * FROM Inserted B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId AND B.IsHistory = 1)

  INSERT INTO dbo.HistoryResources
         ( ResourceTypeId, ResourceSurrogateId, ResourceIdInt, Version, IsDeleted, RequestMethod, IsRawResourceMetaSet, SearchParamHash, TransactionId, HistoryTransactionId, FileId, OffsetInFile )
    SELECT ResourceTypeId, ResourceSurrogateId, ResourceIdInt, Version, IsDeleted, RequestMethod, IsRawResourceMetaSet, SearchParamHash, TransactionId, HistoryTransactionId, FileId, OffsetInFile
      FROM Inserted
      WHERE IsHistory = 1
END
GO
CREATE OR ALTER TRIGGER dbo.ResourceDel ON dbo.Resource INSTEAD OF DELETE
AS
BEGIN
  DELETE FROM A
    FROM dbo.CurrentResources A
    WHERE EXISTS (SELECT * FROM Deleted B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId AND B.IsHistory = 0)

  DELETE FROM A
    FROM dbo.HistoryResources A
    WHERE EXISTS (SELECT * FROM Deleted B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId AND B.IsHistory = 1)

  DELETE FROM A
    FROM dbo.RawResources A
    WHERE EXISTS (SELECT * FROM Deleted B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId)
END
GO
IF EXISTS (SELECT * FROM sys.objects WHERE name = 'ReferenceSearchParam' AND type = 'u') 
BEGIN
  BEGIN TRANSACTION
  
  EXECUTE sp_rename 'ReferenceSearchParam', 'ReferenceSearchParamTbl'

  CREATE TABLE dbo.ResourceReferenceSearchParams
  (
      ResourceTypeId            smallint     NOT NULL
     ,ResourceSurrogateId       bigint       NOT NULL
     ,SearchParamId             smallint     NOT NULL
     ,BaseUri                   varchar(128) COLLATE Latin1_General_100_CS_AS NULL
     ,ReferenceResourceTypeId   smallint     NOT NULL
     ,ReferenceResourceIdInt    bigint       NOT NULL
     ,IsResourceRef             bit          NOT NULL CONSTRAINT DF_ResourceReferenceSearchParams_IsResourceRef DEFAULT 1, CONSTRAINT CH_ResourceReferenceSearchParams_IsResourceRef CHECK (IsResourceRef = 1)
  )

  ALTER TABLE dbo.ResourceReferenceSearchParams ADD CONSTRAINT FK_ResourceReferenceSearchParams_ReferenceResourceIdInt_ReferenceResourceTypeId_ResourceIdIntMap FOREIGN KEY (ReferenceResourceIdInt, ReferenceResourceTypeId) REFERENCES dbo.ResourceIdIntMap (ResourceIdInt, ResourceTypeId)

  ALTER TABLE dbo.ResourceReferenceSearchParams SET ( LOCK_ESCALATION = AUTO )

  CREATE CLUSTERED INDEX IXC_ResourceSurrogateId_SearchParamId_ResourceTypeId 
    ON dbo.ResourceReferenceSearchParams (ResourceSurrogateId, SearchParamId, ResourceTypeId)
    WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)

  CREATE UNIQUE INDEX IXU_ReferenceResourceIdInt_ReferenceResourceTypeId_SearchParamId_BaseUri_ResourceSurrogateId_ResourceTypeId 
    ON dbo.ResourceReferenceSearchParams (ReferenceResourceIdInt, ReferenceResourceTypeId, SearchParamId, BaseUri, ResourceSurrogateId, ResourceTypeId)
    WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)

  CREATE TABLE dbo.StringReferenceSearchParams
  (
      ResourceTypeId            smallint     NOT NULL
     ,ResourceSurrogateId       bigint       NOT NULL
     ,SearchParamId             smallint     NOT NULL
     ,BaseUri                   varchar(128) COLLATE Latin1_General_100_CS_AS NULL
     ,ReferenceResourceId       varchar(768) COLLATE Latin1_General_100_CS_AS NOT NULL
     ,IsResourceRef             bit          NOT NULL CONSTRAINT DF_StringReferenceSearchParams_IsResourceRef DEFAULT 0, CONSTRAINT CH_StringReferenceSearchParams_IsResourceRef CHECK (IsResourceRef = 0)
  )

  ALTER TABLE dbo.StringReferenceSearchParams SET ( LOCK_ESCALATION = AUTO )

  CREATE CLUSTERED INDEX IXC_ResourceSurrogateId_SearchParamId_ResourceTypeId 
    ON dbo.StringReferenceSearchParams (ResourceSurrogateId, SearchParamId, ResourceTypeId)
    WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)

  CREATE UNIQUE INDEX IXU_ReferenceResourceId_SearchParamId_BaseUri_ResourceSurrogateId_ResourceTypeId 
    ON dbo.StringReferenceSearchParams (ReferenceResourceId, SearchParamId, BaseUri, ResourceSurrogateId, ResourceTypeId)
    WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)

  EXECUTE('
CREATE VIEW dbo.ReferenceSearchParam
AS
SELECT A.ResourceTypeId
      ,ResourceSurrogateId
      ,SearchParamId
      ,BaseUri
      ,ReferenceResourceTypeId
      ,ReferenceResourceId = B.ResourceId
      ,IsResourceRef
  FROM dbo.ResourceReferenceSearchParams A
       LEFT OUTER JOIN dbo.ResourceIdIntMap B ON B.ResourceTypeId = A.ReferenceResourceTypeId AND B.ResourceIdInt = A.ReferenceResourceIdInt
UNION ALL
SELECT ResourceTypeId
      ,ResourceSurrogateId
      ,SearchParamId
      ,BaseUri
      ,NULL
      ,ReferenceResourceId
      ,IsResourceRef
  FROM dbo.StringReferenceSearchParams
UNION ALL
SELECT ResourceTypeId
      ,ResourceSurrogateId
      ,SearchParamId
      ,BaseUri
      ,ReferenceResourceTypeId
      ,ReferenceResourceId
      ,NULL
  FROM dbo.ReferenceSearchParamTbl
    ')
  
  EXECUTE('
CREATE TRIGGER dbo.ReferenceSearchParamDel ON dbo.ReferenceSearchParam INSTEAD OF DELETE
AS
BEGIN
  DELETE FROM A
    FROM dbo.ResourceReferenceSearchParams A
    WHERE EXISTS (SELECT * FROM Deleted B WHERE B.IsResourceRef = 1 AND B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId)

  DELETE FROM A
    FROM dbo.StringReferenceSearchParams A
    WHERE EXISTS (SELECT * FROM Deleted B WHERE B.IsResourceRef = 0 AND B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId)
END
  ')

  COMMIT TRANSACTION
END
GO
GO
BEGIN TRANSACTION
GO
DROP PROCEDURE MergeResources
DROP PROCEDURE UpdateResourceSearchParams
DROP TYPE ReferenceSearchParamList
GO
CREATE TYPE dbo.ReferenceSearchParamList AS TABLE
(
    ResourceTypeId           smallint NOT NULL
   ,ResourceSurrogateId      bigint   NOT NULL
   ,SearchParamId            smallint NOT NULL
   ,BaseUri                  varchar(128) COLLATE Latin1_General_100_CS_AS NULL
   ,ReferenceResourceTypeId  smallint NULL
   ,ReferenceResourceId      varchar(768) COLLATE Latin1_General_100_CS_AS NOT NULL
   ,ReferenceResourceVersion int      NULL

   UNIQUE (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId) 
)
GO
CREATE PROCEDURE dbo.UpdateResourceSearchParams
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
    SET SearchParamHash = (SELECT SearchParamHash FROM @Resources A WHERE A.ResourceTypeId = B.ResourceTypeId AND A.ResourceSurrogateId = B.ResourceSurrogateId)
    OUTPUT deleted.ResourceTypeId, deleted.ResourceSurrogateId INTO @Ids 
    FROM dbo.Resource B 
    WHERE EXISTS (SELECT * FROM @Resources A WHERE A.ResourceTypeId = B.ResourceTypeId AND A.ResourceSurrogateId = B.ResourceSurrogateId)
      AND B.IsHistory = 0
  SET @Rows = @@rowcount

  -- First, delete all the search params of the resources to reindex.
  DELETE FROM B FROM @Ids A JOIN dbo.ResourceWriteClaim B ON B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM dbo.ResourceReferenceSearchParams B WHERE EXISTS (SELECT * FROM @Ids A WHERE A.ResourceTypeId = B.ResourceTypeId AND A.ResourceSurrogateId = B.ResourceSurrogateId)
  DELETE FROM B FROM dbo.StringReferenceSearchParams B WHERE EXISTS (SELECT * FROM @Ids A WHERE A.ResourceTypeId = B.ResourceTypeId AND A.ResourceSurrogateId = B.ResourceSurrogateId)
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

  -- TODO: Add insert into ResourceIdIntMap

  INSERT INTO dbo.ResourceReferenceSearchParams 
         (   ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceIdInt )
    SELECT A.ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId,        B.ResourceIdInt
      FROM @ReferenceSearchParams A
           JOIN dbo.ResourceIdIntMap B ON B.ResourceTypeId = A.ReferenceResourceTypeId AND B.ResourceId = A.ReferenceResourceId

  INSERT INTO dbo.StringReferenceSearchParams 
         (  ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceId )
    SELECT  ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceId
      FROM @ReferenceSearchParams
      WHERE ReferenceResourceTypeId IS NULL

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
CREATE PROCEDURE dbo.MergeResources
-- This stored procedure can be used for:
-- 1. Ordinary put with single version per resource in input
-- 2. Put with history preservation (multiple input versions per resource)
-- 3. Copy from one gen2 store to another with ResourceSurrogateId preserved.
    @AffectedRows int = 0 OUT
   ,@RaiseExceptionOnConflict bit = 1
   ,@IsResourceChangeCaptureEnabled bit = 0
   ,@TransactionId bigint = NULL
   ,@SingleTransaction bit = 1
   ,@Resources dbo.ResourceList READONLY -- before lake code. TODO: Remove after deployment
   ,@ResourcesLake dbo.ResourceListLake READONLY -- Lake code
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
       ,@RT smallint
       ,@NewIdsCount int
       ,@FirstIdInt bigint

DECLARE @Mode varchar(200) = isnull((SELECT 'RT=['+convert(varchar,min(ResourceTypeId))+','+convert(varchar,max(ResourceTypeId))+'] Sur=['+convert(varchar,min(ResourceSurrogateId))+','+convert(varchar,max(ResourceSurrogateId))+'] V='+convert(varchar,max(Version))+' Rows='+convert(varchar,count(*)) FROM @Resources),'Input=Empty')
SET @Mode += ' E='+convert(varchar,@RaiseExceptionOnConflict)+' CC='+convert(varchar,@IsResourceChangeCaptureEnabled)+' IT='+convert(varchar,@InitialTranCount)+' T='+isnull(convert(varchar,@TransactionId),'NULL')

SET @AffectedRows = 0

RetryResourceIdIntMapInsert:
BEGIN TRY
  DECLARE @RTs AS TABLE (ResourceTypeId smallint NOT NULL PRIMARY KEY)
  DECLARE @InputIds AS TABLE (ResourceTypeId smallint NOT NULL, ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY (ResourceTypeId, ResourceId))
  DECLARE @ExistingIds AS TABLE (ResourceTypeId smallint NOT NULL, ResourceIdInt bigint NOT NULL, ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY (ResourceTypeId, ResourceId))
  DECLARE @InsertIds AS TABLE (ResourceIndex int NOT NULL, ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL)
  DECLARE @InsertedIds AS TABLE (ResourceTypeId smallint NOT NULL, ResourceIdInt bigint NOT NULL, ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY (ResourceTypeId, ResourceId))
  DECLARE @ResourcesWithIds AS TABLE 
    (
        ResourceTypeId       smallint            NOT NULL
       ,ResourceSurrogateId  bigint              NOT NULL
       ,ResourceIdInt        bigint              NOT NULL
       ,Version              int                 NOT NULL
       ,HasVersionToCompare  bit                 NOT NULL -- in case of multiple versions per resource indicates that row contains (existing version + 1) value
       ,IsDeleted            bit                 NOT NULL
       ,IsHistory            bit                 NOT NULL
       ,KeepHistory          bit                 NOT NULL
       ,RawResource          varbinary(max)      NULL
       ,IsRawResourceMetaSet bit                 NOT NULL
       ,RequestMethod        varchar(10)         NULL
       ,SearchParamHash      varchar(64)         NULL
       ,FileId               bigint              NULL
       ,OffsetInFile         int                 NULL

        PRIMARY KEY (ResourceTypeId, ResourceSurrogateId)
       ,UNIQUE (ResourceTypeId, ResourceIdInt, Version)
    )
  DECLARE @ReferenceSearchParamsWithIds AS TABLE
    (
        ResourceTypeId           smallint NOT NULL
       ,ResourceSurrogateId      bigint   NOT NULL
       ,SearchParamId            smallint NOT NULL
       ,BaseUri                  varchar(128) COLLATE Latin1_General_100_CS_AS NULL
       ,ReferenceResourceTypeId  smallint NOT NULL
       ,ReferenceResourceIdInt   bigint   NOT NULL

       UNIQUE (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceIdInt) 
    )
  
  INSERT INTO @InputIds SELECT DISTINCT ReferenceResourceTypeId, ReferenceResourceId FROM @ReferenceSearchParams WHERE ReferenceResourceTypeId IS NOT NULL
  INSERT INTO @RTs SELECT DISTINCT ResourceTypeId FROM @InputIds

-- Prepare id map for reference search params Start ---------------------------------------------------------------------------
  WHILE EXISTS (SELECT * FROM @RTs)
  BEGIN
    SET @RT = (SELECT TOP 1 ResourceTypeId FROM @RTs)

    INSERT INTO @ExistingIds 
         ( ResourceTypeId, ResourceIdInt,   ResourceId )
      SELECT          @RT, ResourceIdInt, A.ResourceId
        FROM (SELECT * FROM @InputIds WHERE ResourceTypeId = @RT) A
             JOIN dbo.ResourceIdIntMap B ON B.ResourceTypeId = @RT AND B.ResourceId = A.ResourceId
    
    DELETE FROM @InsertIds

    INSERT INTO @InsertIds 
           (                                       ResourceIndex, ResourceId ) 
      SELECT RowId = row_number() OVER (ORDER BY ResourceId) - 1, ResourceId
        FROM (SELECT ResourceId FROM @InputIds WHERE ResourceTypeId = @RT) A
        WHERE NOT EXISTS (SELECT * FROM @ExistingIds B WHERE B.ResourceTypeId = @RT AND B.ResourceId = A.ResourceId)

    SET @NewIdsCount = (SELECT count(*) FROM @InsertIds)
    IF @NewIdsCount > 0
    BEGIN
      EXECUTE dbo.AssignResourceIdInts @NewIdsCount, @FirstIdInt OUT

      INSERT INTO dbo.ResourceIdIntMap 
           (   ResourceTypeId,               ResourceIdInt,          ResourceId ) 
        OUTPUT            @RT,      inserted.ResourceIdInt, inserted.ResourceId INTO @InsertedIds
        SELECT            @RT, ResourceIndex + @FirstIdInt,          ResourceId
          FROM @InsertIds
    END

    DELETE FROM @RTs WHERE ResourceTypeId = @RT
  END
  
  INSERT INTO @ReferenceSearchParamsWithIds
         (   ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId,                  ReferenceResourceIdInt )
    SELECT A.ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, isnull(C.ResourceIdInt,B.ResourceIdInt)
      FROM (SELECT * FROM @ReferenceSearchParams WHERE ReferenceResourceTypeId IS NOT NULL) A
           LEFT OUTER JOIN @InsertedIds B ON B.ResourceTypeId = A.ReferenceResourceTypeId AND B.ResourceId = A.ReferenceResourceId
           LEFT OUTER JOIN @ExistingIds C ON C.ResourceTypeId = A.ReferenceResourceTypeId AND C.ResourceId = A.ReferenceResourceId
-- Prepare id map for reference search params End ---------------------------------------------------------------------------

  DELETE FROM @InputIds
  DELETE FROM @RTs
  DELETE FROM @InsertedIds
  DELETE FROM @ExistingIds
  
-- Prepare id map for resources Start ---------------------------------------------------------------------------
  INSERT INTO @InputIds SELECT ResourceTypeId, ResourceId 
    FROM (SELECT ResourceTypeId, ResourceId FROM @ResourcesLake
          UNION ALL
          SELECT ResourceTypeId, ResourceId FROM @Resources -- TODO: Remove after deployment
         ) A
    GROUP BY ResourceTypeId, ResourceId
  INSERT INTO @RTs SELECT DISTINCT ResourceTypeId FROM @InputIds

  WHILE EXISTS (SELECT * FROM @RTs)
  BEGIN
    SET @RT = (SELECT TOP 1 ResourceTypeId FROM @RTs)

    INSERT INTO @ExistingIds 
         ( ResourceTypeId, ResourceIdInt,   ResourceId )
      SELECT          @RT, ResourceIdInt, A.ResourceId
        FROM (SELECT * FROM @InputIds WHERE ResourceTypeId = @RT) A
             JOIN dbo.ResourceIdIntMap B ON B.ResourceTypeId = @RT AND B.ResourceId = A.ResourceId
    
    DELETE FROM @InsertIds

    INSERT INTO @InsertIds 
           (                                       ResourceIndex, ResourceId ) 
      SELECT RowId = row_number() OVER (ORDER BY ResourceId) - 1, ResourceId
        FROM (SELECT ResourceId FROM @InputIds WHERE ResourceTypeId = @RT) A
        WHERE NOT EXISTS (SELECT * FROM @ExistingIds B WHERE B.ResourceTypeId = @RT AND B.ResourceId = A.ResourceId)

    SET @NewIdsCount = (SELECT count(*) FROM @InsertIds)
    IF @NewIdsCount > 0
    BEGIN
      EXECUTE dbo.AssignResourceIdInts @NewIdsCount, @FirstIdInt OUT

      INSERT INTO dbo.ResourceIdIntMap 
           (   ResourceTypeId,               ResourceIdInt,          ResourceId ) 
        OUTPUT            @RT,      inserted.ResourceIdInt, inserted.ResourceId INTO @InsertedIds
        SELECT            @RT, ResourceIndex + @FirstIdInt,          ResourceId
          FROM @InsertIds
    END

    DELETE FROM @RTs WHERE ResourceTypeId = @RT
  END
  
  INSERT INTO @ResourcesWithIds
         (   ResourceTypeId,                           ResourceIdInt, Version, HasVersionToCompare, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, KeepHistory, RawResource, IsRawResourceMetaSet, SearchParamHash, FileId, OffsetInFile )
    SELECT A.ResourceTypeId, isnull(C.ResourceIdInt,B.ResourceIdInt), Version, HasVersionToCompare, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, KeepHistory, RawResource, IsRawResourceMetaSet, SearchParamHash, FileId, OffsetInFile
      FROM (SELECT ResourceTypeId, ResourceId, Version, HasVersionToCompare, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, KeepHistory, RawResource, IsRawResourceMetaSet, SearchParamHash, FileId = CASE WHEN OffsetInFile IS NOT NULL THEN @TransactionId END, OffsetInFile FROM @ResourcesLake
            UNION ALL
            SELECT ResourceTypeId, ResourceId, Version, HasVersionToCompare, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, KeepHistory, RawResource, IsRawResourceMetaSet, SearchParamHash,                                                                NULL,         NULL FROM @Resources
           ) A
           LEFT OUTER JOIN @InsertedIds B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
           LEFT OUTER JOIN @ExistingIds C ON C.ResourceTypeId = A.ResourceTypeId AND C.ResourceId = A.ResourceId
  --EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Run',@Action='Insert',@Target='@ResourcesWithIds',@Rows=@@rowcount,@Start=@st

-- Prepare id map for resources End ---------------------------------------------------------------------------
END TRY
BEGIN CATCH
  IF @InitialTranCount = 0 AND @@trancount > 0 ROLLBACK TRANSACTION
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st

  IF error_number() IN (2601, 2627) AND error_message() LIKE '%''dbo.ResourceIdIntMap''%'
  BEGIN
    DELETE FROM @ResourcesWithIds
    DELETE FROM @ReferenceSearchParamsWithIds
    DELETE FROM @InputIds
    DELETE FROM @RTs
    DELETE FROM @InsertedIds
    DELETE FROM @ExistingIds

    GOTO RetryResourceIdIntMapInsert
  END
  ELSE
    THROW
END CATCH

--EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Run',@Text='ResourceIdIntMap populated'

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
            AND B.ResourceId = A.ResourceId
            AND B.Version = A.Version
          OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
    
      IF @@rowcount = (SELECT count(*) FROM @Resources) SET @IsRetry = 1

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
        FROM (SELECT TOP (@DummyTop) * FROM @ResourcesWithIds WHERE HasVersionToCompare = 1) A
             LEFT OUTER JOIN dbo.CurrentResources B -- WITH (UPDLOCK, HOLDLOCK) These locking hints cause deadlocks and are not needed. Racing might lead to tries to insert dups in unique index (with version key), but it will fail anyway, and in no case this will cause incorrect data saved.
               ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceIdInt = A.ResourceIdInt
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))

    IF @RaiseExceptionOnConflict = 1 AND EXISTS (SELECT * FROM @ResourceInfos WHERE PreviousVersion IS NOT NULL AND Version <= PreviousVersion)
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
           ( ResourceTypeId, ResourceIdInt, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash,  TransactionId, FileId, OffsetInFile )
      SELECT ResourceTypeId, ResourceIdInt, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash, @TransactionId, FileId, OffsetInFile
        FROM @ResourcesWithIds
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.ResourceWriteClaim 
           ( ResourceSurrogateId, ClaimTypeId, ClaimValue )
      SELECT ResourceSurrogateId, ClaimTypeId, ClaimValue
        FROM @ResourceWriteClaims
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.ResourceReferenceSearchParams 
           ( ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceIdInt )
      SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceIdInt
        FROM @ReferenceSearchParamsWithIds
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.StringReferenceSearchParams 
           ( ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceId )
      SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceId
        FROM @ReferenceSearchParams
        WHERE ReferenceResourceTypeId IS NULL
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

    INSERT INTO dbo.ResourceReferenceSearchParams 
           (   ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceIdInt )
      SELECT A.ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceIdInt
        FROM (SELECT TOP (@DummyTop) * FROM @ReferenceSearchParamsWithIds) A
        WHERE EXISTS (SELECT * FROM @Existing B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.SurrogateId = A.ResourceSurrogateId)
          AND NOT EXISTS (SELECT * FROM dbo.ResourceReferenceSearchParams C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.StringReferenceSearchParams 
           (   ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceId )
      SELECT A.ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceId
        FROM (SELECT TOP (@DummyTop) * FROM @ReferenceSearchParams WHERE ReferenceResourceTypeId IS NULL) A
        WHERE EXISTS (SELECT * FROM @Existing B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.SurrogateId = A.ResourceSurrogateId)
          AND NOT EXISTS (SELECT * FROM dbo.StringReferenceSearchParams C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
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

  IF @RaiseExceptionOnConflict = 1 AND error_number() IN (2601, 2627) AND (error_message() LIKE '%''dbo.Resource%' OR error_message() LIKE '%''dbo.CurrentResources%' OR error_message() LIKE '%''dbo.HistoryResources%' OR error_message() LIKE '%''dbo.RawResources''%') -- handles old and separated tables
    THROW 50409, 'Resource has been recently updated or added, please compare the resource content in code for any duplicate updates', 1;
  ELSE
    THROW
END CATCH
GO
COMMIT TRANSACTION
GO
ALTER PROCEDURE dbo.GetResources @ResourceKeys dbo.ResourceKeyList READONLY
AS
set nocount on
DECLARE @st datetime = getUTCdate()
       ,@SP varchar(100) = 'GetResources'
       ,@InputRows int
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
            ,FileId
            ,OffsetInFile
        FROM (SELECT * FROM @ResourceKeys) A
             INNER LOOP JOIN dbo.Resource B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId AND B.Version = A.Version
        OPTION (MAXDOP 1)
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
                    ,FileId
                    ,OffsetInFile
                FROM (SELECT * FROM @ResourceKeys WHERE Version IS NOT NULL) A
                     INNER LOOP JOIN dbo.Resource B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = B.ResourceId AND B.Version = A.Version
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
                    ,FileId
                    ,OffsetInFile
                FROM (SELECT * FROM @ResourceKeys WHERE Version IS NULL) A
                     INNER LOOP JOIN dbo.Resource B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = B.ResourceId AND B.IsHistory = 0
             ) A
        OPTION (MAXDOP 1)
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
          ,FileId
          ,OffsetInFile
      FROM (SELECT * FROM @ResourceKeys) A
           INNER LOOP JOIN dbo.Resource B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId AND B.IsHistory = 0
      OPTION (MAXDOP 1)

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
ALTER PROCEDURE dbo.GetResourcesByTransactionId @TransactionId bigint, @IncludeHistory bit = 0, @ReturnResourceKeysOnly bit = 0
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(100) = 'T='+convert(varchar,@TransactionId)+' H='+convert(varchar,@IncludeHistory)
       ,@st datetime = getUTCdate()

BEGIN TRY
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
          ,FileId
          ,OffsetInFile
      FROM dbo.Resource
      WHERE TransactionId = @TransactionId AND (IsHistory = 0 OR @IncludeHistory = 1)
      OPTION (MAXDOP 1)
  ELSE
    SELECT ResourceTypeId
          ,ResourceId
          ,ResourceSurrogateId
          ,Version
          ,IsDeleted
      FROM dbo.Resource
      WHERE TransactionId = @TransactionId AND (IsHistory = 0 OR @IncludeHistory = 1)
      OPTION (MAXDOP 1)

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
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

  UPDATE dbo.Resource
    SET IsDeleted = 1
       ,RawResource = 0xF -- invisible value
       ,SearchParamHash = NULL
       ,HistoryTransactionId = @TransactionId
    OUTPUT deleted.ResourceSurrogateId INTO @SurrogateIds
    WHERE ResourceTypeId = @ResourceTypeId
      AND ResourceId = @ResourceId
      AND (@KeepCurrentVersion = 0 OR IsHistory = 1)
        --AND RawResource <> 0xF -- Cannot check this as resource can be stored in ADLS

  IF @KeepCurrentVersion = 0
  BEGIN
    -- PAGLOCK allows deallocation of empty page without waiting for ghost cleanup 
    DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.ResourceWriteClaim B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM dbo.ReferenceSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
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
-- Special version before data movement
ALTER PROCEDURE dbo.GetResourceVersions @ResourceDateKeys dbo.ResourceDateKeyList READONLY
AS
-- This stored procedure allows to identifiy if version gap is available and checks dups on lastUpdated
set nocount on
DECLARE @st datetime = getUTCdate()
       ,@SP varchar(100) = 'GetResourceVersions'
       ,@Mode varchar(100) = 'Rows='+convert(varchar,(SELECT count(*) FROM @ResourceDateKeys))
       ,@DummyTop bigint = 9223372036854775807

BEGIN TRY
  SELECT A.ResourceTypeId
        ,A.ResourceId
        ,A.ResourceSurrogateId
        -- set version to 0 if there is no gap available, or lastUpdated is already used. It would indicate potential conflict for the caller.
        ,Version = CASE
                     -- ResourceSurrogateId is generated from lastUpdated only without extra bits at the end. Need to ckeck interval (0..79999) on resource id level.
                     WHEN D.Version IS NOT NULL THEN 0 -- input lastUpdated matches stored 
                     WHEN isnull(U.Version, 1) - isnull(L.Version, 0) > ResourceIndex THEN isnull(U.Version, 1) - ResourceIndex -- gap is available
                     ELSE isnull(M.Version, 0) - ResourceIndex -- late arrival
                   END
        ,MatchedVersion = isnull(D.Version,0)
        ,MatchedRawResource = D.RawResource
        ,MatchedFileId = D.FileId
        ,MatchedOffsetInFile = D.OffsetInFile
        -- ResourceIndex allows to deal with more than one late arrival per resource 
    FROM (SELECT TOP (@DummyTop) A.*, ResourceIndex = convert(int,row_number() OVER (PARTITION BY A.ResourceTypeId, A.ResourceId ORDER BY ResourceSurrogateId DESC)) 
            FROM @ResourceDateKeys A
         ) A
         OUTER APPLY (SELECT TOP 1 * FROM dbo.Resource B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId AND B.Version > 0 AND B.ResourceSurrogateId < A.ResourceSurrogateId ORDER BY B.ResourceSurrogateId DESC) L -- lower
         OUTER APPLY (SELECT TOP 1 * FROM dbo.Resource B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId AND B.Version > 0 AND B.ResourceSurrogateId > A.ResourceSurrogateId ORDER BY B.ResourceSurrogateId) U -- upper
         OUTER APPLY (SELECT TOP 1 * FROM dbo.Resource B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId AND B.Version < 0 ORDER BY B.Version) M -- minus
         OUTER APPLY (SELECT TOP 1 * FROM dbo.Resource B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId AND B.ResourceSurrogateId BETWEEN A.ResourceSurrogateId AND A.ResourceSurrogateId + 79999) D -- date
    OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
-- Move data
set nocount on
INSERT INTO dbo.Parameters (Id, Char) SELECT 'LakeSchemaUpgrade', 'LogEvent'

DECLARE @Types TABLE (ResourceTypeId smallint PRIMARY KEY, Name varchar(100))
DECLARE @MaxSurrogateId bigint = 0
       ,@ResourceTypeId smallint

IF NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = 'LakeSchemaUpgrade.MaxSurrogateId') -- DELETE FROM dbo.Parameters WHERE Id = 'LakeSchemaUpgrade.MaxSurrogateId'
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
  INSERT INTO dbo.Parameters (Id, Bigint) SELECT 'LakeSchemaUpgrade.MaxSurrogateId', @MaxSurrogateId
END
  
SET @MaxSurrogateId = (SELECT Bigint FROM dbo.Parameters WHERE Id = 'LakeSchemaUpgrade.MaxSurrogateId')
EXECUTE dbo.LogEvent @Process='LakeSchemaUpgrade',@Status='Run',@Target='@MaxSurrogateId',@Action='Select',@Text=@MaxSurrogateId

DECLARE @Process varchar(100) = 'LakeSchemaUpgrade.MoveResources'
       ,@Id varchar(100) = 'LakeSchemaUpgrade.MoveResources.LastProcessed.TypeId.SurrogateId' -- SELECT * FROM Parameters
       ,@SurrogateId bigint
       ,@RowsToProcess int
       ,@ProcessedResources int
       ,@ReportDate datetime = getUTCdate()
       ,@DummyTop bigint = 9223372036854775807
       ,@Rows int
       ,@CurrentMaxSurrogateId bigint
       ,@LastProcessed varchar(100)
       ,@st datetime
       ,@NewIdsCount int
       ,@Count int
       ,@FirstIdInt bigint
       ,@RT smallint

DECLARE @RTs AS TABLE (ResourceTypeId smallint NOT NULL PRIMARY KEY)
DECLARE @InputIds AS TABLE (ResourceTypeId smallint NOT NULL, ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY (ResourceTypeId, ResourceId))
DECLARE @ExistingIds AS TABLE (ResourceTypeId smallint NOT NULL, ResourceIdInt bigint NOT NULL, ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY (ResourceTypeId, ResourceId))
DECLARE @InsertIds AS TABLE (ResourceIndex int NOT NULL, ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL)
DECLARE @InsertedIds AS TABLE (ResourceTypeId smallint NOT NULL, ResourceIdInt bigint NOT NULL, ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY (ResourceTypeId, ResourceId))

BEGIN TRY
  EXECUTE dbo.LogEvent @Process=@Process,@Status='Start'

  INSERT INTO dbo.Parameters (Id, Char) SELECT @Id, '0.0'

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
    WHILE @CurrentMaxSurrogateId IS NOT NULL
    BEGIN
      BEGIN TRANSACTION

      SET @CurrentMaxSurrogateId = NULL
      SELECT @CurrentMaxSurrogateId = max(ResourceSurrogateId), @RowsToProcess = count(*)
        FROM (SELECT TOP 5000 ResourceSurrogateId
                FROM dbo.ResourceTbl WITH (HOLDLOCK, ROWLOCK)
                WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId > @SurrogateId AND ResourceSurrogateId <= @MaxSurrogateId 
                ORDER BY ResourceSurrogateId
             ) A

      IF @CurrentMaxSurrogateId IS NOT NULL
      BEGIN
        SET @LastProcessed = convert(varchar,@ResourceTypeId)+'.'+convert(varchar,@CurrentMaxSurrogateId)

        DELETE FROM @InputIds
        DELETE FROM @RTs
        DELETE FROM @InsertedIds
        DELETE FROM @ExistingIds

        RetryResourceIdIntMapInsert:
        BEGIN TRY
          SET @st = getUTCdate()
          INSERT INTO @InputIds SELECT DISTINCT ReferenceResourceTypeId, ReferenceResourceId 
            FROM dbo.ReferenceSearchParam 
            WHERE ResourceTypeId = @ResourceTypeId 
              AND ResourceSurrogateId > @SurrogateId AND ResourceSurrogateId <= @CurrentMaxSurrogateId 
              AND ReferenceResourceTypeId IS NOT NULL
          EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Mode=@LastProcessed,@Target='ReferenceSearchParam.@InputIds',@Action='Insert',@Rows=@@rowcount,@Start=@st

          INSERT INTO @RTs SELECT DISTINCT ResourceTypeId FROM @InputIds

          -- Prepare id map for reference search params Start ---------------------------------------------------------------------------
          SET @st = getUTCdate()
          SET @Count = 0
          WHILE EXISTS (SELECT * FROM @RTs)
          BEGIN
            SET @RT = (SELECT TOP 1 ResourceTypeId FROM @RTs)

            INSERT INTO @ExistingIds 
                 ( ResourceTypeId, ResourceIdInt,   ResourceId )
              SELECT          @RT, ResourceIdInt, A.ResourceId
                FROM (SELECT * FROM @InputIds WHERE ResourceTypeId = @RT) A
                     JOIN dbo.ResourceIdIntMap B ON B.ResourceTypeId = @RT AND B.ResourceId = A.ResourceId
    
            DELETE FROM @InsertIds

            INSERT INTO @InsertIds 
                   (                                       ResourceIndex, ResourceId ) 
              SELECT RowId = row_number() OVER (ORDER BY ResourceId) - 1, ResourceId
                FROM (SELECT ResourceId FROM @InputIds WHERE ResourceTypeId = @RT) A
                WHERE NOT EXISTS (SELECT * FROM @ExistingIds B WHERE B.ResourceTypeId = @RT AND B.ResourceId = A.ResourceId)

            SET @NewIdsCount = (SELECT count(*) FROM @InsertIds)
            SET @Count += @NewIdsCount
            IF @NewIdsCount > 0
            BEGIN
              EXECUTE dbo.AssignResourceIdInts @NewIdsCount, @FirstIdInt OUT

              INSERT INTO dbo.ResourceIdIntMap 
                   (   ResourceTypeId,               ResourceIdInt,          ResourceId ) 
                OUTPUT            @RT,      inserted.ResourceIdInt, inserted.ResourceId INTO @InsertedIds
                SELECT            @RT, ResourceIndex + @FirstIdInt,          ResourceId
                  FROM @InsertIds
            END

            DELETE FROM @RTs WHERE ResourceTypeId = @RT
          END
          EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Mode=@LastProcessed,@Target='ReferenceSearchParam.ResourceIdIntMap',@Action='Insert',@Rows=@Count,@Start=@st
  
          SET @st = getUTCdate()
          SET @Count = 0
          INSERT INTO dbo.ResourceReferenceSearchParams
                 (   ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId,                 ReferenceResourceIdInt )
            SELECT A.ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, isnull(C.ResourceIdInt,B.ResourceIdInt)
              FROM (SELECT * 
                      FROM dbo.ReferenceSearchParam 
                      WHERE ResourceTypeId = @ResourceTypeId 
                        AND ResourceSurrogateId > @SurrogateId AND ResourceSurrogateId <= @CurrentMaxSurrogateId 
                        AND ReferenceResourceTypeId IS NOT NULL
                   ) A
                   LEFT OUTER JOIN @InsertedIds B ON B.ResourceTypeId = A.ReferenceResourceTypeId AND B.ResourceId = A.ReferenceResourceId
                   LEFT OUTER JOIN @ExistingIds C ON C.ResourceTypeId = A.ReferenceResourceTypeId AND C.ResourceId = A.ReferenceResourceId
          SET @Count += @@rowcount

          INSERT INTO dbo.StringReferenceSearchParams 
                 ( ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceId )
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceId
              FROM dbo.ReferenceSearchParamTbl
              WHERE ResourceTypeId = @ResourceTypeId 
                AND ResourceSurrogateId > @SurrogateId AND ResourceSurrogateId <= @CurrentMaxSurrogateId 
                AND ReferenceResourceTypeId IS NULL
          SET @Count += @@rowcount
          EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Mode=@LastProcessed,@Target='ReferenceSearchParam',@Action='Insert',@Rows=@Count,@Start=@st

          SET @st = getUTCdate()
          DELETE FROM dbo.ReferenceSearchParamTbl
            WHERE ResourceTypeId = @ResourceTypeId 
              AND ResourceSurrogateId > @SurrogateId AND ResourceSurrogateId <= @CurrentMaxSurrogateId 
          SET @Count += @@rowcount
          EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Mode=@LastProcessed,@Target='ReferenceSearchParamTbl',@Action='Delete',@Rows=@Count,@Start=@st
          -- Prepare id map for reference search params End ---------------------------------------------------------------------------

          DELETE FROM @InputIds
          DELETE FROM @RTs
          DELETE FROM @InsertedIds
          DELETE FROM @ExistingIds
  
          SET @st = getUTCdate()
          INSERT INTO @InputIds SELECT @ResourceTypeId, ResourceId 
            FROM (SELECT ResourceId 
                    FROM dbo.ResourceTbl 
                    WHERE ResourceTypeId = @ResourceTypeId 
                      AND ResourceSurrogateId > @SurrogateId AND ResourceSurrogateId <= @CurrentMaxSurrogateId 
                 ) A
            GROUP BY ResourceId
          EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Mode=@LastProcessed,@Target='Resource.@InputIds',@Action='Insert',@Rows=@@rowcount,@Start=@st

          SET @st = getUTCdate()
          SET @Count = 0
          INSERT INTO @ExistingIds 
                 (  ResourceTypeId, ResourceIdInt,   ResourceId )
            SELECT @ResourceTypeId, ResourceIdInt, A.ResourceId
              FROM @InputIds A
                   JOIN dbo.ResourceIdIntMap B ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceId = A.ResourceId
    
          DELETE FROM @InsertIds

          INSERT INTO @InsertIds 
                 (                                       ResourceIndex, ResourceId ) 
            SELECT RowId = row_number() OVER (ORDER BY ResourceId) - 1, ResourceId
              FROM @InputIds A
              WHERE NOT EXISTS (SELECT * FROM @ExistingIds B WHERE B.ResourceTypeId = @ResourceTypeId AND B.ResourceId = A.ResourceId)

          SET @NewIdsCount = (SELECT count(*) FROM @InsertIds)
          IF @NewIdsCount > 0
          BEGIN
            EXECUTE dbo.AssignResourceIdInts @NewIdsCount, @FirstIdInt OUT

            INSERT INTO dbo.ResourceIdIntMap 
                  (   ResourceTypeId,               ResourceIdInt,          ResourceId ) 
              OUTPUT @ResourceTypeId,      inserted.ResourceIdInt, inserted.ResourceId INTO @InsertedIds
              SELECT @ResourceTypeId, ResourceIndex + @FirstIdInt,          ResourceId
                FROM @InsertIds
            SET @Count = @@rowcount
          END
          EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Mode=@LastProcessed,@Target='Resource.ResourceIdIntMap',@Action='Insert',@Rows=@Count,@Start=@st

          SET @st = getUTCdate()
          INSERT INTO dbo.Resource
                 (   ResourceTypeId,                           ResourceIdInt, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash, FileId, OffsetInFile )
            SELECT A.ResourceTypeId, isnull(C.ResourceIdInt,B.ResourceIdInt), Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash,   NULL,         NULL
              FROM dbo.ResourceTbl A 
                   LEFT OUTER JOIN @InsertedIds B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
                   LEFT OUTER JOIN @ExistingIds C ON C.ResourceTypeId = A.ResourceTypeId AND C.ResourceId = A.ResourceId
              WHERE A.ResourceTypeId = @ResourceTypeId 
                AND A.ResourceSurrogateId > @SurrogateId AND ResourceSurrogateId <= @CurrentMaxSurrogateId
          EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Mode=@LastProcessed,@Target='Resource',@Action='Insert',@Rows=@@rowcount,@Start=@st

          SET @st = getUTCdate()
          DELETE FROM dbo.ResourceTbl
            WHERE ResourceTypeId = @ResourceTypeId 
              AND ResourceSurrogateId > @SurrogateId AND ResourceSurrogateId <= @CurrentMaxSurrogateId
          EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Mode=@LastProcessed,@Target='ResourceTbl',@Action='Delete',@Rows=@@rowcount,@Start=@st
        END TRY
        BEGIN CATCH
          EXECUTE dbo.LogEvent @Process=@Process,@Mode=@LastProcessed,@Status='Error',@Start=@st

          IF error_number() IN (2601, 2627) AND error_message() LIKE '%''dbo.ResourceIdIntMap''%'
          BEGIN
            GOTO RetryResourceIdIntMapInsert
          END
          ELSE
            THROW
        END CATCH

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

  EXECUTE dbo.LogEvent @Process=@Process,@Status='End'
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@Process,@Status='Error';
  THROW
END CATCH
GO
ALTER VIEW dbo.ReferenceSearchParam
AS
SELECT A.ResourceTypeId
      ,ResourceSurrogateId
      ,SearchParamId
      ,BaseUri
      ,ReferenceResourceTypeId
      ,ReferenceResourceId = B.ResourceId
      ,IsResourceRef
  FROM dbo.ResourceReferenceSearchParams A
       LEFT OUTER JOIN dbo.ResourceIdIntMap B ON B.ResourceTypeId = A.ReferenceResourceTypeId AND B.ResourceIdInt = A.ReferenceResourceIdInt
UNION ALL
SELECT ResourceTypeId
      ,ResourceSurrogateId
      ,SearchParamId
      ,BaseUri
      ,NULL
      ,ReferenceResourceId
      ,IsResourceRef
  FROM dbo.StringReferenceSearchParams
GO
ALTER VIEW dbo.Resource
AS 
SELECT A.ResourceTypeId
      ,A.ResourceSurrogateId
      ,ResourceId
      ,A.ResourceIdInt
      ,Version
      ,IsHistory
      ,IsDeleted
      ,RequestMethod
      ,B.RawResource
      ,IsRawResourceMetaSet
      ,SearchParamHash
      ,TransactionId 
      ,HistoryTransactionId
      ,FileId
      ,OffsetInFile
  FROM dbo.CurrentResources A
       LEFT OUTER JOIN dbo.RawResources B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
       LEFT OUTER JOIN dbo.ResourceIdIntMap C ON C.ResourceTypeId = A.ResourceTypeId AND C.ResourceIdInt = A.ResourceIdInt
UNION ALL
SELECT A.ResourceTypeId
      ,A.ResourceSurrogateId
      ,ResourceId
      ,A.ResourceIdInt
      ,Version
      ,IsHistory
      ,IsDeleted
      ,RequestMethod
      ,B.RawResource
      ,IsRawResourceMetaSet
      ,SearchParamHash
      ,TransactionId 
      ,HistoryTransactionId
      ,FileId
      ,OffsetInFile
  FROM dbo.HistoryResources A
       LEFT OUTER JOIN dbo.RawResources B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
       LEFT OUTER JOIN dbo.ResourceIdIntMap C ON C.ResourceTypeId = A.ResourceTypeId AND C.ResourceIdInt = A.ResourceIdInt
GO
ALTER PROCEDURE dbo.GetResourceVersions @ResourceDateKeys dbo.ResourceDateKeyList READONLY
AS
-- This stored procedure allows to identifiy if version gap is available and checks dups on lastUpdated
set nocount on
DECLARE @st datetime = getUTCdate()
       ,@SP varchar(100) = 'GetResourceVersions'
       ,@Mode varchar(100) = 'Rows='+convert(varchar,(SELECT count(*) FROM @ResourceDateKeys))
       ,@DummyTop bigint = 9223372036854775807

BEGIN TRY
  SELECT A.ResourceTypeId
        ,A.ResourceId
        ,A.ResourceSurrogateId
        -- set version to 0 if there is no gap available, or lastUpdated is already used. It would indicate potential conflict for the caller.
        ,Version = CASE
                     -- ResourceSurrogateId is generated from lastUpdated only without extra bits at the end. Need to ckeck interval (0..79999) on resource id level.
                     WHEN D.Version IS NOT NULL THEN 0 -- input lastUpdated matches stored 
                     WHEN isnull(U.Version, 1) - isnull(L.Version, 0) > ResourceIndex THEN isnull(U.Version, 1) - ResourceIndex -- gap is available
                     ELSE isnull(M.Version, 0) - ResourceIndex -- late arrival
                   END
        ,MatchedVersion = isnull(D.Version,0)
        ,MatchedRawResource = D.RawResource
        ,MatchedFileId = D.FileId
        ,MatchedOffsetInFile = D.OffsetInFile
        -- ResourceIndex allows to deal with more than one late arrival per resource 
    FROM (SELECT TOP (@DummyTop) A.*, M.ResourceIdInt, ResourceIndex = convert(int,row_number() OVER (PARTITION BY A.ResourceTypeId, A.ResourceId ORDER BY ResourceSurrogateId DESC)) 
            FROM @ResourceDateKeys A
                 LEFT OUTER JOIN dbo.ResourceIdIntMap M WITH (INDEX = U_ResourceIdIntMap_ResourceId_ResourceTypeId) ON M.ResourceTypeId = A.ResourceTypeId AND M.ResourceId = A.ResourceId
         ) A
         OUTER APPLY (SELECT TOP 1 * FROM dbo.Resource B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceIdInt = A.ResourceIdInt AND B.Version > 0 AND B.ResourceSurrogateId < A.ResourceSurrogateId ORDER BY B.ResourceSurrogateId DESC) L -- lower
         OUTER APPLY (SELECT TOP 1 * FROM dbo.Resource B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceIdInt = A.ResourceIdInt AND B.Version > 0 AND B.ResourceSurrogateId > A.ResourceSurrogateId ORDER BY B.ResourceSurrogateId) U -- upper
         OUTER APPLY (SELECT TOP 1 * FROM dbo.Resource B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceIdInt = A.ResourceIdInt AND B.Version < 0 ORDER BY B.Version) M -- minus
         OUTER APPLY (SELECT TOP 1 * FROM dbo.Resource B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceIdInt = A.ResourceIdInt AND B.ResourceSurrogateId BETWEEN A.ResourceSurrogateId AND A.ResourceSurrogateId + 79999) D -- date
    OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
