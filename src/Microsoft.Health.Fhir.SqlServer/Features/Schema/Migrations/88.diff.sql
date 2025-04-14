
-- Step 1: Create the new type ResourceListWithLake with the same definition as ResourceListLake  
IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'ResourceListWithLake' AND is_table_type = 1)
BEGIN  
   CREATE TYPE dbo.ResourceListWithLake AS TABLE
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
       ,ResourceLength       int                 NULL

        PRIMARY KEY (ResourceTypeId, ResourceSurrogateId)
       ,UNIQUE (ResourceTypeId, ResourceId, Version)
    )
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns 
               WHERE Name = N'ResourceLength' AND Object_ID = Object_ID(N'dbo.CurrentResources'))
BEGIN
    ALTER TABLE dbo.CurrentResources
    ADD ResourceLength INT NULL;
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns 
               WHERE Name = N'ResourceLength' AND Object_ID = Object_ID(N'dbo.HistoryResources'))
BEGIN
    ALTER TABLE dbo.HistoryResources
    ADD ResourceLength INT NULL;
END
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
      ,RawResource
      ,IsRawResourceMetaSet
      ,SearchParamHash
      ,TransactionId 
      ,HistoryTransactionId
      ,FileId
      ,OffsetInFile
      ,ResourceLength
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
      ,ResourceLength
  FROM dbo.HistoryResources A
       LEFT OUTER JOIN dbo.RawResources B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
       LEFT OUTER JOIN dbo.ResourceIdIntMap C ON C.ResourceTypeId = A.ResourceTypeId AND C.ResourceIdInt = A.ResourceIdInt
GO
ALTER TRIGGER dbo.ResourceIns ON dbo.Resource INSTEAD OF INSERT
AS
BEGIN
  INSERT INTO dbo.RawResources
         ( ResourceTypeId, ResourceSurrogateId, RawResource )
    SELECT ResourceTypeId, ResourceSurrogateId, RawResource
      FROM Inserted
      WHERE RawResource IS NOT NULL

  INSERT INTO dbo.CurrentResources
         ( ResourceTypeId, ResourceSurrogateId, ResourceIdInt, Version, IsDeleted, RequestMethod, IsRawResourceMetaSet, SearchParamHash, TransactionId, HistoryTransactionId, FileId, OffsetInFile, ResourceLength )
    SELECT ResourceTypeId, ResourceSurrogateId, ResourceIdInt, Version, IsDeleted, RequestMethod, IsRawResourceMetaSet, SearchParamHash, TransactionId, HistoryTransactionId, FileId, OffsetInFile, ResourceLength
      FROM Inserted
      WHERE IsHistory = 0

  INSERT INTO dbo.HistoryResources
         ( ResourceTypeId, ResourceSurrogateId, ResourceIdInt, Version, IsDeleted, RequestMethod, IsRawResourceMetaSet, SearchParamHash, TransactionId, HistoryTransactionId, FileId, OffsetInFile, ResourceLength )
    SELECT ResourceTypeId, ResourceSurrogateId, ResourceIdInt, Version, IsDeleted, RequestMethod, IsRawResourceMetaSet, SearchParamHash, TransactionId, HistoryTransactionId, FileId, OffsetInFile, ResourceLength
      FROM Inserted
      WHERE IsHistory = 1
END
GO
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
    RAISERROR('Generic updates are not supported via Resource view',18,127)

  DELETE FROM A
    FROM dbo.CurrentResources A
    WHERE EXISTS (SELECT * FROM Inserted B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId AND B.IsHistory = 1)

  INSERT INTO dbo.HistoryResources
         ( ResourceTypeId, ResourceSurrogateId, ResourceIdInt, Version, IsDeleted, RequestMethod, IsRawResourceMetaSet, SearchParamHash, TransactionId, HistoryTransactionId, FileId, OffsetInFile, ResourceLength )
    SELECT ResourceTypeId, ResourceSurrogateId, ResourceIdInt, Version, IsDeleted, RequestMethod, IsRawResourceMetaSet, SearchParamHash, TransactionId, HistoryTransactionId, FileId, OffsetInFile, ResourceLength
      FROM Inserted
      WHERE IsHistory = 1
END
GO
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
GO

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
      ,ResourceLength
  FROM dbo.CurrentResources A
       LEFT OUTER JOIN dbo.RawResources B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
       LEFT OUTER JOIN dbo.ResourceIdIntMap C ON C.ResourceTypeId = A.ResourceTypeId AND C.ResourceIdInt = A.ResourceIdInt
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
            ,C.Version
            ,IsDeleted
            ,IsHistory
            ,RawResource
            ,IsRawResourceMetaSet
            ,SearchParamHash
            ,FileId
            ,OffsetInFile
            ,ResourceLength
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
                    ,ResourceLength
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
                    ,ResourceLength
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
          ,ResourceLength
      FROM (SELECT * FROM @ResourceKeys) A
           INNER LOOP JOIN dbo.ResourceIdIntMap B WITH (INDEX = U_ResourceIdIntMap_ResourceId_ResourceTypeId) ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
           INNER LOOP JOIN dbo.CurrentResources C ON C.ResourceTypeId = A.ResourceTypeId AND C.ResourceIdInt = B.ResourceIdInt
           LEFT OUTER JOIN dbo.RawResources D ON D.ResourceTypeId = A.ResourceTypeId AND D.ResourceSurrogateId = C.ResourceSurrogateId
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
          ,ResourceLength
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
    SELECT ResourceTypeId, ResourceId, Version, IsDeleted, ResourceSurrogateId, RequestMethod, IsMatch = convert(bit,1), IsPartial = convert(bit,0), IsRawResourceMetaSet, SearchParamHash, RawResource, FileId, OffsetInFile, ResourceLength
      FROM dbo.Resource
      WHERE ResourceTypeId = @ResourceTypeId 
        AND ResourceSurrogateId BETWEEN @StartId AND @EndId 
        AND IsHistory = 0
        AND (IsDeleted = 0 OR @IncludeDeleted = 1)
    UNION ALL
    SELECT ResourceTypeId, ResourceId, Version, IsDeleted, ResourceSurrogateId, RequestMethod, IsMatch = convert(bit,1), IsPartial = convert(bit,0), IsRawResourceMetaSet, SearchParamHash, RawResource, FileId, OffsetInFile, ResourceLength
      FROM @SurrogateIds
           JOIN dbo.Resource ON ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId = MaxSurrogateId
      WHERE IsHistory = 1
        AND (IsDeleted = 0 OR @IncludeDeleted = 1)
    OPTION (MAXDOP 1, LOOP JOIN)
  ELSE -- @IncludeHistory = 1
    SELECT ResourceTypeId, ResourceId, Version, IsDeleted, ResourceSurrogateId, RequestMethod, IsMatch = convert(bit,1), IsPartial = convert(bit,0), IsRawResourceMetaSet, SearchParamHash, RawResource, FileId, OffsetInFile, ResourceLength
      FROM dbo.Resource
      WHERE ResourceTypeId = @ResourceTypeId 
        AND ResourceSurrogateId BETWEEN @StartId AND @EndId 
        AND (IsDeleted = 0 OR @IncludeDeleted = 1)
    UNION ALL
    SELECT ResourceTypeId, ResourceId, Version, IsDeleted, ResourceSurrogateId, RequestMethod, IsMatch = convert(bit,1), IsPartial = convert(bit,0), IsRawResourceMetaSet, SearchParamHash, RawResource, FileId, OffsetInFile, ResourceLength
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
        ,MatchedResourceLength = D.ResourceLength
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

ALTER PROCEDURE dbo.CaptureResourceIdsForChanges @Resources dbo.ResourceList READONLY, @ResourcesLake dbo.ResourceListWithLake READONLY
AS
set nocount on
-- This procedure is intended to be called from the MergeResources procedure and relies on its transaction logic
INSERT INTO dbo.ResourceChangeData 
       ( ResourceId, ResourceTypeId, ResourceVersion,                                              ResourceChangeTypeId )
  SELECT ResourceId, ResourceTypeId,         Version, CASE WHEN IsDeleted = 1 THEN 2 WHEN Version > 1 THEN 1 ELSE 0 END
    FROM (SELECT ResourceId, ResourceTypeId, Version, IsHistory, IsDeleted FROM @Resources UNION ALL SELECT ResourceId, ResourceTypeId, Version, IsHistory, IsDeleted FROM @ResourcesLake) A
    WHERE IsHistory = 0
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
   ,@Resources dbo.ResourceList READONLY -- before lake code. TODO: Remove after deployment
   ,@ResourcesLake dbo.ResourceListWithLake READONLY -- Lake code
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

DECLARE @Mode varchar(200) = isnull((SELECT 'RT=['+convert(varchar,min(ResourceTypeId))+','+convert(varchar,max(ResourceTypeId))+'] Sur=['+convert(varchar,min(ResourceSurrogateId))+','+convert(varchar,max(ResourceSurrogateId))+'] V='+convert(varchar,max(Version))+' Rows='+convert(varchar,count(*)) FROM (SELECT ResourceTypeId, ResourceSurrogateId, Version FROM @Resources UNION ALL SELECT ResourceTypeId, ResourceSurrogateId, Version FROM @ResourcesLake) A),'Input=Empty')
SET @Mode += ' E='+convert(varchar,@RaiseExceptionOnConflict)+' CC='+convert(varchar,@IsResourceChangeCaptureEnabled)+' IT='+convert(varchar,@InitialTranCount)+' T='+isnull(convert(varchar,@TransactionId),'NULL')

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
       ,ResourceLength       int                 NULL

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
           (   ResourceTypeId,   ResourceId,                           ResourceIdInt, Version, HasVersionToCompare, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, KeepHistory, RawResource, IsRawResourceMetaSet, SearchParamHash, FileId, OffsetInFile, ResourceLength )
      SELECT A.ResourceTypeId, A.ResourceId, isnull(C.ResourceIdInt,B.ResourceIdInt), Version, HasVersionToCompare, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, KeepHistory, RawResource, IsRawResourceMetaSet, SearchParamHash, FileId, OffsetInFile, ResourceLength
        FROM @ResourcesLake A
             LEFT OUTER JOIN @InsertedIdsResource B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
             LEFT OUTER JOIN @ExistingIdsResource C ON C.ResourceTypeId = A.ResourceTypeId AND C.ResourceId = A.ResourceId
  ELSE
    INSERT INTO @ResourcesWithIds
           (   ResourceTypeId,   ResourceId,                           ResourceIdInt, Version, HasVersionToCompare, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, KeepHistory, RawResource, IsRawResourceMetaSet, SearchParamHash, FileId, OffsetInFile, ResourceLength )
      SELECT A.ResourceTypeId, A.ResourceId, isnull(C.ResourceIdInt,B.ResourceIdInt), Version, HasVersionToCompare, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, KeepHistory, RawResource, IsRawResourceMetaSet, SearchParamHash,   NULL,         NULL,           NULL
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

  IF @SingleTransaction = 0 AND isnull((SELECT Number FROM dbo.Parameters WHERE Id = 'MergeResources.NoTransaction.IsEnabled'),0) = 0
    SET @SingleTransaction = 1
  
  SET @Mode += ' ST='+convert(varchar,@SingleTransaction)

  -- perform retry check in transaction to hold locks
  IF @InitialTranCount = 0
  BEGIN
    IF EXISTS (SELECT * -- This extra statement avoids putting range locks when we don't need them
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

      --EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Start=@st,@Rows=@AffectedRows,@Text='Old rows'
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
           ( ResourceTypeId, ResourceIdInt, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash,  TransactionId, FileId, OffsetInFile, ResourceLength )
      SELECT ResourceTypeId, ResourceIdInt, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash, @TransactionId, FileId, OffsetInFile, ResourceLength
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

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@AffectedRows,@Text=@DeletedIdMap
END TRY
BEGIN CATCH
  IF @InitialTranCount = 0 AND @@trancount > 0 ROLLBACK TRANSACTION
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st

  IF error_number() IN (2601, 2627) AND error_message() LIKE '%''dbo.ResourceIdIntMap''%' -- pk violation
     OR error_number() = 547 AND error_message() LIKE '%DELETE%' -- reference violation on DELETE
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
    IF @RaiseExceptionOnConflict = 1 AND error_number() IN (2601, 2627) AND (error_message() LIKE '%''dbo.Resource%' OR error_message() LIKE '%''dbo.CurrentResources%' OR error_message() LIKE '%''dbo.HistoryResources%' OR error_message() LIKE '%''dbo.RawResources''%')
      THROW 50409, 'Resource has been recently updated or added, please compare the resource content in code for any duplicate updates', 1;
    ELSE
      THROW
END CATCH
GO

ALTER PROCEDURE dbo.UpdateResourceSearchParams
    @FailedResources int = 0 OUT
   ,@Resources dbo.ResourceList READONLY -- TODO: Remove after deployment
   ,@ResourcesLake dbo.ResourceListWithLake READONLY
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
       ,@Mode varchar(200) = isnull((SELECT 'RT=['+convert(varchar,min(ResourceTypeId))+','+convert(varchar,max(ResourceTypeId))+'] Sur=['+convert(varchar,min(ResourceSurrogateId))+','+convert(varchar,max(ResourceSurrogateId))+'] V='+convert(varchar,max(Version))+' Rows='+convert(varchar,count(*)) FROM (SELECT ResourceTypeId, ResourceSurrogateId, Version FROM @ResourcesLake UNION ALL SELECT ResourceTypeId, ResourceSurrogateId, Version FROM @Resources) A),'Input=Empty')
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

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@ResourceRows,@Text=@DeletedIdMap
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st

  IF error_number() IN (2601, 2627) AND error_message() LIKE '%''dbo.ResourceIdIntMap''%' -- pk violation
     OR error_number() = 547 AND error_message() LIKE '%DELETE%' -- reference violation on DELETE
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
GO

-- Step 3: Drop the old type
IF EXISTS (SELECT * FROM sys.types WHERE name = 'ResourceListLake' AND is_table_type = 1)
BEGIN
    DROP TYPE dbo.ResourceListLake;
END
GO
