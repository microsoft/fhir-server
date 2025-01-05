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
   ,FileId               bigint              NULL
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
     ,IsHistory                   bit                     NOT NULL CONSTRAINT DF_CurrentResources_IsHistory DEFAULT 0, CONSTRAINT CH_CurrentResources_IsHistory CHECK (IsHistory = 0)
     ,IsDeleted                   bit                     NOT NULL
     ,RequestMethod               varchar(10)             NULL
     ,IsRawResourceMetaSet        bit                     NOT NULL CONSTRAINT DF_CurrentResources_IsRawResourceMetaSet DEFAULT 0
     ,SearchParamHash             varchar(64)             NULL
     ,TransactionId               bigint                  NULL
     ,HistoryTransactionId        bigint                  NULL
     ,FileId                      bigint                  NULL
     ,OffsetInFile                int                     NULL

      CONSTRAINT PKC_CurrentResources_ResourceTypeId_ResourceSurrogateId PRIMARY KEY CLUSTERED (ResourceTypeId, ResourceSurrogateId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
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

      CONSTRAINT PKC_HistoryResources_ResourceTypeId_ResourceSurrogateId PRIMARY KEY CLUSTERED (ResourceTypeId, ResourceSurrogateId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
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
      ,RawResource
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
      ,RawResource
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

  EXECUTE('
ALTER PROCEDURE dbo.GetResources @ResourceKeys dbo.ResourceKeyList READONLY
AS
set nocount on
DECLARE @st datetime = getUTCdate()
       ,@SP varchar(100) = ''GetResources''
       ,@InputRows int
       ,@DummyTop bigint = 9223372036854775807
       ,@NotNullVersionExists bit 
       ,@NullVersionExists bit
       ,@MinRT smallint
       ,@MaxRT smallint

SELECT @MinRT = min(ResourceTypeId), @MaxRT = max(ResourceTypeId), @InputRows = count(*), @NotNullVersionExists = max(CASE WHEN Version IS NOT NULL THEN 1 ELSE 0 END), @NullVersionExists = max(CASE WHEN Version IS NULL THEN 1 ELSE 0 END) FROM @ResourceKeys

DECLARE @Mode varchar(100) = ''RT=[''+convert(varchar,@MinRT)+'',''+convert(varchar,@MaxRT)+''] Cnt=''+convert(varchar,@InputRows)+'' NNVE=''+convert(varchar,@NotNullVersionExists)+'' NVE=''+convert(varchar,@NullVersionExists)

BEGIN TRY
  IF @NotNullVersionExists = 1
    IF @NullVersionExists = 0
      SELECT *
        FROM (SELECT B.ResourceTypeId
                    ,B.ResourceId
                    ,ResourceSurrogateId
                    ,C.Version
                    ,IsDeleted
                    ,IsHistory
                    ,RawResource
                    ,IsRawResourceMetaSet
                    ,SearchParamHash
                    ,FileId
                    ,OffsetInFile
                FROM (SELECT * FROM @ResourceKeys) A
                     INNER LOOP JOIN dbo.ResourceIdIntMap B WITH (INDEX = U_ResourceIdIntMap_ResourceId_ResourceTypeId) ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
                     INNER LOOP JOIN dbo.Resource C ON C.ResourceTypeId = A.ResourceTypeId AND C.ResourceIdInt = B.ResourceIdInt AND C.Version = A.Version
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
                    ,NULL
                    ,NULL
                FROM (SELECT TOP (@DummyTop) * FROM @ResourceKeys) A
                     JOIN dbo.ResourceTbl B WITH (INDEX = IX_Resource_ResourceTypeId_ResourceId_Version) ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId AND B.Version = A.Version
             ) A
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
    ELSE
      SELECT *
        FROM (SELECT B.ResourceTypeId
                    ,B.ResourceId
                    ,ResourceSurrogateId
                    ,C.Version
                    ,IsDeleted
                    ,IsHistory
                    ,RawResource
                    ,IsRawResourceMetaSet
                    ,SearchParamHash
                    ,FileId
                    ,OffsetInFile
                FROM (SELECT * FROM @ResourceKeys WHERE Version IS NOT NULL) A
                     INNER LOOP JOIN dbo.ResourceIdIntMap B WITH (INDEX = U_ResourceIdIntMap_ResourceId_ResourceTypeId) ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
                     INNER LOOP JOIN dbo.Resource C ON C.ResourceTypeId = A.ResourceTypeId AND C.ResourceIdInt = B.ResourceIdInt AND C.Version = A.Version
              UNION ALL
              SELECT B.ResourceTypeId
                    ,B.ResourceId
                    ,C.ResourceSurrogateId
                    ,C.Version
                    ,IsDeleted
                    ,IsHistory
                    ,RawResource
                    ,IsRawResourceMetaSet
                    ,SearchParamHash
                    ,FileId
                    ,OffsetInFile
                FROM (SELECT * FROM @ResourceKeys WHERE Version IS NULL) A
                     INNER LOOP JOIN dbo.ResourceIdIntMap B WITH (INDEX = U_ResourceIdIntMap_ResourceId_ResourceTypeId) ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
                     INNER LOOP JOIN dbo.CurrentResources C ON C.ResourceTypeId = A.ResourceTypeId AND C.ResourceIdInt = B.ResourceIdInt AND C.IsHistory = 0
                     LEFT OUTER JOIN dbo.RawResources D ON D.ResourceTypeId = A.ResourceTypeId AND D.ResourceSurrogateId = C.ResourceSurrogateId
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
                    ,NULL
                    ,NULL
                FROM (SELECT TOP (@DummyTop) * FROM @ResourceKeys WHERE Version IS NOT NULL) A
                     JOIN dbo.ResourceTbl B WITH (INDEX = IX_Resource_ResourceTypeId_ResourceId_Version) ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId AND B.Version = A.Version
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
                    ,NULL
                    ,NULL
                FROM (SELECT TOP (@DummyTop) * FROM @ResourceKeys WHERE Version IS NULL) A
                     JOIN dbo.ResourceTbl B WITH (INDEX = IX_Resource_ResourceTypeId_ResourceId) ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
                WHERE IsHistory = 0
           ) A
      OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
  ELSE
    SELECT *
      FROM (SELECT B.ResourceTypeId
                  ,B.ResourceId
                  ,C.ResourceSurrogateId
                  ,C.Version
                  ,IsDeleted
                  ,IsHistory
                  ,RawResource
                  ,IsRawResourceMetaSet
                  ,SearchParamHash
                  ,FileId
                  ,OffsetInFile
              FROM (SELECT * FROM @ResourceKeys) A
                   INNER LOOP JOIN dbo.ResourceIdIntMap B WITH (INDEX = U_ResourceIdIntMap_ResourceId_ResourceTypeId) ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
                   INNER LOOP JOIN dbo.CurrentResources C ON C.ResourceTypeId = A.ResourceTypeId AND C.ResourceIdInt = B.ResourceIdInt
                   LEFT OUTER JOIN dbo.RawResources D ON D.ResourceTypeId = A.ResourceTypeId AND D.ResourceSurrogateId = C.ResourceSurrogateId
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
                  ,NULL
                  ,NULL
              FROM (SELECT TOP (@DummyTop) * FROM @ResourceKeys) A
                   JOIN dbo.ResourceTbl B WITH (INDEX = IX_Resource_ResourceTypeId_ResourceId) ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
              WHERE IsHistory = 0
           ) A
      OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status=''End'',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status=''Error'',@Start=@st;
  THROW
END CATCH
   ')

  COMMIT TRANSACTION
END
GO
CREATE OR ALTER VIEW dbo.CurrentResource
AS
SELECT A.ResourceTypeId
      ,A.ResourceSurrogateId
      ,ResourceId
      ,A.ResourceIdInt
      ,Version
      ,IsHistory
      ,IsDeleted
      ,RequestMethod
      ,RawResource
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
  WHERE IsHistory = 0
GO
CREATE OR ALTER TRIGGER dbo.ResourceIns ON dbo.Resource INSTEAD OF INSERT
AS
BEGIN
  INSERT INTO dbo.RawResources
         ( ResourceTypeId, ResourceSurrogateId, RawResource )
    SELECT ResourceTypeId, ResourceSurrogateId, RawResource
      FROM Inserted A
      WHERE RawResource IS NOT NULL
        AND NOT EXISTS (SELECT * FROM dbo.RawResources B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId)

  INSERT INTO dbo.CurrentResources
         ( ResourceTypeId, ResourceSurrogateId, ResourceIdInt, Version, IsDeleted, RequestMethod, IsRawResourceMetaSet, SearchParamHash, TransactionId, HistoryTransactionId, FileId, OffsetInFile )
    SELECT ResourceTypeId, ResourceSurrogateId, ResourceIdInt, Version, IsDeleted, RequestMethod, IsRawResourceMetaSet, SearchParamHash, TransactionId, HistoryTransactionId, FileId, OffsetInFile
      FROM Inserted A
      WHERE IsHistory = 0
        AND NOT EXISTS (SELECT * FROM dbo.CurrentResources B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId)

  INSERT INTO dbo.HistoryResources
         ( ResourceTypeId, ResourceSurrogateId, ResourceIdInt, Version, IsDeleted, RequestMethod, IsRawResourceMetaSet, SearchParamHash, TransactionId, HistoryTransactionId, FileId, OffsetInFile )
    SELECT ResourceTypeId, ResourceSurrogateId, ResourceIdInt, Version, IsDeleted, RequestMethod, IsRawResourceMetaSet, SearchParamHash, TransactionId, HistoryTransactionId, FileId, OffsetInFile
      FROM Inserted A
      WHERE IsHistory = 1
        AND NOT EXISTS (SELECT * FROM dbo.HistoryResources B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId)
END
GO
CREATE OR ALTER TRIGGER dbo.ResourceUpd ON dbo.Resource INSTEAD OF UPDATE
AS
BEGIN
  IF UPDATE(IsDeleted) AND UPDATE(RawResource) AND UPDATE(SearchParamHash) AND UPDATE(HistoryTransactionId) AND NOT UPDATE(IsHistory) -- hard delete resource
  BEGIN
    UPDATE B
      SET IsDeleted = A.IsDeleted
         ,SearchParamHash = A.SearchParamHash
         ,HistoryTransactionId = A.HistoryTransactionId
         ,RawResource = A.RawResource
      FROM Inserted A
           JOIN dbo.ResourceTbl B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId

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
           JOIN dbo.ResourceTbl B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
      WHERE A.IsHistory = 0

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
           JOIN dbo.ResourceTbl B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId

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
           JOIN dbo.ResourceTbl B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId

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

  UPDATE A
    SET IsHistory = 1
    FROM dbo.ResourceTbl A
    WHERE EXISTS (SELECT * FROM Inserted B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId AND B.IsHistory = 1)

  DELETE FROM A
    FROM dbo.CurrentResources A
    WHERE EXISTS (SELECT * FROM Inserted B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId AND B.IsHistory = 1)

  INSERT INTO dbo.HistoryResources
         ( ResourceTypeId, ResourceSurrogateId, ResourceIdInt, Version, IsDeleted, RequestMethod, IsRawResourceMetaSet, SearchParamHash, TransactionId, HistoryTransactionId, FileId, OffsetInFile )
    SELECT ResourceTypeId, ResourceSurrogateId, ResourceIdInt, Version, IsDeleted, RequestMethod, IsRawResourceMetaSet, SearchParamHash, TransactionId, HistoryTransactionId, FileId, OffsetInFile
      FROM Inserted
      WHERE IsHistory = 1
        AND ResourceIdInt IS NOT NULL
END
GO
CREATE OR ALTER TRIGGER dbo.ResourceDel ON dbo.Resource INSTEAD OF DELETE
AS
BEGIN
  DELETE FROM A
    FROM dbo.ResourceTbl A
    WHERE EXISTS (SELECT * FROM Deleted B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId)

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
      ,ReferenceResourceIdInt
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
      ,NULL
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
BEGIN TRY
  BEGIN TRANSACTION -- update ReferenceSearchParamList

  DROP PROCEDURE CaptureResourceIdsForChanges
  DROP PROCEDURE MergeResources
  DROP PROCEDURE UpdateResourceSearchParams
  DROP TYPE ReferenceSearchParamList

  EXECUTE('
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
')
  EXECUTE('
CREATE PROCEDURE dbo.CaptureResourceIdsForChanges @Resources dbo.ResourceList READONLY, @ResourcesLake dbo.ResourceListLake READONLY
AS
set nocount on
-- This procedure is intended to be called from the MergeResources procedure and relies on its transaction logic
INSERT INTO dbo.ResourceChangeData 
       ( ResourceId, ResourceTypeId, ResourceVersion,                                              ResourceChangeTypeId )
  SELECT ResourceId, ResourceTypeId,         Version, CASE WHEN IsDeleted = 1 THEN 2 WHEN Version > 1 THEN 1 ELSE 0 END
    FROM (SELECT ResourceId, ResourceTypeId, Version, IsHistory, IsDeleted FROM @Resources UNION ALL SELECT ResourceId, ResourceTypeId, Version, IsHistory, IsDeleted FROM @ResourcesLake) A
    WHERE IsHistory = 0
')
  -- The following 2 procs and trigger are special for data movement
  EXECUTE('
CREATE PROCEDURE dbo.UpdateResourceSearchParams
    @FailedResources int = 0 OUT
   ,@Resources dbo.ResourceList READONLY -- TODO: Remove after deployment
   ,@ResourcesLake dbo.ResourceListLake READONLY
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
       ,@Mode varchar(200) = isnull((SELECT ''RT=[''+convert(varchar,min(ResourceTypeId))+'',''+convert(varchar,max(ResourceTypeId))+''] Sur=[''+convert(varchar,min(ResourceSurrogateId))+'',''+convert(varchar,max(ResourceSurrogateId))+''] V=''+convert(varchar,max(Version))+'' Rows=''+convert(varchar,count(*)) FROM (SELECT ResourceTypeId, ResourceSurrogateId, Version FROM @ResourcesLake UNION ALL SELECT ResourceTypeId, ResourceSurrogateId, Version FROM @Resources) A),''Input=Empty'')
       ,@ResourceRows int
       ,@InsertRows int
       ,@DeletedIdMap int
       ,@FirstIdInt bigint
       ,@CurrentRows int

RetryResourceIdIntMapLogic:
BEGIN TRY
  DECLARE @Ids TABLE (ResourceTypeId smallint NOT NULL, ResourceSurrogateId bigint NOT NULL)
  DECLARE @CurrentRefIdsRaw TABLE (ResourceTypeId smallint NOT NULL, ResourceIdInt bigint NOT NULL)
  DECLARE @CurrentRefIds TABLE (ResourceTypeId smallint NOT NULL, ResourceIdInt bigint NOT NULL PRIMARY KEY (ResourceTypeId, ResourceIdInt))
  DECLARE @InputRefIds AS TABLE (ResourceTypeId smallint NOT NULL, ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY (ResourceTypeId, ResourceId))
  DECLARE @ExistingRefIds AS TABLE (ResourceTypeId smallint NOT NULL, ResourceIdInt bigint NOT NULL, ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY (ResourceTypeId, ResourceId))
  DECLARE @InsertRefIds AS TABLE (ResourceTypeId smallint NOT NULL, IdIndex int NOT NULL, ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY (ResourceTypeId, ResourceId))
  DECLARE @InsertedRefIds AS TABLE (ResourceTypeId smallint NOT NULL, ResourceIdInt bigint NOT NULL, ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY (ResourceTypeId, ResourceId))
  DECLARE @ReferenceSearchParamsWithIds AS TABLE
  (
      ResourceTypeId           smallint NOT NULL
     ,ResourceSurrogateId      bigint   NOT NULL
     ,SearchParamId            smallint NOT NULL
     ,BaseUri                  varchar(128) COLLATE Latin1_General_100_CS_AS NULL
     ,ReferenceResourceTypeId  smallint NULL
     ,ReferenceResourceIdInt   bigint   NOT NULL
     ,ReferenceResourceVersion int      NULL

     UNIQUE (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceIdInt) 
  )
  
  -- Prepare insert into ResourceIdIntMap outside of transaction to minimize blocking
  INSERT INTO @InputRefIds SELECT DISTINCT ReferenceResourceTypeId, ReferenceResourceId FROM @ReferenceSearchParams WHERE ReferenceResourceTypeId IS NOT NULL

  INSERT INTO @ExistingRefIds
       (     ResourceTypeId, ResourceIdInt,   ResourceId )
    SELECT A.ResourceTypeId, ResourceIdInt, A.ResourceId
      FROM @InputRefIds A
           JOIN dbo.ResourceIdIntMap B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
    
  INSERT INTO @InsertRefIds 
         ( ResourceTypeId,                                                     IdIndex, ResourceId ) 
    SELECT ResourceTypeId, row_number() OVER (ORDER BY ResourceTypeId, ResourceId) - 1, ResourceId
      FROM @InputRefIds A
      WHERE NOT EXISTS (SELECT * FROM @ExistingRefIds B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId)

  SET @InsertRows = (SELECT count(*) FROM @InsertRefIds)
  IF @InsertRows > 0
  BEGIN
    EXECUTE dbo.AssignResourceIdInts @InsertRows, @FirstIdInt OUT

    INSERT INTO @InsertedRefIds
         (   ResourceTypeId,         ResourceIdInt, ResourceId ) 
      SELECT ResourceTypeId, IdIndex + @FirstIdInt, ResourceId
        FROM @InsertRefIds
  END

  INSERT INTO @ReferenceSearchParamsWithIds
         (   ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId,                  ReferenceResourceIdInt, ReferenceResourceVersion )
    SELECT A.ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, isnull(C.ResourceIdInt,B.ResourceIdInt), ReferenceResourceVersion
      FROM @ReferenceSearchParams A
           LEFT OUTER JOIN @InsertedRefIds B ON B.ResourceTypeId = A.ReferenceResourceTypeId AND B.ResourceId = A.ReferenceResourceId
           LEFT OUTER JOIN @ExistingRefIds C ON C.ResourceTypeId = A.ReferenceResourceTypeId AND C.ResourceId = A.ReferenceResourceId

  BEGIN TRANSACTION

  -- Update the search parameter hash value in the main resource table
  IF EXISTS (SELECT * FROM @ResourcesLake)
    UPDATE B
      SET SearchParamHash = (SELECT SearchParamHash FROM @ResourcesLake A WHERE A.ResourceTypeId = B.ResourceTypeId AND A.ResourceSurrogateId = B.ResourceSurrogateId)
      OUTPUT deleted.ResourceTypeId, deleted.ResourceSurrogateId INTO @Ids 
      FROM dbo.Resource B 
      WHERE EXISTS (SELECT * FROM @ResourcesLake A WHERE A.ResourceTypeId = B.ResourceTypeId AND A.ResourceSurrogateId = B.ResourceSurrogateId)
        AND B.IsHistory = 0
  ELSE
    UPDATE B
      SET SearchParamHash = (SELECT SearchParamHash FROM @Resources A WHERE A.ResourceTypeId = B.ResourceTypeId AND A.ResourceSurrogateId = B.ResourceSurrogateId)
      OUTPUT deleted.ResourceTypeId, deleted.ResourceSurrogateId INTO @Ids 
      FROM dbo.Resource B 
      WHERE EXISTS (SELECT * FROM @Resources A WHERE A.ResourceTypeId = B.ResourceTypeId AND A.ResourceSurrogateId = B.ResourceSurrogateId)
        AND B.IsHistory = 0
  SET @ResourceRows = @@rowcount

  -- First, delete all the search params of the resources to reindex.
  DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.ResourceWriteClaim B ON B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.ReferenceSearchParamTbl B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B 
    OUTPUT deleted.ReferenceResourceTypeId, deleted.ReferenceResourceIdInt INTO @CurrentRefIdsRaw
    FROM @Ids A INNER LOOP JOIN dbo.ResourceReferenceSearchParams B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.StringReferenceSearchParams B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.TokenSearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.TokenText B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.StringSearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.UriSearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.NumberSearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.QuantitySearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.DateTimeSearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.ReferenceTokenCompositeSearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.TokenTokenCompositeSearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.TokenDateTimeCompositeSearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.TokenQuantityCompositeSearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.TokenStringCompositeSearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.TokenNumberNumberCompositeSearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId

  -- Next, insert all the new search params.
  INSERT INTO dbo.ResourceWriteClaim 
         ( ResourceSurrogateId, ClaimTypeId, ClaimValue )
    SELECT ResourceSurrogateId, ClaimTypeId, ClaimValue
      FROM @ResourceWriteClaims
        
  -- start delete logic from ResourceIdIntMap
  INSERT INTO @CurrentRefIds SELECT DISTINCT ResourceTypeId, ResourceIdInt FROM @CurrentRefIdsRaw
  SET @CurrentRows = @@rowcount
  IF @CurrentRows > 0
  BEGIN
    -- remove not reused
    DELETE FROM A FROM @CurrentRefIds A WHERE EXISTS (SELECT * FROM @ReferenceSearchParamsWithIds B WHERE B.ReferenceResourceTypeId = A.ResourceTypeId AND B.ReferenceResourceIdInt = A.ResourceIdInt)
    SET @CurrentRows -= @@rowcount 
    IF @CurrentRows > 0
    BEGIN
      -- remove referenced by resources
      DELETE FROM A FROM @CurrentRefIds A WHERE EXISTS (SELECT * FROM dbo.CurrentResources B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceIdInt = A.ResourceIdInt)
      SET @CurrentRows -= @@rowcount
      IF @CurrentRows > 0
      BEGIN
        -- remove referenced by reference search params
        DELETE FROM A FROM @CurrentRefIds A WHERE EXISTS (SELECT * FROM dbo.ResourceReferenceSearchParams B WHERE B.ReferenceResourceTypeId = A.ResourceTypeId AND B.ReferenceResourceIdInt = A.ResourceIdInt)
        SET @CurrentRows -= @@rowcount
        IF @CurrentRows > 0
        BEGIN
          -- finally delete from id map
          DELETE FROM B FROM @CurrentRefIds A INNER LOOP JOIN dbo.ResourceIdIntMap B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceIdInt = A.ResourceIdInt
          SET @DeletedIdMap = @@rowcount
        END
      END
    END
  END

  INSERT INTO dbo.ResourceIdIntMap 
      (    ResourceTypeId, ResourceIdInt, ResourceId ) 
    SELECT ResourceTypeId, ResourceIdInt, ResourceId
      FROM @InsertedRefIds

  INSERT INTO dbo.ResourceReferenceSearchParams 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceIdInt )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceIdInt
      FROM @ReferenceSearchParamsWithIds

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

  SET @FailedResources = (SELECT count(*) FROM @Resources) + (SELECT count(*) FROM @ResourcesLake) - @ResourceRows

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status=''End'',@Start=@st,@Rows=@ResourceRows,@Text=@DeletedIdMap
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status=''Error'',@Start=@st

  IF error_number() IN (2601, 2627) AND error_message() LIKE ''%''''dbo.ResourceIdIntMap''''%'' -- pk violation
     OR error_number() = 547 AND error_message() LIKE ''%DELETE%'' -- reference violation on DELETE
  BEGIN
    DELETE FROM @Ids
    DELETE FROM @InputRefIds
    DELETE FROM @CurrentRefIdsRaw
    DELETE FROM @CurrentRefIds
    DELETE FROM @ExistingRefIds
    DELETE FROM @InsertRefIds
    DELETE FROM @InsertedRefIds
    DELETE FROM @ReferenceSearchParamsWithIds

    GOTO RetryResourceIdIntMapLogic
  END
  ELSE
    THROW
END CATCH
')
  
  EXECUTE('
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
       ,@CurrentRows int
       ,@DeletedIdMap int

DECLARE @Mode varchar(200) = isnull((SELECT ''RT=[''+convert(varchar,min(ResourceTypeId))+'',''+convert(varchar,max(ResourceTypeId))+''] Sur=[''+convert(varchar,min(ResourceSurrogateId))+'',''+convert(varchar,max(ResourceSurrogateId))+''] V=''+convert(varchar,max(Version))+'' Rows=''+convert(varchar,count(*)) FROM (SELECT ResourceTypeId, ResourceSurrogateId, Version FROM @Resources UNION ALL SELECT ResourceTypeId, ResourceSurrogateId, Version FROM @ResourcesLake) A),''Input=Empty'')
SET @Mode += '' E=''+convert(varchar,@RaiseExceptionOnConflict)+'' CC=''+convert(varchar,@IsResourceChangeCaptureEnabled)+'' IT=''+convert(varchar,@InitialTranCount)+'' T=''+isnull(convert(varchar,@TransactionId),''NULL'')

SET @AffectedRows = 0

RetryResourceIdIntMapLogic:
BEGIN TRY
  DECLARE @InputIds AS TABLE (ResourceTypeId smallint NOT NULL, ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY (ResourceTypeId, ResourceId))
  DECLARE @CurrentRefIdsRaw TABLE (ResourceTypeId smallint NOT NULL, ResourceIdInt bigint NOT NULL)
  DECLARE @CurrentRefIds TABLE (ResourceTypeId smallint NOT NULL, ResourceIdInt bigint NOT NULL PRIMARY KEY (ResourceTypeId, ResourceIdInt))
  DECLARE @ExistingIdsReference AS TABLE (ResourceTypeId smallint NOT NULL, ResourceIdInt bigint NOT NULL, ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY (ResourceTypeId, ResourceId))
  DECLARE @ExistingIdsResource AS TABLE (ResourceTypeId smallint NOT NULL, ResourceIdInt bigint NOT NULL, ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY (ResourceTypeId, ResourceId))
  DECLARE @InsertIds AS TABLE (ResourceTypeId smallint NOT NULL, IdIndex int NOT NULL, ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY (ResourceTypeId, ResourceId))
  DECLARE @InsertedIdsReference AS TABLE (ResourceTypeId smallint NOT NULL, ResourceIdInt bigint NOT NULL, ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY (ResourceTypeId, ResourceId))
  DECLARE @InsertedIdsResource AS TABLE (ResourceTypeId smallint NOT NULL, ResourceIdInt bigint NOT NULL, ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY (ResourceTypeId, ResourceId))
  DECLARE @ResourcesWithIds AS TABLE 
    (
        ResourceTypeId       smallint            NOT NULL
       ,ResourceSurrogateId  bigint              NOT NULL
       ,ResourceId           varchar(64)         COLLATE Latin1_General_100_CS_AS NOT NULL
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
  
  -- Prepare id map for reference search params Start ---------------------------------------------------------------------------
  INSERT INTO @InputIds SELECT DISTINCT ReferenceResourceTypeId, ReferenceResourceId FROM @ReferenceSearchParams WHERE ReferenceResourceTypeId IS NOT NULL

  INSERT INTO @ExistingIdsReference 
       (     ResourceTypeId, ResourceIdInt,   ResourceId )
    SELECT A.ResourceTypeId, ResourceIdInt, A.ResourceId
      FROM @InputIds A
           JOIN dbo.ResourceIdIntMap B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
    
  INSERT INTO @InsertIds 
         ( ResourceTypeId,                                                     IdIndex, ResourceId ) 
    SELECT ResourceTypeId, row_number() OVER (ORDER BY ResourceTypeId, ResourceId) - 1, ResourceId
      FROM @InputIds A
      WHERE NOT EXISTS (SELECT * FROM @ExistingIdsReference B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId)

  SET @NewIdsCount = (SELECT count(*) FROM @InsertIds)
  IF @NewIdsCount > 0
  BEGIN
    EXECUTE dbo.AssignResourceIdInts @NewIdsCount, @FirstIdInt OUT

    INSERT INTO @InsertedIdsReference 
         (   ResourceTypeId,         ResourceIdInt, ResourceId ) 
      SELECT ResourceTypeId, IdIndex + @FirstIdInt, ResourceId
        FROM @InsertIds
  END
  
  INSERT INTO @ReferenceSearchParamsWithIds
         (   ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId,                  ReferenceResourceIdInt )
    SELECT A.ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, isnull(C.ResourceIdInt,B.ResourceIdInt)
      FROM @ReferenceSearchParams A
           LEFT OUTER JOIN @InsertedIdsReference B ON B.ResourceTypeId = A.ReferenceResourceTypeId AND B.ResourceId = A.ReferenceResourceId
           LEFT OUTER JOIN @ExistingIdsReference C ON C.ResourceTypeId = A.ReferenceResourceTypeId AND C.ResourceId = A.ReferenceResourceId
      WHERE ReferenceResourceTypeId IS NOT NULL
  -- Prepare id map for reference search params End ---------------------------------------------------------------------------

  -- Prepare id map for resources Start ---------------------------------------------------------------------------
  DELETE FROM @InputIds
  IF EXISTS (SELECT * FROM @ResourcesLake)
    INSERT INTO @InputIds SELECT ResourceTypeId, ResourceId FROM @ResourcesLake GROUP BY ResourceTypeId, ResourceId
  ELSE
    INSERT INTO @InputIds SELECT ResourceTypeId, ResourceId FROM @Resources GROUP BY ResourceTypeId, ResourceId

  INSERT INTO @ExistingIdsResource 
       (     ResourceTypeId, ResourceIdInt,   ResourceId )
    SELECT A.ResourceTypeId, isnull(C.ResourceIdInt,B.ResourceIdInt), A.ResourceId
      FROM @InputIds A
           LEFT OUTER JOIN dbo.ResourceIdIntMap B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
           LEFT OUTER JOIN @InsertedIdsReference C ON C.ResourceTypeId = A.ResourceTypeId AND C.ResourceId = A.ResourceId
      WHERE C.ResourceIdInt IS NOT NULL OR B.ResourceIdInt IS NOT NULL

  DELETE FROM @InsertIds
  INSERT INTO @InsertIds 
         ( ResourceTypeId,                                                     IdIndex, ResourceId ) 
    SELECT ResourceTypeId, row_number() OVER (ORDER BY ResourceTypeId, ResourceId) - 1, ResourceId
      FROM @InputIds A
      WHERE NOT EXISTS (SELECT * FROM @ExistingIdsResource B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId)

  SET @NewIdsCount = (SELECT count(*) FROM @InsertIds)
  IF @NewIdsCount > 0
  BEGIN
    EXECUTE dbo.AssignResourceIdInts @NewIdsCount, @FirstIdInt OUT

    INSERT INTO @InsertedIdsResource 
         (   ResourceTypeId,         ResourceIdInt, ResourceId ) 
      SELECT ResourceTypeId, IdIndex + @FirstIdInt, ResourceId
        FROM @InsertIds
  END
  
  IF EXISTS (SELECT * FROM @ResourcesLake)
    INSERT INTO @ResourcesWithIds
           (   ResourceTypeId,   ResourceId,                           ResourceIdInt, Version, HasVersionToCompare, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, KeepHistory, RawResource, IsRawResourceMetaSet, SearchParamHash, FileId, OffsetInFile )
      SELECT A.ResourceTypeId, A.ResourceId, isnull(C.ResourceIdInt,B.ResourceIdInt), Version, HasVersionToCompare, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, KeepHistory, RawResource, IsRawResourceMetaSet, SearchParamHash, FileId, OffsetInFile
        FROM @ResourcesLake A
             LEFT OUTER JOIN @InsertedIdsResource B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
             LEFT OUTER JOIN @ExistingIdsResource C ON C.ResourceTypeId = A.ResourceTypeId AND C.ResourceId = A.ResourceId
  ELSE
    INSERT INTO @ResourcesWithIds
           (   ResourceTypeId,   ResourceId,                           ResourceIdInt, Version, HasVersionToCompare, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, KeepHistory, RawResource, IsRawResourceMetaSet, SearchParamHash, FileId, OffsetInFile )
      SELECT A.ResourceTypeId, A.ResourceId, isnull(C.ResourceIdInt,B.ResourceIdInt), Version, HasVersionToCompare, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, KeepHistory, RawResource, IsRawResourceMetaSet, SearchParamHash,   NULL,         NULL
        FROM @Resources A
             LEFT OUTER JOIN @InsertedIdsResource B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
             LEFT OUTER JOIN @ExistingIdsResource C ON C.ResourceTypeId = A.ResourceTypeId AND C.ResourceId = A.ResourceId
  -- Prepare id map for resources End ---------------------------------------------------------------------------

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

  IF @SingleTransaction = 0 AND isnull((SELECT Number FROM dbo.Parameters WHERE Id = ''MergeResources.NoTransaction.IsEnabled''),0) = 0
    SET @SingleTransaction = 1
  
  SET @Mode += '' ST=''+convert(varchar,@SingleTransaction)

  -- perform retry check in transaction to hold locks
  IF @InitialTranCount = 0
  BEGIN
    IF EXISTS (SELECT * -- This extra statement avoids putting range locks when we don''t need them
                 FROM @ResourcesWithIds A JOIN dbo.Resource B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
                 WHERE B.IsHistory = 0
              )
    BEGIN
      BEGIN TRANSACTION

      INSERT INTO @Existing
              (  ResourceTypeId,           SurrogateId )
        SELECT B.ResourceTypeId, B.ResourceSurrogateId
          FROM (SELECT TOP (@DummyTop) * FROM @ResourcesWithIds) A
               JOIN dbo.Resource B WITH (ROWLOCK, HOLDLOCK) ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
          WHERE B.IsHistory = 0
            AND B.ResourceId = A.ResourceId
            AND B.Version = A.Version
          OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
    
      IF @@rowcount = (SELECT count(*) FROM @ResourcesWithIds) SET @IsRetry = 1

      IF @IsRetry = 0 COMMIT TRANSACTION -- commit check transaction 
    END
  END

  SET @Mode += '' R=''+convert(varchar,@IsRetry)

  IF @SingleTransaction = 1 AND @@trancount = 0 BEGIN TRANSACTION
  
  IF @IsRetry = 0
  BEGIN
    INSERT INTO @ResourceInfos
            (  ResourceTypeId,           SurrogateId,   Version,   KeepHistory, PreviousVersion,   PreviousSurrogateId )
      SELECT A.ResourceTypeId, A.ResourceSurrogateId, A.Version, A.KeepHistory,       B.Version, B.ResourceSurrogateId
        FROM (SELECT TOP (@DummyTop) * FROM @ResourcesWithIds WHERE HasVersionToCompare = 1) A
             LEFT OUTER JOIN dbo.CurrentResource B -- WITH (UPDLOCK, HOLDLOCK) These locking hints cause deadlocks and are not needed. Racing might lead to tries to insert dups in unique index (with version key), but it will fail anyway, and in no case this will cause incorrect data saved.
               ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))

    IF @RaiseExceptionOnConflict = 1 AND EXISTS (SELECT * FROM @ResourceInfos WHERE PreviousVersion IS NOT NULL AND Version <= PreviousVersion)
      THROW 50409, ''Resource has been recently updated or added, please compare the resource content in code for any duplicate updates'', 1

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

      IF @IsResourceChangeCaptureEnabled = 1 AND NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = ''InvisibleHistory.IsEnabled'' AND Number = 0)
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
      DELETE FROM dbo.ReferenceSearchParamTbl WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
      SET @AffectedRows += @@rowcount
      DELETE FROM dbo.ResourceReferenceSearchParams
        OUTPUT deleted.ReferenceResourceTypeId, deleted.ReferenceResourceIdInt INTO @CurrentRefIdsRaw
        WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
      SET @CurrentRows = @@rowcount
      SET @AffectedRows += @CurrentRows
      -- start deleting from ResourceIdIntMap
      INSERT INTO @CurrentRefIds SELECT DISTINCT ResourceTypeId, ResourceIdInt FROM @CurrentRefIdsRaw
      SET @CurrentRows = @@rowcount
      IF @CurrentRows > 0
      BEGIN
        -- remove not reused
        DELETE FROM A FROM @CurrentRefIds A WHERE EXISTS (SELECT * FROM @ReferenceSearchParamsWithIds B WHERE B.ReferenceResourceTypeId = A.ResourceTypeId AND B.ReferenceResourceIdInt = A.ResourceIdInt)
        SET @CurrentRows -= @@rowcount 
        IF @CurrentRows > 0
        BEGIN
          -- remove referenced in Resources
          DELETE FROM A FROM @CurrentRefIds A WHERE EXISTS (SELECT * FROM dbo.CurrentResources B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceIdInt = A.ResourceIdInt)
          SET @CurrentRows -= @@rowcount
          IF @CurrentRows > 0
          BEGIN
            -- remove still referenced in ResourceReferenceSearchParams
            DELETE FROM A FROM @CurrentRefIds A WHERE EXISTS (SELECT * FROM dbo.ResourceReferenceSearchParams B WHERE B.ReferenceResourceTypeId = A.ResourceTypeId AND B.ReferenceResourceIdInt = A.ResourceIdInt)
            SET @CurrentRows -= @@rowcount
            IF @CurrentRows > 0
            BEGIN
              -- delete from id map
              DELETE FROM B FROM @CurrentRefIds A INNER LOOP JOIN dbo.ResourceIdIntMap B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceIdInt = A.ResourceIdInt
              SET @DeletedIdMap = @@rowcount
            END
          END
        END
      END
      DELETE FROM dbo.StringReferenceSearchParams WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
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

      --EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status=''Info'',@Start=@st,@Rows=@AffectedRows,@Text=''Old rows''
    END

    INSERT INTO dbo.ResourceIdIntMap 
        (    ResourceTypeId, ResourceIdInt, ResourceId ) 
      SELECT ResourceTypeId, ResourceIdInt, ResourceId
        FROM @InsertedIdsResource

    INSERT INTO dbo.ResourceIdIntMap 
        (    ResourceTypeId, ResourceIdInt, ResourceId ) 
      SELECT ResourceTypeId, ResourceIdInt, ResourceId
        FROM @InsertedIdsReference
    
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
    EXECUTE dbo.CaptureResourceIdsForChanges @Resources, @ResourcesLake

  IF @TransactionId IS NOT NULL
    EXECUTE dbo.MergeResourcesCommitTransaction @TransactionId

  IF @InitialTranCount = 0 AND @@trancount > 0 COMMIT TRANSACTION

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status=''End'',@Start=@st,@Rows=@AffectedRows,@Text=@DeletedIdMap
END TRY
BEGIN CATCH
  IF @InitialTranCount = 0 AND @@trancount > 0 ROLLBACK TRANSACTION
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status=''Error'',@Start=@st

  IF error_number() IN (2601, 2627) AND error_message() LIKE ''%''''dbo.ResourceIdIntMap''''%'' -- pk violation
     OR error_number() = 547 AND error_message() LIKE ''%DELETE%'' -- reference violation on DELETE
  BEGIN
    DELETE FROM @ResourcesWithIds
    DELETE FROM @ReferenceSearchParamsWithIds
    DELETE FROM @CurrentRefIdsRaw
    DELETE FROM @CurrentRefIds
    DELETE FROM @InputIds
    DELETE FROM @InsertIds
    DELETE FROM @InsertedIdsReference
    DELETE FROM @ExistingIdsReference
    DELETE FROM @InsertedIdsResource
    DELETE FROM @ExistingIdsResource
    DELETE FROM @Existing
    DELETE FROM @ResourceInfos
    DELETE FROM @PreviousSurrogateIds 

    GOTO RetryResourceIdIntMapLogic
  END
  ELSE 
    IF @RaiseExceptionOnConflict = 1 AND error_number() IN (2601, 2627) AND (error_message() LIKE ''%''''dbo.Resource%'' OR error_message() LIKE ''%''''dbo.CurrentResources%'' OR error_message() LIKE ''%''''dbo.HistoryResources%'' OR error_message() LIKE ''%''''dbo.RawResources''''%'')
      THROW 50409, ''Resource has been recently updated or added, please compare the resource content in code for any duplicate updates'', 1;
    ELSE
      THROW
END CATCH
')

  COMMIT TRANSACTION
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION
  THROW
END CATCH
GO
-- Special versions of procedures for data movement
GO
ALTER PROCEDURE dbo.GetResourcesByTypeAndSurrogateIdRange @ResourceTypeId smallint, @StartId bigint, @EndId bigint, @GlobalEndId bigint = NULL, @IncludeHistory bit = 1, @IncludeDeleted bit = 1
AS
set nocount on
DECLARE @SP varchar(100) = 'GetResourcesByTypeAndSurrogateIdRange'
       ,@Mode varchar(100) = 'RT='+isnull(convert(varchar,@ResourceTypeId),'NULL')
                           +' S='+isnull(convert(varchar,@StartId),'NULL')
                           +' E='+isnull(convert(varchar,@EndId),'NULL')
                           +' GE='+isnull(convert(varchar,@GlobalEndId),'NULL')
                           +' HI='+isnull(convert(varchar,@IncludeHistory),'NULL')
                           +' DE='+isnull(convert(varchar,@IncludeDeleted),'NULL')
       ,@st datetime = getUTCdate()
       ,@DummyTop bigint = 9223372036854775807
       ,@Rows int

BEGIN TRY
  DECLARE @ResourceIds TABLE (ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS PRIMARY KEY)
  DECLARE @SurrogateIds TABLE (MaxSurrogateId bigint PRIMARY KEY)

  IF @GlobalEndId IS NOT NULL AND @IncludeHistory = 0 -- snapshot view
  BEGIN
    INSERT INTO @ResourceIds
      SELECT DISTINCT ResourceId
        FROM dbo.Resource 
        WHERE ResourceTypeId = @ResourceTypeId 
          AND ResourceSurrogateId BETWEEN @StartId AND @EndId
          AND IsHistory = 1
          AND (IsDeleted = 0 OR @IncludeDeleted = 1)
        OPTION (MAXDOP 1)

    IF @@rowcount > 0
      INSERT INTO @SurrogateIds
        SELECT ResourceSurrogateId
          FROM (SELECT ResourceId, ResourceSurrogateId, RowId = row_number() OVER (PARTITION BY ResourceId ORDER BY ResourceSurrogateId DESC)
                  FROM dbo.Resource
                  WHERE ResourceTypeId = @ResourceTypeId
                    AND ResourceId IN (SELECT TOP (@DummyTop) ResourceId FROM @ResourceIds)
                    AND ResourceSurrogateId BETWEEN @StartId AND @GlobalEndId
               ) A
          WHERE RowId = 1
            AND ResourceSurrogateId BETWEEN @StartId AND @EndId
          OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
  END

  IF @IncludeHistory = 0
    SELECT ResourceTypeId, ResourceId, Version, IsDeleted, ResourceSurrogateId, RequestMethod, IsMatch = convert(bit,1), IsPartial = convert(bit,0), IsRawResourceMetaSet, SearchParamHash, RawResource, FileId, OffsetInFile
      FROM dbo.Resource
      WHERE ResourceTypeId = @ResourceTypeId 
        AND ResourceSurrogateId BETWEEN @StartId AND @EndId 
        AND IsHistory = 0
        AND (IsDeleted = 0 OR @IncludeDeleted = 1)
    UNION ALL
    SELECT ResourceTypeId, ResourceId, Version, IsDeleted, ResourceSurrogateId, RequestMethod, IsMatch = convert(bit,1), IsPartial = convert(bit,0), IsRawResourceMetaSet, SearchParamHash, RawResource, FileId, OffsetInFile
      FROM @SurrogateIds
           JOIN dbo.Resource ON ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId = MaxSurrogateId
      WHERE IsHistory = 1
        AND (IsDeleted = 0 OR @IncludeDeleted = 1)
    OPTION (MAXDOP 1, LOOP JOIN)
  ELSE -- @IncludeHistory = 1
    SELECT ResourceTypeId, ResourceId, Version, IsDeleted, ResourceSurrogateId, RequestMethod, IsMatch = convert(bit,1), IsPartial = convert(bit,0), IsRawResourceMetaSet, SearchParamHash, RawResource, FileId, OffsetInFile
      FROM dbo.Resource
      WHERE ResourceTypeId = @ResourceTypeId 
        AND ResourceSurrogateId BETWEEN @StartId AND @EndId 
        AND (IsDeleted = 0 OR @IncludeDeleted = 1)
    UNION ALL
    SELECT ResourceTypeId, ResourceId, Version, IsDeleted, ResourceSurrogateId, RequestMethod, IsMatch = convert(bit,1), IsPartial = convert(bit,0), IsRawResourceMetaSet, SearchParamHash, RawResource, FileId, OffsetInFile
      FROM @SurrogateIds
           JOIN dbo.Resource ON ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId = MaxSurrogateId
      WHERE IsHistory = 1
        AND (IsDeleted = 0 OR @IncludeDeleted = 1)
    OPTION (MAXDOP 1, LOOP JOIN)

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
ALTER PROCEDURE dbo.MergeResourcesDeleteInvisibleHistory @TransactionId bigint, @AffectedRows int = NULL OUT
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(100) = 'T='+convert(varchar,@TransactionId)
       ,@st datetime
       ,@Rows int
       ,@DeletedIdMap int
       ,@TypeId smallint

SET @AffectedRows = 0

DECLARE @Types TABLE (TypeId smallint PRIMARY KEY, Name varchar(100))
INSERT INTO @Types EXECUTE dbo.GetUsedResourceTypes

DECLARE @Ids TABLE (ResourceTypeId smallint NOT NULL, ResourceIdInt bigint NOT NULL)

Retry:
BEGIN TRY 
  WHILE EXISTS (SELECT * FROM @Types)
  BEGIN
    SET @TypeId = (SELECT TOP 1 TypeId FROM @Types ORDER BY TypeId)

    DELETE FROM dbo.ResourceTbl WHERE ResourceTypeId = @TypeId AND HistoryTransactionId = @TransactionId AND RawResource = 0xF
    SET @AffectedRows += @@rowcount

    DELETE FROM @Types WHERE TypeId = @TypeId
  END

  BEGIN TRANSACTION

  SET @st = getUTCdate()
  DELETE FROM A
    OUTPUT deleted.ResourceTypeId, deleted.ResourceIdInt INTO @Ids 
    FROM dbo.Resource A
    WHERE HistoryTransactionId = @TransactionId -- requires statistics to reflect not null values
  SET @Rows = @@rowcount
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Run',@Target='Resource',@Action='Delete',@Start=@st,@Rows=@Rows
  SET @AffectedRows += @Rows

  SET @st = getUTCdate()
  IF @Rows > 0
  BEGIN
    -- remove referenced in resources
    DELETE FROM A FROM @Ids A WHERE EXISTS (SELECT * FROM dbo.CurrentResources B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceIdInt = A.ResourceIdInt)
    SET @Rows -= @@rowcount
    IF @Rows > 0
    BEGIN
      -- remove referenced in reference search params
      DELETE FROM A FROM @Ids A WHERE EXISTS (SELECT * FROM dbo.ResourceReferenceSearchParams B WHERE B.ReferenceResourceTypeId = A.ResourceTypeId AND B.ReferenceResourceIdInt = A.ResourceIdInt)
      SET @Rows -= @@rowcount
      IF @Rows > 0
      BEGIN
        -- delete from id map
        DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.ResourceIdIntMap B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceIdInt = A.ResourceIdInt
        SET @DeletedIdMap = @@rowcount
      END
    END
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Run',@Target='ResourceIdIntMap',@Action='Delete',@Start=@st,@Rows=@DeletedIdMap
  END

  COMMIT TRANSACTION
  
  SET @st = getUTCdate()
  UPDATE dbo.Resource SET TransactionId = NULL WHERE TransactionId = @TransactionId
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Target='Resource',@Action='Update',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  IF @@trancount > 0 ROLLBACK TRANSACTION
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error'
  IF error_number() = 547 AND error_message() LIKE '%DELETE%' -- reference violation on DELETE
  BEGIN
    DELETE FROM @Ids
    GOTO Retry
  END
  ELSE
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
ALTER PROCEDURE dbo.HardDeleteResource
   @ResourceTypeId smallint
  ,@ResourceId varchar(64)
  ,@KeepCurrentVersion bit
  ,@IsResourceChangeCaptureEnabled bit = 0 -- TODO: Remove input parameter after deployment
  ,@MakeResourceInvisible bit = 0
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(200) = 'RT='+convert(varchar,@ResourceTypeId)+' R='+@ResourceId+' V='+convert(varchar,@KeepCurrentVersion)
       ,@st datetime = getUTCdate()
       ,@TransactionId bigint
       ,@DeletedIdMap int = 0
       ,@Rows int

IF @IsResourceChangeCaptureEnabled = 1
  SET @MakeResourceInvisible = 1

SET @Mode += ' I='+convert(varchar,@MakeResourceInvisible)

IF @MakeResourceInvisible = 1
BEGIN 
  EXECUTE dbo.MergeResourcesBeginTransaction @Count = 1, @TransactionId = @TransactionId OUT
  SET @Mode += ' T='+convert(varchar,@TransactionId)
END

DECLARE @Ids TABLE (ResourceSurrogateId bigint NOT NULL, ResourceIdInt bigint NULL)
DECLARE @IdsDistinct TABLE (ResourceTypeId smallint NOT NULL, ResourceIdInt bigint NOT NULL PRIMARY KEY (ResourceTypeId, ResourceIdInt))
DECLARE @RefIdsRaw TABLE (ResourceTypeId smallint NOT NULL, ResourceIdInt bigint NOT NULL)

RetryResourceIdIntMapLogic:
BEGIN TRY
  BEGIN TRANSACTION

  IF @MakeResourceInvisible = 1
    UPDATE dbo.Resource
      SET IsDeleted = 1
         ,RawResource = 0xF -- invisible value
         ,SearchParamHash = NULL
         ,HistoryTransactionId = @TransactionId
      OUTPUT deleted.ResourceSurrogateId, deleted.ResourceIdInt INTO @Ids
      WHERE ResourceTypeId = @ResourceTypeId
        AND ResourceId = @ResourceId
        AND (@KeepCurrentVersion = 0 OR IsHistory = 1)
        AND (RawResource IS NULL -- stored in ADLS
             OR RawResource <> 0xF -- stored in the database and not already invisible
            )
  ELSE
  BEGIN
    DELETE dbo.Resource
      OUTPUT deleted.ResourceSurrogateId, deleted.ResourceIdInt INTO @Ids
      WHERE ResourceTypeId = @ResourceTypeId
        AND ResourceId = @ResourceId
        AND (@KeepCurrentVersion = 0 OR IsHistory = 1)
        AND RawResource <> 0xF

    INSERT INTO @IdsDistinct SELECT DISTINCT @ResourceTypeId, ResourceIdInt FROM @Ids WHERE ResourceIdInt IS NOT NULL
    SET @Rows = @@rowcount
    IF @Rows > 0
    BEGIN
      DELETE FROM A FROM @IdsDistinct A WHERE EXISTS (SELECT * FROM dbo.CurrentResources B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceIdInt = A.ResourceIdInt)
      SET @Rows -= @@rowcount
      IF @Rows > 0
      BEGIN
        DELETE FROM A FROM @IdsDistinct A WHERE EXISTS (SELECT * FROM dbo.ResourceReferenceSearchParams B WHERE B.ReferenceResourceTypeId = A.ResourceTypeId AND B.ReferenceResourceIdInt = A.ResourceIdInt)
        SET @Rows -= @@rowcount
        IF @Rows > 0
        BEGIN
          DELETE FROM B FROM @IdsDistinct A INNER LOOP JOIN dbo.ResourceIdIntMap B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceIdInt = A.ResourceIdInt
          SET @DeletedIdMap = @@rowcount
        END
      END
    END
  END

  IF @KeepCurrentVersion = 0
  BEGIN
    -- PAGLOCK allows deallocation of empty page without waiting for ghost cleanup 
    DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.ResourceWriteClaim B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.ReferenceSearchParamTbl B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B 
      OUTPUT deleted.ReferenceResourceTypeId, deleted.ReferenceResourceIdInt INTO @RefIdsRaw
      FROM @Ids A INNER LOOP JOIN dbo.ResourceReferenceSearchParams B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM @IdsDistinct -- is used above
    INSERT INTO @IdsDistinct SELECT DISTINCT ResourceTypeId, ResourceIdInt FROM @RefIdsRaw
    SET @Rows = @@rowcount
    IF @Rows > 0
    BEGIN
      DELETE FROM A FROM @IdsDistinct A WHERE EXISTS (SELECT * FROM dbo.CurrentResources B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceIdInt = A.ResourceIdInt)
      SET @Rows -= @@rowcount
      IF @Rows > 0
      BEGIN
        DELETE FROM A FROM @IdsDistinct A WHERE EXISTS (SELECT * FROM dbo.ResourceReferenceSearchParams B WHERE B.ReferenceResourceTypeId = A.ResourceTypeId AND B.ReferenceResourceIdInt = A.ResourceIdInt)
        SET @Rows -= @@rowcount
        IF @Rows > 0
        BEGIN
          DELETE FROM B FROM @IdsDistinct A INNER LOOP JOIN dbo.ResourceIdIntMap B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceIdInt = A.ResourceIdInt
          SET @DeletedIdMap += @@rowcount
        END
      END
    END
    DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.StringReferenceSearchParams B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.TokenSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.TokenText B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.StringSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.UriSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.NumberSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.QuantitySearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.DateTimeSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.ReferenceTokenCompositeSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.TokenTokenCompositeSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.TokenDateTimeCompositeSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.TokenQuantityCompositeSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.TokenStringCompositeSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.TokenNumberNumberCompositeSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
  END
  
  COMMIT TRANSACTION

  IF @MakeResourceInvisible = 1
    EXECUTE dbo.MergeResourcesCommitTransaction @TransactionId

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Text=@DeletedIdMap
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st
  
  IF error_number() = 547 AND error_message() LIKE '%DELETE%'-- reference violation on DELETE
  BEGIN
    DELETE FROM @Ids
    DELETE FROM @RefIdsRaw
    DELETE FROM @IdsDistinct
    GOTO RetryResourceIdIntMapLogic
  END
  ELSE
    THROW
END CATCH
GO
CREATE OR ALTER PROCEDURE dbo.tmp_MoveResources @ResourceTypeId smallint, @SurrogateId bigint, @CurrentMaxSurrogateId bigint, @LastProcessed varchar(100)
AS
set nocount on
DECLARE @Process varchar(100) = 'LakeSchemaUpgrade.MoveResources'
       ,@Id varchar(100) = 'LakeSchemaUpgrade.MoveResources.LastProcessed.TypeId.SurrogateId' -- SELECT * FROM Parameters
       ,@st datetime
       ,@NewIdsCount int
       ,@FirstIdInt bigint
       ,@DummyTop bigint = 9223372036854775807
       
RetryResourceIdIntMapLogic:
BEGIN TRY
  DECLARE @InputIds AS TABLE (ResourceTypeId smallint NOT NULL, ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY (ResourceTypeId, ResourceId))
  DECLARE @ExistingIdsReference AS TABLE (ResourceTypeId smallint NOT NULL, ResourceIdInt bigint NOT NULL, ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY (ResourceTypeId, ResourceId))
  DECLARE @ExistingIdsResource AS TABLE (ResourceTypeId smallint NOT NULL, ResourceIdInt bigint NOT NULL, ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY (ResourceTypeId, ResourceId))
  DECLARE @InsertIds AS TABLE (ResourceTypeId smallint NOT NULL, IdIndex int NOT NULL, ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY (ResourceTypeId, ResourceId))
  DECLARE @InsertedIdsReference AS TABLE (ResourceTypeId smallint NOT NULL, ResourceIdInt bigint NOT NULL, ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY (ResourceTypeId, ResourceId))
  DECLARE @InsertedIdsResource AS TABLE (ResourceTypeId smallint NOT NULL, ResourceIdInt bigint NOT NULL, ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY (ResourceTypeId, ResourceId))
  DECLARE @ResourcesWithIds AS TABLE 
    (
        ResourceTypeId       smallint            NOT NULL
       ,ResourceSurrogateId  bigint              NOT NULL
       ,ResourceId           varchar(64)         COLLATE Latin1_General_100_CS_AS NOT NULL
       ,ResourceIdInt        bigint              NOT NULL
       ,Version              int                 NOT NULL
       ,IsDeleted            bit                 NOT NULL
       ,IsHistory            bit                 NOT NULL
       ,RawResource          varbinary(max)      NULL
       ,IsRawResourceMetaSet bit                 NOT NULL
       ,RequestMethod        varchar(10)         NULL
       ,SearchParamHash      varchar(64)         NULL
       ,TransactionId        bigint              NULL
       ,HistoryTransactionId bigint              NULL

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
  
  -- reference search params Start ---------------------------------------------------------------------------
  SET @st = getUTCdate()
  INSERT INTO @InputIds 
    SELECT DISTINCT ReferenceResourceTypeId, ReferenceResourceId 
      FROM dbo.ReferenceSearchParamTbl 
      WHERE ResourceTypeId = @ResourceTypeId 
        AND ResourceSurrogateId > @SurrogateId AND ResourceSurrogateId <= @CurrentMaxSurrogateId 
        AND ReferenceResourceTypeId IS NOT NULL
  EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Mode=@LastProcessed,@Target='ReferenceSearchParamTbl.@InputIds',@Action='Insert',@Rows=@@rowcount,@Start=@st

  SET @st = getUTCdate()
  INSERT INTO @ExistingIdsReference 
       (     ResourceTypeId, ResourceIdInt,   ResourceId )
    SELECT A.ResourceTypeId, ResourceIdInt, A.ResourceId
      FROM @InputIds A
           JOIN dbo.ResourceIdIntMap B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
    
  INSERT INTO @InsertIds 
         ( ResourceTypeId,                                                     IdIndex, ResourceId ) 
    SELECT ResourceTypeId, row_number() OVER (ORDER BY ResourceTypeId, ResourceId) - 1, ResourceId
      FROM @InputIds A
      WHERE NOT EXISTS (SELECT * FROM @ExistingIdsReference B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId)

  SET @NewIdsCount = (SELECT count(*) FROM @InsertIds)
  IF @NewIdsCount > 0
  BEGIN
    EXECUTE dbo.AssignResourceIdInts @NewIdsCount, @FirstIdInt OUT

    INSERT INTO @InsertedIdsReference 
         (   ResourceTypeId,         ResourceIdInt, ResourceId ) 
      SELECT ResourceTypeId, IdIndex + @FirstIdInt, ResourceId
        FROM @InsertIds
  END
  
  INSERT INTO @ReferenceSearchParamsWithIds
         (   ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId,                  ReferenceResourceIdInt )
    SELECT A.ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, isnull(C.ResourceIdInt,B.ResourceIdInt)
      FROM (SELECT TOP (@DummyTop) * 
              FROM dbo.ReferenceSearchParamTbl 
              WHERE ResourceTypeId = @ResourceTypeId 
                AND ResourceSurrogateId > @SurrogateId AND ResourceSurrogateId <= @CurrentMaxSurrogateId 
                AND ReferenceResourceTypeId IS NOT NULL
           ) A
           LEFT OUTER JOIN @InsertedIdsReference B ON B.ResourceTypeId = A.ReferenceResourceTypeId AND B.ResourceId = A.ReferenceResourceId
           LEFT OUTER JOIN @ExistingIdsReference C ON C.ResourceTypeId = A.ReferenceResourceTypeId AND C.ResourceId = A.ReferenceResourceId
      OPTION (OPTIMIZE FOR (@DummyTop = 1))
  EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Mode=@LastProcessed,@Target='@ReferenceSearchParamsWithIds',@Action='Insert',@Rows=@@rowcount,@Start=@st

  BEGIN TRANSACTION

  SET @st = getUTCdate()
  INSERT INTO dbo.ResourceIdIntMap 
      (    ResourceTypeId, ResourceIdInt, ResourceId ) 
    SELECT ResourceTypeId, ResourceIdInt, ResourceId
      FROM @InsertedIdsReference
  EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Mode=@LastProcessed,@Target='Reference.ResourceIdIntMap',@Action='Insert',@Rows=@@rowcount,@Start=@st
    
  SET @st = getUTCdate()
  INSERT INTO dbo.ResourceReferenceSearchParams 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceIdInt )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceIdInt
      FROM @ReferenceSearchParamsWithIds
  EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Mode=@LastProcessed,@Target='ResourceReferenceSearchParams',@Action='Insert',@Rows=@@rowcount,@Start=@st

  SET @st = getUTCdate()
  INSERT INTO dbo.StringReferenceSearchParams 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceId )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceId
      FROM dbo.ReferenceSearchParamTbl
      WHERE ResourceTypeId = @ResourceTypeId 
        AND ResourceSurrogateId > @SurrogateId AND ResourceSurrogateId <= @CurrentMaxSurrogateId 
        AND ReferenceResourceTypeId IS NULL
  EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Mode=@LastProcessed,@Target='StringReferenceSearchParams',@Action='Insert',@Rows=@@rowcount,@Start=@st

  SET @st = getUTCdate()
  DELETE FROM dbo.ReferenceSearchParamTbl
    WHERE ResourceTypeId = @ResourceTypeId 
      AND ResourceSurrogateId > @SurrogateId AND ResourceSurrogateId <= @CurrentMaxSurrogateId 
  EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Mode=@LastProcessed,@Target='ReferenceSearchParamTbl',@Action='Delete',@Rows=@@rowcount,@Start=@st

  COMMIT TRANSACTION
  -- reference search params End ---------------------------------------------------------------------------

  -- resources Start ---------------------------------------------------------------------------
  SET @st = getUTCdate()
  DELETE FROM @InputIds
  INSERT INTO @InputIds 
    SELECT @ResourceTypeId, ResourceId 
      FROM (SELECT ResourceId 
              FROM dbo.ResourceTbl WITH (INDEX = 1)
              WHERE ResourceTypeId = @ResourceTypeId 
                AND ResourceSurrogateId > @SurrogateId AND ResourceSurrogateId <= @CurrentMaxSurrogateId 
           ) A
      GROUP BY ResourceId
      OPTION (MAXDOP 1)
  EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Mode=@LastProcessed,@Target='ResourceTbl.@InputIds',@Action='Insert',@Rows=@@rowcount,@Start=@st

  SET @st = getUTCdate()
  INSERT INTO @ExistingIdsResource 
       (     ResourceTypeId, ResourceIdInt,   ResourceId )
    SELECT A.ResourceTypeId, isnull(C.ResourceIdInt,B.ResourceIdInt), A.ResourceId
      FROM @InputIds A
           LEFT OUTER JOIN dbo.ResourceIdIntMap B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
           LEFT OUTER JOIN @InsertedIdsReference C ON C.ResourceTypeId = A.ResourceTypeId AND C.ResourceId = A.ResourceId
      WHERE C.ResourceIdInt IS NOT NULL OR B.ResourceIdInt IS NOT NULL

  DELETE FROM @InsertIds
  INSERT INTO @InsertIds 
         ( ResourceTypeId,                                                     IdIndex, ResourceId ) 
    SELECT ResourceTypeId, row_number() OVER (ORDER BY ResourceTypeId, ResourceId) - 1, ResourceId
      FROM @InputIds A
      WHERE NOT EXISTS (SELECT * FROM @ExistingIdsResource B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId)

  SET @NewIdsCount = (SELECT count(*) FROM @InsertIds)
  IF @NewIdsCount > 0
  BEGIN
    EXECUTE dbo.AssignResourceIdInts @NewIdsCount, @FirstIdInt OUT

    INSERT INTO @InsertedIdsResource 
         (   ResourceTypeId,         ResourceIdInt, ResourceId ) 
      SELECT ResourceTypeId, IdIndex + @FirstIdInt, ResourceId
        FROM @InsertIds
  END
  
  INSERT INTO @ResourcesWithIds
         (   ResourceTypeId,   ResourceId,                           ResourceIdInt, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash, TransactionId, HistoryTransactionId )
    SELECT A.ResourceTypeId, A.ResourceId, isnull(C.ResourceIdInt,B.ResourceIdInt), Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash, TransactionId, HistoryTransactionId
      FROM dbo.ResourceTbl A 
           LEFT OUTER JOIN @InsertedIdsResource B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
           LEFT OUTER JOIN @ExistingIdsResource C ON C.ResourceTypeId = A.ResourceTypeId AND C.ResourceId = A.ResourceId
      WHERE A.ResourceTypeId = @ResourceTypeId 
        AND A.ResourceSurrogateId > @SurrogateId AND ResourceSurrogateId <= @CurrentMaxSurrogateId
  EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Mode=@LastProcessed,@Target='@ResourcesWithIds',@Action='Insert',@Rows=@@rowcount,@Start=@st

  BEGIN TRANSACTION 

  SET @st = getUTCdate()
  DELETE FROM dbo.ResourceTbl
    WHERE ResourceTypeId = @ResourceTypeId 
      AND ResourceSurrogateId > @SurrogateId AND ResourceSurrogateId <= @CurrentMaxSurrogateId
  EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Mode=@LastProcessed,@Target='ResourceTbl',@Action='Delete',@Rows=@@rowcount,@Start=@st

  SET @st = getUTCdate()
  INSERT INTO dbo.ResourceIdIntMap 
      (    ResourceTypeId, ResourceIdInt, ResourceId ) 
    SELECT ResourceTypeId, ResourceIdInt, ResourceId
      FROM @InsertedIdsResource
  EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Mode=@LastProcessed,@Target='Resource.ResourceIdIntMap',@Action='Insert',@Rows=@@rowcount,@Start=@st

  SET @st = getUTCdate()
  INSERT INTO dbo.Resource 
         ( ResourceTypeId, ResourceIdInt, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash, TransactionId, HistoryTransactionId )
    SELECT ResourceTypeId, ResourceIdInt, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash, TransactionId, HistoryTransactionId
      FROM @ResourcesWithIds
  EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Mode=@LastProcessed,@Target='Resource',@Action='Insert',@Rows=@@rowcount,@Start=@st

  UPDATE dbo.Parameters SET Char = @LastProcessed WHERE Id = @Id

  COMMIT TRANSACTION
  -- resources End ---------------------------------------------------------------------------
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION

  EXECUTE dbo.LogEvent @Process=@Process,@Mode=@LastProcessed,@Status='Error',@Start=@st

  IF error_number() IN (2601, 2627) AND error_message() LIKE '%''dbo.ResourceIdIntMap''%' -- pk violation
     OR error_number() = 547 AND error_message() LIKE '%DELETE%' -- reference violation on DELETE
  BEGIN
    DELETE FROM @ResourcesWithIds
    DELETE FROM @ReferenceSearchParamsWithIds
    DELETE FROM @InputIds
    DELETE FROM @InsertIds
    DELETE FROM @InsertedIdsReference
    DELETE FROM @ExistingIdsReference
    DELETE FROM @InsertedIdsResource
    DELETE FROM @ExistingIdsResource

    GOTO RetryResourceIdIntMapLogic
  END
  ELSE
    THROW
END CATCH
GO
-- Move data
-- ROLLBACK TRANSACTION
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
       ,@Rows int
       ,@CurrentMaxSurrogateId bigint
       ,@LastProcessed varchar(100)
       ,@st datetime

INSERT INTO dbo.Parameters (Id, Char) SELECT @Process, 'LogEvent'

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
      SET @CurrentMaxSurrogateId = NULL
      SELECT @CurrentMaxSurrogateId = max(ResourceSurrogateId), @RowsToProcess = count(*)
        FROM (SELECT TOP 5000 ResourceSurrogateId
                FROM dbo.ResourceTbl
                WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId > @SurrogateId AND ResourceSurrogateId <= @MaxSurrogateId 
                ORDER BY ResourceSurrogateId
             ) A

      IF @CurrentMaxSurrogateId IS NOT NULL
      BEGIN
        SET @LastProcessed = convert(varchar,@ResourceTypeId)+'.'+convert(varchar,@CurrentMaxSurrogateId)

        EXECUTE tmp_MoveResources @ResourceTypeId = @ResourceTypeId, @SurrogateId = @SurrogateId, @CurrentMaxSurrogateId = @CurrentMaxSurrogateId, @LastProcessed = @LastProcessed

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

  IF 0 < (SELECT sum(row_count) FROM sys.dm_db_partition_stats WHERE object_Id = object_id('ResourceTbl') AND index_id IN (0,1))
    RAISERROR('ResourceTbl is not empty', 18, 127)
  
  IF 0 < (SELECT sum(row_count) FROM sys.dm_db_partition_stats WHERE object_Id = object_id('ReferenceSearchParamTbl') AND index_id IN (0,1))
    RAISERROR('ReferenceSearchParamTbl is not empty', 18, 127)

  EXECUTE('
ALTER VIEW dbo.ReferenceSearchParam
AS
SELECT A.ResourceTypeId
      ,ResourceSurrogateId
      ,SearchParamId
      ,BaseUri
      ,ReferenceResourceTypeId
      ,ReferenceResourceId = B.ResourceId
      ,ReferenceResourceIdInt
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
      ,NULL
      ,IsResourceRef
  FROM dbo.StringReferenceSearchParams
  ')
  EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Target='ReferenceSearchParam',@Action='Alter'

  EXECUTE('
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
      ,RawResource
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
      ,RawResource
      ,IsRawResourceMetaSet
      ,SearchParamHash
      ,TransactionId 
      ,HistoryTransactionId
      ,FileId
      ,OffsetInFile
  FROM dbo.HistoryResources A
       LEFT OUTER JOIN dbo.RawResources B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
       LEFT OUTER JOIN dbo.ResourceIdIntMap C ON C.ResourceTypeId = A.ResourceTypeId AND C.ResourceIdInt = A.ResourceIdInt
  ')
  EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Target='Resource',@Action='Alter'

  EXECUTE('
ALTER VIEW dbo.CurrentResource
AS 
SELECT A.ResourceTypeId
      ,A.ResourceSurrogateId
      ,ResourceId
      ,A.ResourceIdInt
      ,Version
      ,IsHistory
      ,IsDeleted
      ,RequestMethod
      ,RawResource
      ,IsRawResourceMetaSet
      ,SearchParamHash
      ,TransactionId 
      ,HistoryTransactionId
      ,FileId
      ,OffsetInFile
  FROM dbo.CurrentResources A
       LEFT OUTER JOIN dbo.RawResources B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
       LEFT OUTER JOIN dbo.ResourceIdIntMap C ON C.ResourceTypeId = A.ResourceTypeId AND C.ResourceIdInt = A.ResourceIdInt
  ')
  EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Target='CurrentResource',@Action='Alter'

  EXECUTE('
ALTER TRIGGER dbo.ResourceIns ON dbo.Resource INSTEAD OF INSERT
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
  ')
  EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Target='ResourceIns',@Action='Alter'
  
  EXECUTE('
ALTER TRIGGER dbo.ResourceUpd ON dbo.Resource INSTEAD OF UPDATE
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
    RAISERROR(''Generic updates are not supported via Resource view'',18,127)

  DELETE FROM A
    FROM dbo.CurrentResources A
    WHERE EXISTS (SELECT * FROM Inserted B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId AND B.IsHistory = 1)

  INSERT INTO dbo.HistoryResources
         ( ResourceTypeId, ResourceSurrogateId, ResourceIdInt, Version, IsDeleted, RequestMethod, IsRawResourceMetaSet, SearchParamHash, TransactionId, HistoryTransactionId, FileId, OffsetInFile )
    SELECT ResourceTypeId, ResourceSurrogateId, ResourceIdInt, Version, IsDeleted, RequestMethod, IsRawResourceMetaSet, SearchParamHash, TransactionId, HistoryTransactionId, FileId, OffsetInFile
      FROM Inserted
      WHERE IsHistory = 1
END
  ')
  EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Target='ResourceUpd',@Action='Alter'

  EXECUTE('
ALTER TRIGGER dbo.ResourceDel ON dbo.Resource INSTEAD OF DELETE
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
  ')
  EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Target='ResourceDel',@Action='Alter'

  EXECUTE('
ALTER PROCEDURE dbo.GetResourceVersions @ResourceDateKeys dbo.ResourceDateKeyList READONLY
AS
-- This stored procedure allows to identifiy if version gap is available and checks dups on lastUpdated
set nocount on
DECLARE @st datetime = getUTCdate()
       ,@SP varchar(100) = ''GetResourceVersions''
       ,@Mode varchar(100) = ''Rows=''+convert(varchar,(SELECT count(*) FROM @ResourceDateKeys))
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

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status=''End'',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status=''Error'',@Start=@st;
  THROW
END CATCH
  ')
  EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Target='GetResourceVersions',@Action='Alter'

  EXECUTE('
ALTER PROCEDURE dbo.GetResources @ResourceKeys dbo.ResourceKeyList READONLY
AS
set nocount on
DECLARE @st datetime = getUTCdate()
       ,@SP varchar(100) = ''GetResources''
       ,@InputRows int
       ,@NotNullVersionExists bit 
       ,@NullVersionExists bit
       ,@MinRT smallint
       ,@MaxRT smallint

SELECT @MinRT = min(ResourceTypeId), @MaxRT = max(ResourceTypeId), @InputRows = count(*), @NotNullVersionExists = max(CASE WHEN Version IS NOT NULL THEN 1 ELSE 0 END), @NullVersionExists = max(CASE WHEN Version IS NULL THEN 1 ELSE 0 END) FROM @ResourceKeys

DECLARE @Mode varchar(100) = ''RT=[''+convert(varchar,@MinRT)+'',''+convert(varchar,@MaxRT)+''] Cnt=''+convert(varchar,@InputRows)+'' NNVE=''+convert(varchar,@NotNullVersionExists)+'' NVE=''+convert(varchar,@NullVersionExists)

BEGIN TRY
  IF @NotNullVersionExists = 1
    IF @NullVersionExists = 0
      SELECT B.ResourceTypeId
            ,B.ResourceId
            ,ResourceSurrogateId
            ,C.Version
            ,IsDeleted
            ,IsHistory
            ,RawResource
            ,IsRawResourceMetaSet
            ,SearchParamHash
            ,FileId
            ,OffsetInFile
        FROM (SELECT * FROM @ResourceKeys) A
             INNER LOOP JOIN dbo.ResourceIdIntMap B WITH (INDEX = U_ResourceIdIntMap_ResourceId_ResourceTypeId) ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
             INNER LOOP JOIN dbo.Resource C ON C.ResourceTypeId = A.ResourceTypeId AND C.ResourceIdInt = B.ResourceIdInt AND C.Version = A.Version
        OPTION (MAXDOP 1)
    ELSE
      SELECT *
        FROM (SELECT B.ResourceTypeId
                    ,B.ResourceId
                    ,ResourceSurrogateId
                    ,C.Version
                    ,IsDeleted
                    ,IsHistory
                    ,RawResource
                    ,IsRawResourceMetaSet
                    ,SearchParamHash
                    ,FileId
                    ,OffsetInFile
                FROM (SELECT * FROM @ResourceKeys WHERE Version IS NOT NULL) A
                     INNER LOOP JOIN dbo.ResourceIdIntMap B WITH (INDEX = U_ResourceIdIntMap_ResourceId_ResourceTypeId) ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
                     INNER LOOP JOIN dbo.Resource C ON C.ResourceTypeId = A.ResourceTypeId AND C.ResourceIdInt = B.ResourceIdInt AND C.Version = A.Version
              UNION ALL
              SELECT B.ResourceTypeId
                    ,B.ResourceId
                    ,C.ResourceSurrogateId
                    ,C.Version
                    ,IsDeleted
                    ,IsHistory
                    ,RawResource
                    ,IsRawResourceMetaSet
                    ,SearchParamHash
                    ,FileId
                    ,OffsetInFile
                FROM (SELECT * FROM @ResourceKeys WHERE Version IS NULL) A
                     INNER LOOP JOIN dbo.ResourceIdIntMap B WITH (INDEX = U_ResourceIdIntMap_ResourceId_ResourceTypeId) ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
                     INNER LOOP JOIN dbo.CurrentResources C ON C.ResourceTypeId = A.ResourceTypeId AND C.ResourceIdInt = B.ResourceIdInt AND C.IsHistory = 0
                     LEFT OUTER JOIN dbo.RawResources D ON D.ResourceTypeId = A.ResourceTypeId AND D.ResourceSurrogateId = C.ResourceSurrogateId
             ) A
        OPTION (MAXDOP 1)
  ELSE
    SELECT B.ResourceTypeId
          ,B.ResourceId
          ,C.ResourceSurrogateId
          ,C.Version
          ,IsDeleted
          ,IsHistory
          ,RawResource
          ,IsRawResourceMetaSet
          ,SearchParamHash
          ,FileId
          ,OffsetInFile
      FROM (SELECT * FROM @ResourceKeys) A
           INNER LOOP JOIN dbo.ResourceIdIntMap B WITH (INDEX = U_ResourceIdIntMap_ResourceId_ResourceTypeId) ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
           INNER LOOP JOIN dbo.CurrentResources C ON C.ResourceTypeId = A.ResourceTypeId AND C.ResourceIdInt = B.ResourceIdInt
           LEFT OUTER JOIN dbo.RawResources D ON D.ResourceTypeId = A.ResourceTypeId AND D.ResourceSurrogateId = C.ResourceSurrogateId
      OPTION (MAXDOP 1)

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status=''End'',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status=''Error'',@Start=@st;
  THROW
END CATCH
  ')
  EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Target='GetResources',@Action='Alter'

  EXECUTE('
ALTER PROCEDURE dbo.UpdateResourceSearchParams
    @FailedResources int = 0 OUT
   ,@Resources dbo.ResourceList READONLY -- TODO: Remove after deployment
   ,@ResourcesLake dbo.ResourceListLake READONLY
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
       ,@Mode varchar(200) = isnull((SELECT ''RT=[''+convert(varchar,min(ResourceTypeId))+'',''+convert(varchar,max(ResourceTypeId))+''] Sur=[''+convert(varchar,min(ResourceSurrogateId))+'',''+convert(varchar,max(ResourceSurrogateId))+''] V=''+convert(varchar,max(Version))+'' Rows=''+convert(varchar,count(*)) FROM (SELECT ResourceTypeId, ResourceSurrogateId, Version FROM @ResourcesLake UNION ALL SELECT ResourceTypeId, ResourceSurrogateId, Version FROM @Resources) A),''Input=Empty'')
       ,@ResourceRows int
       ,@InsertRows int
       ,@DeletedIdMap int
       ,@FirstIdInt bigint
       ,@CurrentRows int

RetryResourceIdIntMapLogic:
BEGIN TRY
  DECLARE @Ids TABLE (ResourceTypeId smallint NOT NULL, ResourceSurrogateId bigint NOT NULL)
  DECLARE @CurrentRefIdsRaw TABLE (ResourceTypeId smallint NOT NULL, ResourceIdInt bigint NOT NULL)
  DECLARE @CurrentRefIds TABLE (ResourceTypeId smallint NOT NULL, ResourceIdInt bigint NOT NULL PRIMARY KEY (ResourceTypeId, ResourceIdInt))
  DECLARE @InputRefIds AS TABLE (ResourceTypeId smallint NOT NULL, ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY (ResourceTypeId, ResourceId))
  DECLARE @ExistingRefIds AS TABLE (ResourceTypeId smallint NOT NULL, ResourceIdInt bigint NOT NULL, ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY (ResourceTypeId, ResourceId))
  DECLARE @InsertRefIds AS TABLE (ResourceTypeId smallint NOT NULL, IdIndex int NOT NULL, ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY (ResourceTypeId, ResourceId))
  DECLARE @InsertedRefIds AS TABLE (ResourceTypeId smallint NOT NULL, ResourceIdInt bigint NOT NULL, ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY (ResourceTypeId, ResourceId))
  DECLARE @ReferenceSearchParamsWithIds AS TABLE
  (
      ResourceTypeId           smallint NOT NULL
     ,ResourceSurrogateId      bigint   NOT NULL
     ,SearchParamId            smallint NOT NULL
     ,BaseUri                  varchar(128) COLLATE Latin1_General_100_CS_AS NULL
     ,ReferenceResourceTypeId  smallint NULL
     ,ReferenceResourceIdInt   bigint   NOT NULL
     ,ReferenceResourceVersion int      NULL

     UNIQUE (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceIdInt) 
  )
  
  -- Prepare insert into ResourceIdIntMap outside of transaction to minimize blocking
  INSERT INTO @InputRefIds SELECT DISTINCT ReferenceResourceTypeId, ReferenceResourceId FROM @ReferenceSearchParams WHERE ReferenceResourceTypeId IS NOT NULL

  INSERT INTO @ExistingRefIds
       (     ResourceTypeId, ResourceIdInt,   ResourceId )
    SELECT A.ResourceTypeId, ResourceIdInt, A.ResourceId
      FROM @InputRefIds A
           JOIN dbo.ResourceIdIntMap B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
    
  INSERT INTO @InsertRefIds 
         ( ResourceTypeId,                                                     IdIndex, ResourceId ) 
    SELECT ResourceTypeId, row_number() OVER (ORDER BY ResourceTypeId, ResourceId) - 1, ResourceId
      FROM @InputRefIds A
      WHERE NOT EXISTS (SELECT * FROM @ExistingRefIds B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId)

  SET @InsertRows = (SELECT count(*) FROM @InsertRefIds)
  IF @InsertRows > 0
  BEGIN
    EXECUTE dbo.AssignResourceIdInts @InsertRows, @FirstIdInt OUT

    INSERT INTO @InsertedRefIds
         (   ResourceTypeId,         ResourceIdInt, ResourceId ) 
      SELECT ResourceTypeId, IdIndex + @FirstIdInt, ResourceId
        FROM @InsertRefIds
  END

  INSERT INTO @ReferenceSearchParamsWithIds
         (   ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId,                  ReferenceResourceIdInt, ReferenceResourceVersion )
    SELECT A.ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, isnull(C.ResourceIdInt,B.ResourceIdInt), ReferenceResourceVersion
      FROM @ReferenceSearchParams A
           LEFT OUTER JOIN @InsertedRefIds B ON B.ResourceTypeId = A.ReferenceResourceTypeId AND B.ResourceId = A.ReferenceResourceId
           LEFT OUTER JOIN @ExistingRefIds C ON C.ResourceTypeId = A.ReferenceResourceTypeId AND C.ResourceId = A.ReferenceResourceId

  BEGIN TRANSACTION

  -- Update the search parameter hash value in the main resource table
  IF EXISTS (SELECT * FROM @ResourcesLake)
    UPDATE B
      SET SearchParamHash = (SELECT SearchParamHash FROM @ResourcesLake A WHERE A.ResourceTypeId = B.ResourceTypeId AND A.ResourceSurrogateId = B.ResourceSurrogateId)
      OUTPUT deleted.ResourceTypeId, deleted.ResourceSurrogateId INTO @Ids 
      FROM dbo.Resource B 
      WHERE EXISTS (SELECT * FROM @ResourcesLake A WHERE A.ResourceTypeId = B.ResourceTypeId AND A.ResourceSurrogateId = B.ResourceSurrogateId)
        AND B.IsHistory = 0
  ELSE
    UPDATE B
      SET SearchParamHash = (SELECT SearchParamHash FROM @Resources A WHERE A.ResourceTypeId = B.ResourceTypeId AND A.ResourceSurrogateId = B.ResourceSurrogateId)
      OUTPUT deleted.ResourceTypeId, deleted.ResourceSurrogateId INTO @Ids 
      FROM dbo.Resource B 
      WHERE EXISTS (SELECT * FROM @Resources A WHERE A.ResourceTypeId = B.ResourceTypeId AND A.ResourceSurrogateId = B.ResourceSurrogateId)
        AND B.IsHistory = 0
  SET @ResourceRows = @@rowcount

  -- First, delete all the search params of the resources to reindex.
  DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.ResourceWriteClaim B ON B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B 
    OUTPUT deleted.ReferenceResourceTypeId, deleted.ReferenceResourceIdInt INTO @CurrentRefIdsRaw
    FROM @Ids A INNER LOOP JOIN dbo.ResourceReferenceSearchParams B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.StringReferenceSearchParams B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.TokenSearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.TokenText B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.StringSearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.UriSearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.NumberSearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.QuantitySearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.DateTimeSearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.ReferenceTokenCompositeSearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.TokenTokenCompositeSearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.TokenDateTimeCompositeSearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.TokenQuantityCompositeSearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.TokenStringCompositeSearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.TokenNumberNumberCompositeSearchParam B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId

  -- Next, insert all the new search params.
  INSERT INTO dbo.ResourceWriteClaim 
         ( ResourceSurrogateId, ClaimTypeId, ClaimValue )
    SELECT ResourceSurrogateId, ClaimTypeId, ClaimValue
      FROM @ResourceWriteClaims
        
  -- start delete logic from ResourceIdIntMap
  INSERT INTO @CurrentRefIds SELECT DISTINCT ResourceTypeId, ResourceIdInt FROM @CurrentRefIdsRaw
  SET @CurrentRows = @@rowcount
  IF @CurrentRows > 0
  BEGIN
    -- remove not reused
    DELETE FROM A FROM @CurrentRefIds A WHERE EXISTS (SELECT * FROM @ReferenceSearchParamsWithIds B WHERE B.ReferenceResourceTypeId = A.ResourceTypeId AND B.ReferenceResourceIdInt = A.ResourceIdInt)
    SET @CurrentRows -= @@rowcount 
    IF @CurrentRows > 0
    BEGIN
      -- remove referenced by resources
      DELETE FROM A FROM @CurrentRefIds A WHERE EXISTS (SELECT * FROM dbo.CurrentResources B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceIdInt = A.ResourceIdInt)
      SET @CurrentRows -= @@rowcount
      IF @CurrentRows > 0
      BEGIN
        -- remove referenced by reference search params
        DELETE FROM A FROM @CurrentRefIds A WHERE EXISTS (SELECT * FROM dbo.ResourceReferenceSearchParams B WHERE B.ReferenceResourceTypeId = A.ResourceTypeId AND B.ReferenceResourceIdInt = A.ResourceIdInt)
        SET @CurrentRows -= @@rowcount
        IF @CurrentRows > 0
        BEGIN
          -- finally delete from id map
          DELETE FROM B FROM @CurrentRefIds A INNER LOOP JOIN dbo.ResourceIdIntMap B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceIdInt = A.ResourceIdInt
          SET @DeletedIdMap = @@rowcount
        END
      END
    END
  END

  INSERT INTO dbo.ResourceIdIntMap 
      (    ResourceTypeId, ResourceIdInt, ResourceId ) 
    SELECT ResourceTypeId, ResourceIdInt, ResourceId
      FROM @InsertedRefIds

  INSERT INTO dbo.ResourceReferenceSearchParams 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceIdInt )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceIdInt
      FROM @ReferenceSearchParamsWithIds

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

  SET @FailedResources = (SELECT count(*) FROM @Resources) + (SELECT count(*) FROM @ResourcesLake) - @ResourceRows

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status=''End'',@Start=@st,@Rows=@ResourceRows,@Text=@DeletedIdMap
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status=''Error'',@Start=@st

  IF error_number() IN (2601, 2627) AND error_message() LIKE ''%''''dbo.ResourceIdIntMap''''%'' -- pk violation
     OR error_number() = 547 AND error_message() LIKE ''%DELETE%'' -- reference violation on DELETE
  BEGIN
    DELETE FROM @Ids
    DELETE FROM @InputRefIds
    DELETE FROM @CurrentRefIdsRaw
    DELETE FROM @CurrentRefIds
    DELETE FROM @ExistingRefIds
    DELETE FROM @InsertRefIds
    DELETE FROM @InsertedRefIds
    DELETE FROM @ReferenceSearchParamsWithIds

    GOTO RetryResourceIdIntMapLogic
  END
  ELSE
    THROW
END CATCH
  ')
  EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Target='UpdateResourceSearchParams',@Action='Alter'

  EXECUTE('
ALTER PROCEDURE dbo.HardDeleteResource
   @ResourceTypeId smallint
  ,@ResourceId varchar(64)
  ,@KeepCurrentVersion bit
  ,@IsResourceChangeCaptureEnabled bit = 0 -- TODO: Remove input parameter after deployment
  ,@MakeResourceInvisible bit = 0
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(200) = ''RT=''+convert(varchar,@ResourceTypeId)+'' R=''+@ResourceId+'' V=''+convert(varchar,@KeepCurrentVersion)
       ,@st datetime = getUTCdate()
       ,@TransactionId bigint
       ,@DeletedIdMap int = 0
       ,@Rows int

IF @IsResourceChangeCaptureEnabled = 1
  SET @MakeResourceInvisible = 1

SET @Mode += '' I=''+convert(varchar,@MakeResourceInvisible)

IF @MakeResourceInvisible = 1
BEGIN 
  EXECUTE dbo.MergeResourcesBeginTransaction @Count = 1, @TransactionId = @TransactionId OUT
  SET @Mode += '' T=''+convert(varchar,@TransactionId)
END

DECLARE @Ids TABLE (ResourceSurrogateId bigint NOT NULL, ResourceIdInt bigint NOT NULL)
DECLARE @IdsDistinct TABLE (ResourceTypeId smallint NOT NULL, ResourceIdInt bigint NOT NULL PRIMARY KEY (ResourceTypeId, ResourceIdInt))
DECLARE @RefIdsRaw TABLE (ResourceTypeId smallint NOT NULL, ResourceIdInt bigint NOT NULL)

RetryResourceIdIntMapLogic:
BEGIN TRY
  BEGIN TRANSACTION

  IF @MakeResourceInvisible = 1
    UPDATE dbo.Resource
      SET IsDeleted = 1
         ,RawResource = 0xF -- invisible value
         ,SearchParamHash = NULL
         ,HistoryTransactionId = @TransactionId
      OUTPUT deleted.ResourceSurrogateId, deleted.ResourceIdInt INTO @Ids
      WHERE ResourceTypeId = @ResourceTypeId
        AND ResourceId = @ResourceId
        AND (@KeepCurrentVersion = 0 OR IsHistory = 1)
        AND (RawResource IS NULL -- stored in ADLS
             OR RawResource <> 0xF -- stored in the database and not already invisible
            )
  ELSE
  BEGIN
    DELETE dbo.Resource
      OUTPUT deleted.ResourceSurrogateId, deleted.ResourceIdInt INTO @Ids
      WHERE ResourceTypeId = @ResourceTypeId
        AND ResourceId = @ResourceId
        AND (@KeepCurrentVersion = 0 OR IsHistory = 1)
        AND RawResource <> 0xF

    INSERT INTO @IdsDistinct SELECT DISTINCT @ResourceTypeId, ResourceIdInt FROM @Ids
    SET @Rows = @@rowcount
    IF @Rows > 0
    BEGIN
      DELETE FROM A FROM @IdsDistinct A WHERE EXISTS (SELECT * FROM dbo.CurrentResources B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceIdInt = A.ResourceIdInt)
      SET @Rows -= @@rowcount
      IF @Rows > 0
      BEGIN
        DELETE FROM A FROM @IdsDistinct A WHERE EXISTS (SELECT * FROM dbo.ResourceReferenceSearchParams B WHERE B.ReferenceResourceTypeId = A.ResourceTypeId AND B.ReferenceResourceIdInt = A.ResourceIdInt)
        SET @Rows -= @@rowcount
        IF @Rows > 0
        BEGIN
          DELETE FROM B FROM @IdsDistinct A INNER LOOP JOIN dbo.ResourceIdIntMap B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceIdInt = A.ResourceIdInt
          SET @DeletedIdMap = @@rowcount
        END
      END
    END
  END

  IF @KeepCurrentVersion = 0
  BEGIN
    -- PAGLOCK allows deallocation of empty page without waiting for ghost cleanup 
    DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.ResourceWriteClaim B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B 
      OUTPUT deleted.ReferenceResourceTypeId, deleted.ReferenceResourceIdInt INTO @RefIdsRaw
      FROM @Ids A INNER LOOP JOIN dbo.ResourceReferenceSearchParams B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM @IdsDistinct -- is used above
    INSERT INTO @IdsDistinct SELECT DISTINCT ResourceTypeId, ResourceIdInt FROM @RefIdsRaw
    SET @Rows = @@rowcount
    IF @Rows > 0
    BEGIN
      DELETE FROM A FROM @IdsDistinct A WHERE EXISTS (SELECT * FROM dbo.CurrentResources B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceIdInt = A.ResourceIdInt)
      SET @Rows -= @@rowcount
      IF @Rows > 0
      BEGIN
        DELETE FROM A FROM @IdsDistinct A WHERE EXISTS (SELECT * FROM dbo.ResourceReferenceSearchParams B WHERE B.ReferenceResourceTypeId = A.ResourceTypeId AND B.ReferenceResourceIdInt = A.ResourceIdInt)
        SET @Rows -= @@rowcount
        IF @Rows > 0
        BEGIN
          DELETE FROM B FROM @IdsDistinct A INNER LOOP JOIN dbo.ResourceIdIntMap B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceIdInt = A.ResourceIdInt
          SET @DeletedIdMap += @@rowcount
        END
      END
    END
    DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.StringReferenceSearchParams B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.TokenSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.TokenText B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.StringSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.UriSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.NumberSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.QuantitySearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.DateTimeSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.ReferenceTokenCompositeSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.TokenTokenCompositeSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.TokenDateTimeCompositeSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.TokenQuantityCompositeSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.TokenStringCompositeSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.TokenNumberNumberCompositeSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
  END
  
  COMMIT TRANSACTION

  IF @MakeResourceInvisible = 1
    EXECUTE dbo.MergeResourcesCommitTransaction @TransactionId

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status=''End'',@Start=@st,@Text=@DeletedIdMap
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status=''Error'',@Start=@st
  
  IF error_number() = 547 AND error_message() LIKE ''%DELETE%''-- reference violation on DELETE
  BEGIN
    DELETE FROM @Ids
    DELETE FROM @RefIdsRaw
    DELETE FROM @IdsDistinct
    GOTO RetryResourceIdIntMapLogic
  END
  ELSE
    THROW
END CATCH
  ')
  EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Target='HardDeleteResource',@Action='Alter'

  EXECUTE('
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
       ,@CurrentRows int
       ,@DeletedIdMap int

DECLARE @Mode varchar(200) = isnull((SELECT ''RT=[''+convert(varchar,min(ResourceTypeId))+'',''+convert(varchar,max(ResourceTypeId))+''] Sur=[''+convert(varchar,min(ResourceSurrogateId))+'',''+convert(varchar,max(ResourceSurrogateId))+''] V=''+convert(varchar,max(Version))+'' Rows=''+convert(varchar,count(*)) FROM (SELECT ResourceTypeId, ResourceSurrogateId, Version FROM @Resources UNION ALL SELECT ResourceTypeId, ResourceSurrogateId, Version FROM @ResourcesLake) A),''Input=Empty'')
SET @Mode += '' E=''+convert(varchar,@RaiseExceptionOnConflict)+'' CC=''+convert(varchar,@IsResourceChangeCaptureEnabled)+'' IT=''+convert(varchar,@InitialTranCount)+'' T=''+isnull(convert(varchar,@TransactionId),''NULL'')

SET @AffectedRows = 0

RetryResourceIdIntMapLogic:
BEGIN TRY
  DECLARE @InputIds AS TABLE (ResourceTypeId smallint NOT NULL, ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY (ResourceTypeId, ResourceId))
  DECLARE @CurrentRefIdsRaw TABLE (ResourceTypeId smallint NOT NULL, ResourceIdInt bigint NOT NULL)
  DECLARE @CurrentRefIds TABLE (ResourceTypeId smallint NOT NULL, ResourceIdInt bigint NOT NULL PRIMARY KEY (ResourceTypeId, ResourceIdInt))
  DECLARE @ExistingIdsReference AS TABLE (ResourceTypeId smallint NOT NULL, ResourceIdInt bigint NOT NULL, ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY (ResourceTypeId, ResourceId))
  DECLARE @ExistingIdsResource AS TABLE (ResourceTypeId smallint NOT NULL, ResourceIdInt bigint NOT NULL, ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY (ResourceTypeId, ResourceId))
  DECLARE @InsertIds AS TABLE (ResourceTypeId smallint NOT NULL, IdIndex int NOT NULL, ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY (ResourceTypeId, ResourceId))
  DECLARE @InsertedIdsReference AS TABLE (ResourceTypeId smallint NOT NULL, ResourceIdInt bigint NOT NULL, ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY (ResourceTypeId, ResourceId))
  DECLARE @InsertedIdsResource AS TABLE (ResourceTypeId smallint NOT NULL, ResourceIdInt bigint NOT NULL, ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY (ResourceTypeId, ResourceId))
  DECLARE @ResourcesWithIds AS TABLE 
    (
        ResourceTypeId       smallint            NOT NULL
       ,ResourceSurrogateId  bigint              NOT NULL
       ,ResourceId           varchar(64)         COLLATE Latin1_General_100_CS_AS NOT NULL
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
  
  -- Prepare id map for reference search params Start ---------------------------------------------------------------------------
  INSERT INTO @InputIds SELECT DISTINCT ReferenceResourceTypeId, ReferenceResourceId FROM @ReferenceSearchParams WHERE ReferenceResourceTypeId IS NOT NULL

  INSERT INTO @ExistingIdsReference 
       (     ResourceTypeId, ResourceIdInt,   ResourceId )
    SELECT A.ResourceTypeId, ResourceIdInt, A.ResourceId
      FROM @InputIds A
           JOIN dbo.ResourceIdIntMap B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
    
  INSERT INTO @InsertIds 
         ( ResourceTypeId,                                                     IdIndex, ResourceId ) 
    SELECT ResourceTypeId, row_number() OVER (ORDER BY ResourceTypeId, ResourceId) - 1, ResourceId
      FROM @InputIds A
      WHERE NOT EXISTS (SELECT * FROM @ExistingIdsReference B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId)

  SET @NewIdsCount = (SELECT count(*) FROM @InsertIds)
  IF @NewIdsCount > 0
  BEGIN
    EXECUTE dbo.AssignResourceIdInts @NewIdsCount, @FirstIdInt OUT

    INSERT INTO @InsertedIdsReference 
         (   ResourceTypeId,         ResourceIdInt, ResourceId ) 
      SELECT ResourceTypeId, IdIndex + @FirstIdInt, ResourceId
        FROM @InsertIds
  END
  
  INSERT INTO @ReferenceSearchParamsWithIds
         (   ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId,                  ReferenceResourceIdInt )
    SELECT A.ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, isnull(C.ResourceIdInt,B.ResourceIdInt)
      FROM @ReferenceSearchParams A
           LEFT OUTER JOIN @InsertedIdsReference B ON B.ResourceTypeId = A.ReferenceResourceTypeId AND B.ResourceId = A.ReferenceResourceId
           LEFT OUTER JOIN @ExistingIdsReference C ON C.ResourceTypeId = A.ReferenceResourceTypeId AND C.ResourceId = A.ReferenceResourceId
      WHERE ReferenceResourceTypeId IS NOT NULL
  -- Prepare id map for reference search params End ---------------------------------------------------------------------------

  -- Prepare id map for resources Start ---------------------------------------------------------------------------
  DELETE FROM @InputIds
  IF EXISTS (SELECT * FROM @ResourcesLake)
    INSERT INTO @InputIds SELECT ResourceTypeId, ResourceId FROM @ResourcesLake GROUP BY ResourceTypeId, ResourceId
  ELSE
    INSERT INTO @InputIds SELECT ResourceTypeId, ResourceId FROM @Resources GROUP BY ResourceTypeId, ResourceId

  INSERT INTO @ExistingIdsResource 
       (     ResourceTypeId, ResourceIdInt,   ResourceId )
    SELECT A.ResourceTypeId, isnull(C.ResourceIdInt,B.ResourceIdInt), A.ResourceId
      FROM @InputIds A
           LEFT OUTER JOIN dbo.ResourceIdIntMap B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
           LEFT OUTER JOIN @InsertedIdsReference C ON C.ResourceTypeId = A.ResourceTypeId AND C.ResourceId = A.ResourceId
      WHERE C.ResourceIdInt IS NOT NULL OR B.ResourceIdInt IS NOT NULL

  DELETE FROM @InsertIds
  INSERT INTO @InsertIds 
         ( ResourceTypeId,                                                     IdIndex, ResourceId ) 
    SELECT ResourceTypeId, row_number() OVER (ORDER BY ResourceTypeId, ResourceId) - 1, ResourceId
      FROM @InputIds A
      WHERE NOT EXISTS (SELECT * FROM @ExistingIdsResource B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId)

  SET @NewIdsCount = (SELECT count(*) FROM @InsertIds)
  IF @NewIdsCount > 0
  BEGIN
    EXECUTE dbo.AssignResourceIdInts @NewIdsCount, @FirstIdInt OUT

    INSERT INTO @InsertedIdsResource 
         (   ResourceTypeId,         ResourceIdInt, ResourceId ) 
      SELECT ResourceTypeId, IdIndex + @FirstIdInt, ResourceId
        FROM @InsertIds
  END
  
  IF EXISTS (SELECT * FROM @ResourcesLake)
    INSERT INTO @ResourcesWithIds
           (   ResourceTypeId,   ResourceId,                           ResourceIdInt, Version, HasVersionToCompare, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, KeepHistory, RawResource, IsRawResourceMetaSet, SearchParamHash, FileId, OffsetInFile )
      SELECT A.ResourceTypeId, A.ResourceId, isnull(C.ResourceIdInt,B.ResourceIdInt), Version, HasVersionToCompare, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, KeepHistory, RawResource, IsRawResourceMetaSet, SearchParamHash, FileId, OffsetInFile
        FROM @ResourcesLake A
             LEFT OUTER JOIN @InsertedIdsResource B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
             LEFT OUTER JOIN @ExistingIdsResource C ON C.ResourceTypeId = A.ResourceTypeId AND C.ResourceId = A.ResourceId
  ELSE
    INSERT INTO @ResourcesWithIds
           (   ResourceTypeId,   ResourceId,                           ResourceIdInt, Version, HasVersionToCompare, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, KeepHistory, RawResource, IsRawResourceMetaSet, SearchParamHash, FileId, OffsetInFile )
      SELECT A.ResourceTypeId, A.ResourceId, isnull(C.ResourceIdInt,B.ResourceIdInt), Version, HasVersionToCompare, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, KeepHistory, RawResource, IsRawResourceMetaSet, SearchParamHash,   NULL,         NULL
        FROM @Resources A
             LEFT OUTER JOIN @InsertedIdsResource B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
             LEFT OUTER JOIN @ExistingIdsResource C ON C.ResourceTypeId = A.ResourceTypeId AND C.ResourceId = A.ResourceId
  -- Prepare id map for resources End ---------------------------------------------------------------------------

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

  IF @SingleTransaction = 0 AND isnull((SELECT Number FROM dbo.Parameters WHERE Id = ''MergeResources.NoTransaction.IsEnabled''),0) = 0
    SET @SingleTransaction = 1
  
  SET @Mode += '' ST=''+convert(varchar,@SingleTransaction)

  -- perform retry check in transaction to hold locks
  IF @InitialTranCount = 0
  BEGIN
    IF EXISTS (SELECT * -- This extra statement avoids putting range locks when we don''t need them
                 FROM @ResourcesWithIds A JOIN dbo.Resource B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
                 WHERE B.IsHistory = 0
              )
    BEGIN
      BEGIN TRANSACTION

      INSERT INTO @Existing
              (  ResourceTypeId,           SurrogateId )
        SELECT B.ResourceTypeId, B.ResourceSurrogateId
          FROM (SELECT TOP (@DummyTop) * FROM @ResourcesWithIds) A
               JOIN dbo.Resource B WITH (ROWLOCK, HOLDLOCK) ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
          WHERE B.IsHistory = 0
            AND B.ResourceId = A.ResourceId
            AND B.Version = A.Version
          OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
    
      IF @@rowcount = (SELECT count(*) FROM @ResourcesWithIds) SET @IsRetry = 1

      IF @IsRetry = 0 COMMIT TRANSACTION -- commit check transaction 
    END
  END

  SET @Mode += '' R=''+convert(varchar,@IsRetry)

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
      THROW 50409, ''Resource has been recently updated or added, please compare the resource content in code for any duplicate updates'', 1

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

      IF @IsResourceChangeCaptureEnabled = 1 AND NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = ''InvisibleHistory.IsEnabled'' AND Number = 0)
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
      DELETE FROM dbo.ResourceReferenceSearchParams
        OUTPUT deleted.ReferenceResourceTypeId, deleted.ReferenceResourceIdInt INTO @CurrentRefIdsRaw
        WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
      SET @CurrentRows = @@rowcount
      SET @AffectedRows += @CurrentRows
      -- start deleting from ResourceIdIntMap
      INSERT INTO @CurrentRefIds SELECT DISTINCT ResourceTypeId, ResourceIdInt FROM @CurrentRefIdsRaw
      SET @CurrentRows = @@rowcount
      IF @CurrentRows > 0
      BEGIN
        -- remove not reused
        DELETE FROM A FROM @CurrentRefIds A WHERE EXISTS (SELECT * FROM @ReferenceSearchParamsWithIds B WHERE B.ReferenceResourceTypeId = A.ResourceTypeId AND B.ReferenceResourceIdInt = A.ResourceIdInt)
        SET @CurrentRows -= @@rowcount 
        IF @CurrentRows > 0
        BEGIN
          -- remove referenced in Resources
          DELETE FROM A FROM @CurrentRefIds A WHERE EXISTS (SELECT * FROM dbo.CurrentResources B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceIdInt = A.ResourceIdInt)
          SET @CurrentRows -= @@rowcount
          IF @CurrentRows > 0
          BEGIN
            -- remove still referenced in ResourceReferenceSearchParams
            DELETE FROM A FROM @CurrentRefIds A WHERE EXISTS (SELECT * FROM dbo.ResourceReferenceSearchParams B WHERE B.ReferenceResourceTypeId = A.ResourceTypeId AND B.ReferenceResourceIdInt = A.ResourceIdInt)
            SET @CurrentRows -= @@rowcount
            IF @CurrentRows > 0
            BEGIN
              -- delete from id map
              DELETE FROM B FROM @CurrentRefIds A INNER LOOP JOIN dbo.ResourceIdIntMap B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceIdInt = A.ResourceIdInt
              SET @DeletedIdMap = @@rowcount
            END
          END
        END
      END
      DELETE FROM dbo.StringReferenceSearchParams WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
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

      --EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status=''Info'',@Start=@st,@Rows=@AffectedRows,@Text=''Old rows''
    END

    INSERT INTO dbo.ResourceIdIntMap 
        (    ResourceTypeId, ResourceIdInt, ResourceId ) 
      SELECT ResourceTypeId, ResourceIdInt, ResourceId
        FROM @InsertedIdsResource

    INSERT INTO dbo.ResourceIdIntMap 
        (    ResourceTypeId, ResourceIdInt, ResourceId ) 
      SELECT ResourceTypeId, ResourceIdInt, ResourceId
        FROM @InsertedIdsReference
    
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
    EXECUTE dbo.CaptureResourceIdsForChanges @Resources, @ResourcesLake

  IF @TransactionId IS NOT NULL
    EXECUTE dbo.MergeResourcesCommitTransaction @TransactionId

  IF @InitialTranCount = 0 AND @@trancount > 0 COMMIT TRANSACTION

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status=''End'',@Start=@st,@Rows=@AffectedRows,@Text=@DeletedIdMap
END TRY
BEGIN CATCH
  IF @InitialTranCount = 0 AND @@trancount > 0 ROLLBACK TRANSACTION
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status=''Error'',@Start=@st

  IF error_number() IN (2601, 2627) AND error_message() LIKE ''%''''dbo.ResourceIdIntMap''''%'' -- pk violation
     OR error_number() = 547 AND error_message() LIKE ''%DELETE%'' -- reference violation on DELETE
  BEGIN
    DELETE FROM @ResourcesWithIds
    DELETE FROM @ReferenceSearchParamsWithIds
    DELETE FROM @CurrentRefIdsRaw
    DELETE FROM @CurrentRefIds
    DELETE FROM @InputIds
    DELETE FROM @InsertIds
    DELETE FROM @InsertedIdsReference
    DELETE FROM @ExistingIdsReference
    DELETE FROM @InsertedIdsResource
    DELETE FROM @ExistingIdsResource
    DELETE FROM @Existing
    DELETE FROM @ResourceInfos
    DELETE FROM @PreviousSurrogateIds 

    GOTO RetryResourceIdIntMapLogic
  END
  ELSE 
    IF @RaiseExceptionOnConflict = 1 AND error_number() IN (2601, 2627) AND (error_message() LIKE ''%''''dbo.Resource%'' OR error_message() LIKE ''%''''dbo.CurrentResources%'' OR error_message() LIKE ''%''''dbo.HistoryResources%'' OR error_message() LIKE ''%''''dbo.RawResources''''%'')
      THROW 50409, ''Resource has been recently updated or added, please compare the resource content in code for any duplicate updates'', 1;
    ELSE
      THROW
END CATCH
  ')
  EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Target='MergeResources',@Action='Alter'

  EXECUTE('
ALTER PROCEDURE dbo.MergeResourcesDeleteInvisibleHistory @TransactionId bigint, @AffectedRows int = NULL OUT
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(100) = ''T=''+convert(varchar,@TransactionId)
       ,@st datetime
       ,@Rows int
       ,@DeletedIdMap int

SET @AffectedRows = 0

Retry:
BEGIN TRY 
  DECLARE @Ids TABLE (ResourceTypeId smallint NOT NULL, ResourceIdInt bigint NOT NULL)
  
  BEGIN TRANSACTION

  SET @st = getUTCdate()
  DELETE FROM A
    OUTPUT deleted.ResourceTypeId, deleted.ResourceIdInt INTO @Ids 
    FROM dbo.Resource A
    WHERE HistoryTransactionId = @TransactionId -- requires updated statistics
  SET @Rows = @@rowcount
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status=''Run'',@Target=''Resource'',@Action=''Delete'',@Start=@st,@Rows=@Rows
  SET @AffectedRows += @Rows

  SET @st = getUTCdate()
  IF @Rows > 0
  BEGIN
    -- remove referenced in resources
    DELETE FROM A FROM @Ids A WHERE EXISTS (SELECT * FROM dbo.CurrentResources B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceIdInt = A.ResourceIdInt)
    SET @Rows -= @@rowcount
    IF @Rows > 0
    BEGIN
      -- remove referenced in reference search params
      DELETE FROM A FROM @Ids A WHERE EXISTS (SELECT * FROM dbo.ResourceReferenceSearchParams B WHERE B.ReferenceResourceTypeId = A.ResourceTypeId AND B.ReferenceResourceIdInt = A.ResourceIdInt)
      SET @Rows -= @@rowcount
      IF @Rows > 0
      BEGIN
        -- delete from id map
        DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.ResourceIdIntMap B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceIdInt = A.ResourceIdInt
        SET @DeletedIdMap = @@rowcount
      END
    END
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status=''Run'',@Target=''ResourceIdIntMap'',@Action=''Delete'',@Start=@st,@Rows=@DeletedIdMap
  END

  COMMIT TRANSACTION
  
  SET @st = getUTCdate()
  UPDATE dbo.Resource SET TransactionId = NULL WHERE TransactionId = @TransactionId
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status=''End'',@Target=''Resource'',@Action=''Update'',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  IF @@trancount > 0 ROLLBACK TRANSACTION
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status=''Error''
  IF error_number() = 547 AND error_message() LIKE ''%DELETE%'' -- reference violation on DELETE
  BEGIN
    DELETE FROM @Ids
    GOTO Retry
  END
  ELSE
    THROW
END CATCH
  ')
  EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Target='MergeResourcesDeleteInvisibleHistory',@Action='Alter'

  EXECUTE('
ALTER PROCEDURE dbo.GetResourcesByTypeAndSurrogateIdRange @ResourceTypeId smallint, @StartId bigint, @EndId bigint, @GlobalEndId bigint = NULL, @IncludeHistory bit = 1, @IncludeDeleted bit = 1
AS
set nocount on
DECLARE @SP varchar(100) = ''GetResourcesByTypeAndSurrogateIdRange''
       ,@Mode varchar(100) = ''RT=''+isnull(convert(varchar,@ResourceTypeId),''NULL'')
                           +'' S=''+isnull(convert(varchar,@StartId),''NULL'')
                           +'' E=''+isnull(convert(varchar,@EndId),''NULL'')
                           +'' GE=''+isnull(convert(varchar,@GlobalEndId),''NULL'')
                           +'' HI=''+isnull(convert(varchar,@IncludeHistory),''NULL'')
                           +'' DE=''+isnull(convert(varchar,@IncludeDeleted),''NULL'')
       ,@st datetime = getUTCdate()
       ,@DummyTop bigint = 9223372036854775807
       ,@Rows int

BEGIN TRY
  DECLARE @ResourceIdInts TABLE (ResourceIdInt bigint PRIMARY KEY)
  DECLARE @SurrogateIds TABLE (MaxSurrogateId bigint PRIMARY KEY)

  IF @GlobalEndId IS NOT NULL AND @IncludeHistory = 0 -- snapshot view
  BEGIN
    INSERT INTO @ResourceIdInts
      SELECT DISTINCT ResourceIdInt
        FROM dbo.Resource 
        WHERE ResourceTypeId = @ResourceTypeId 
          AND ResourceSurrogateId BETWEEN @StartId AND @EndId
          AND IsHistory = 1
          AND (IsDeleted = 0 OR @IncludeDeleted = 1)
        OPTION (MAXDOP 1)

    IF @@rowcount > 0
      INSERT INTO @SurrogateIds
        SELECT ResourceSurrogateId
          FROM (SELECT ResourceIdInt, ResourceSurrogateId, RowId = row_number() OVER (PARTITION BY ResourceIdInt ORDER BY ResourceSurrogateId DESC)
                  FROM dbo.Resource
                  WHERE ResourceTypeId = @ResourceTypeId
                    AND ResourceIdInt IN (SELECT TOP (@DummyTop) ResourceIdInt FROM @ResourceIdInts)
                    AND ResourceSurrogateId BETWEEN @StartId AND @GlobalEndId
               ) A
          WHERE RowId = 1
            AND ResourceSurrogateId BETWEEN @StartId AND @EndId
          OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
  END

  IF @IncludeHistory = 0
    SELECT ResourceTypeId, ResourceId, Version, IsDeleted, ResourceSurrogateId, RequestMethod, IsMatch = convert(bit,1), IsPartial = convert(bit,0), IsRawResourceMetaSet, SearchParamHash, RawResource, FileId, OffsetInFile
      FROM dbo.Resource
      WHERE ResourceTypeId = @ResourceTypeId 
        AND ResourceSurrogateId BETWEEN @StartId AND @EndId 
        AND IsHistory = 0
        AND (IsDeleted = 0 OR @IncludeDeleted = 1)
    UNION ALL
    SELECT ResourceTypeId, ResourceId, Version, IsDeleted, ResourceSurrogateId, RequestMethod, IsMatch = convert(bit,1), IsPartial = convert(bit,0), IsRawResourceMetaSet, SearchParamHash, RawResource, FileId, OffsetInFile
      FROM @SurrogateIds
           JOIN dbo.Resource ON ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId = MaxSurrogateId
      WHERE IsHistory = 1
        AND (IsDeleted = 0 OR @IncludeDeleted = 1)
    OPTION (MAXDOP 1, LOOP JOIN)
  ELSE -- @IncludeHistory = 1
    SELECT ResourceTypeId, ResourceId, Version, IsDeleted, ResourceSurrogateId, RequestMethod, IsMatch = convert(bit,1), IsPartial = convert(bit,0), IsRawResourceMetaSet, SearchParamHash, RawResource, FileId, OffsetInFile
      FROM dbo.Resource
      WHERE ResourceTypeId = @ResourceTypeId 
        AND ResourceSurrogateId BETWEEN @StartId AND @EndId 
        AND (IsDeleted = 0 OR @IncludeDeleted = 1)
    UNION ALL
    SELECT ResourceTypeId, ResourceId, Version, IsDeleted, ResourceSurrogateId, RequestMethod, IsMatch = convert(bit,1), IsPartial = convert(bit,0), IsRawResourceMetaSet, SearchParamHash, RawResource, FileId, OffsetInFile
      FROM @SurrogateIds
           JOIN dbo.Resource ON ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId = MaxSurrogateId
      WHERE IsHistory = 1
        AND (IsDeleted = 0 OR @IncludeDeleted = 1)
    OPTION (MAXDOP 1, LOOP JOIN)

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status=''End'',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status=''Error'';
  THROW
END CATCH
  ')
  EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Target='GetResourcesByTypeAndSurrogateIdRange',@Action='Alter'
END TRY
BEGIN CATCH
  EXECUTE dbo.LogEvent @Process=@Process,@Status='Error';
  THROW
END CATCH
GO
--DROP TABLE IF EXISTS ResourceTbl -- TODO: Remove table after deployment
GO
--DROP TABLE IF EXISTS ReferenceSearchParamTbl -- TODO: Remove table after deployment
GO
DROP PROCEDURE IF EXISTS tmp_MoveResources
GO
