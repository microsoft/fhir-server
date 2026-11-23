IF EXISTS (SELECT 1 FROM dbo.SearchParam)
  THROW 50000, 'This experimental migration requires an empty SearchParam table.', 1
ALTER TABLE dbo.SearchParam ADD TargetTable varchar(128) NOT NULL
ALTER TABLE dbo.SearchParam ADD CONSTRAINT CH_SearchParam_TargetTable CHECK (len(TargetTable) > 0)
GO
CREATE OR ALTER PROCEDURE dbo.CreateGranularSecondaryIndexes @TableName varchar(128), @SearchParamId smallint
WITH EXECUTE AS 'dbo'
AS
set nocount on

DECLARE @SP varchar(100) = object_name(@@procid)
       ,@st datetime = getUTCdate()
       ,@ObjectId int = object_id(@TableName)
       ,@SearchParamIdChar varchar(6) = convert(varchar,@SearchParamId)
       ,@IndexName sysname
       ,@IndexId int
       ,@Sql nvarchar(max)

DECLARE @Mode varchar(200) = 'T='+@TableName+' SP='+@SearchParamIdChar

BEGIN TRY
  IF @@trancount <> 0
    RAISERROR('Granular secondary indexes must be created outside transaction.', 18, 128)
  
  IF @ObjectId IS NULL OR @SearchParamId IS NULL OR NOT @TableName LIKE '%SearchParam'
    RAISERROR('A search parameter table and search parameter ID are required.', 18, 128)

  DECLARE @Indexes TABLE (IndexId int PRIMARY KEY, TemplateName sysname NOT NULL, IndexName sysname NOT NULL UNIQUE, Compression varchar(100), Sql nvarchar(max))
  INSERT INTO @Indexes
    SELECT I.index_id, I.name, N.IndexName, P.PropertyValue
          ,'CREATE'+CASE WHEN I.is_unique = 1 THEN ' UNIQUE' ELSE '' END +' INDEX '+N.IndexName+' ON dbo.'+@TableName+' ('+C.KeyColumns+')'
           +CASE WHEN C.IncludeColumns IS NOT NULL THEN ' INCLUDE ('+C.IncludeColumns+')' ELSE '' END
           +' WHERE SearchParamId = '+@SearchParamIdChar
           +CASE WHEN I.filter_definition IS NOT NULL THEN ' AND ('+I.filter_definition+')' ELSE '' END
           +' WITH (DATA_COMPRESSION = '+P.PropertyValue+')'
           +' ON PartitionScheme_ResourceTypeId(ResourceTypeId)'
      FROM sys.indexes I
           CROSS APPLY (SELECT IndexName = 'G_'+replace(I.name,'_SearchParamId','')+'_WHERE_SearchParam_'+@SearchParamIdChar) N
           LEFT JOIN dbo.IndexProperties P ON P.TableName = @TableName AND P.IndexName = I.name AND P.PropertyName = 'DATA_COMPRESSION'
           CROSS APPLY
           (SELECT KeyColumns = string_agg(convert(nvarchar(max),CASE WHEN IC.key_ordinal > 0 AND C.name <> 'SearchParamId' THEN C.name+CASE WHEN IC.is_descending_key = 1 THEN ' DESC' ELSE '' END END), ',') WITHIN GROUP (ORDER BY IC.key_ordinal, IC.index_column_id)
                  ,IncludeColumns = string_agg(convert(nvarchar(max),CASE WHEN IC.is_included_column = 1 THEN C.name END), ',') WITHIN GROUP (ORDER BY IC.key_ordinal, IC.index_column_id)
              FROM sys.index_columns IC JOIN sys.columns C ON C.object_id = IC.object_id AND C.column_id = IC.column_id
              WHERE IC.object_id = I.object_id AND IC.index_id = I.index_id
           ) C
      WHERE I.object_id = @ObjectId
        AND I.type = 2
        AND I.is_disabled = 1
        AND I.name NOT LIKE 'G[_]%'
        AND I.is_primary_key = 0
        AND I.is_unique_constraint = 0

  IF EXISTS (SELECT * FROM @Indexes WHERE Compression IS NULL)
  BEGIN
    SELECT TOP 1 @IndexName = TemplateName FROM @Indexes WHERE Compression IS NULL ORDER BY IndexId
    DECLARE @ErrorMessage varchar(2048) = 'Missing DATA_COMPRESSION in IndexProperties for '+@TableName+'.'+@IndexName;
    RAISERROR(@ErrorMessage, 18, 128)
  END

  IF EXISTS (SELECT * FROM @Indexes WHERE Sql IS NULL)
    RAISERROR('Could not generate SQL for granular index.', 18, 128)

  WHILE EXISTS (SELECT * FROM @Indexes)
  BEGIN
    SELECT TOP 1 @IndexId = IndexId, @IndexName = IndexName, @Sql = Sql FROM @Indexes ORDER BY IndexId
    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = @ObjectId AND name = @IndexName)
    BEGIN TRY
      EXECUTE sys.sp_executesql @Sql
      EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Run',@Action='Create Index',@Target=@TableName,@Start=@st,@Text=@Sql
    END TRY
    BEGIN CATCH
      IF error_number() <> 1913
        THROW;
      ELSE 
        EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Run',@Action='Index Exists',@Target=@TableName,@Start=@st,@Text=@Sql
    END CATCH
    DELETE FROM @Indexes WHERE IndexId = @IndexId
  END

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st
END TRY
BEGIN CATCH
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
INSERT INTO dbo.Parameters (Id, Char) SELECT 'CreateGranularSecondaryIndexes','LogEvent'
GO

CREATE OR ALTER PROCEDURE dbo.CreateAllGranularSecondaryIndexes
WITH EXECUTE AS 'dbo'
AS
set nocount on

IF @@trancount <> 0
BEGIN
  RAISERROR('Granular secondary indexes must be created outside transaction.', 18, 128)
  RETURN
END

IF EXISTS (SELECT * FROM dbo.SearchParam WHERE Status IN ('Enabled', 'Supported') AND TargetTable = 'Unsupported')
BEGIN
  RAISERROR('A searchable or supported search parameter has no supported target table.', 18, 128)
  RETURN
END

DECLARE @Targets TABLE (TableName varchar(128) NOT NULL, SearchParamId smallint NOT NULL, PRIMARY KEY (TableName, SearchParamId))

INSERT INTO @Targets (TableName, SearchParamId)
SELECT TargetTable, SearchParamId
FROM dbo.SearchParam
WHERE Status IN ('Enabled', 'Supported') AND TargetTable NOT IN ('Resource', 'VectorSearchParam')

IF EXISTS (
  SELECT * FROM @Targets T
  WHERE T.TableName NOT LIKE '%SearchParam' OR NOT EXISTS (
    SELECT * FROM sys.indexes I
    WHERE I.object_id = object_id('dbo.'+T.TableName)
      AND I.type = 2 AND I.is_disabled = 1 AND I.name NOT LIKE 'G[_]%'
      AND I.is_primary_key = 0 AND I.is_unique_constraint = 0))
BEGIN
  RAISERROR('A target table does not have a disabled index template.', 18, 128)
  RETURN
END

DECLARE @TableName varchar(128), @SearchParamId smallint
WHILE EXISTS (SELECT * FROM @Targets)
BEGIN
  SELECT TOP 1 @TableName = TableName, @SearchParamId = SearchParamId FROM @Targets ORDER BY TableName, SearchParamId
  EXECUTE dbo.CreateGranularSecondaryIndexes @TableName, @SearchParamId
  DELETE FROM @Targets WHERE TableName = @TableName AND SearchParamId = @SearchParamId
END
GO

CREATE OR ALTER PROCEDURE dbo.InitializeBase
     @searchParams nvarchar(max)
    ,@resourceTypes nvarchar(3000)
    ,@claimTypes varchar(100)
    ,@compartmentTypes varchar(100)
AS
SET XACT_ABORT ON
BEGIN TRANSACTION

INSERT INTO dbo.ResourceType (Name)
SELECT value FROM string_split(@resourceTypes, ',')
EXCEPT SELECT Name FROM dbo.ResourceType WITH (TABLOCKX);

-- result set 1
SELECT ResourceTypeId, Name FROM dbo.ResourceType;

;WITH Input AS (
    SELECT DISTINCT
            j.Uri,
            CAST(j.IsPartiallySupported AS bit) AS IsPartiallySupported,
            j.TargetTable
    FROM OPENJSON(@searchParams)
    WITH (Uri varchar(128) '$.Uri', IsPartiallySupported bit '$.IsPartiallySupported', TargetTable varchar(128) '$.TargetTable') AS j
)
INSERT dbo.SearchParam (Uri, Status, LastUpdated, IsPartiallySupported, TargetTable)
SELECT i.Uri, 'Initialized', SYSDATETIMEOFFSET(), i.IsPartiallySupported, i.TargetTable
FROM Input AS i
WHERE NOT EXISTS (SELECT 1 FROM dbo.SearchParam AS sp WHERE sp.Uri = i.Uri);

-- result set 2
SELECT Uri, SearchParamId FROM dbo.SearchParam;

INSERT INTO dbo.ClaimType (Name)
SELECT value FROM string_split(@claimTypes, ',')
EXCEPT SELECT Name FROM dbo.ClaimType;

-- result set 3
SELECT ClaimTypeId, Name FROM dbo.ClaimType;

INSERT INTO dbo.CompartmentType (Name)
SELECT value FROM string_split(@compartmentTypes, ',')
EXCEPT SELECT Name FROM dbo.CompartmentType;

-- result set 4
SELECT CompartmentTypeId, Name FROM dbo.CompartmentType;

COMMIT TRANSACTION

-- result set 5
SELECT Value, SystemId from dbo.System;

-- result set 6
SELECT Value, QuantityCodeId FROM dbo.QuantityCode
GO

CREATE OR ALTER PROCEDURE dbo.MergeSearchParams @SearchParams dbo.SearchParamList READONLY, @ReindexId bigint = NULL, @SearchParamTargets nvarchar(max) = NULL
-- @ReindexId IS NULL - not reindex. @ReindexId IS NOT NULL - reindex.
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(200) = 'Cnt='+convert(varchar,(SELECT count(*) FROM @SearchParams))+' R='+isnull(convert(varchar,@ReindexId),'NULL')
       ,@st datetime = getUTCdate()
       ,@LastUpdated datetimeoffset(7) = convert(datetimeoffset(7), sysUTCdatetime())
       ,@MaxLastUpdated datetimeoffset(7)
       ,@msg varchar(4000)
       ,@Rows int
       ,@Uri varchar(4000)
       ,@Status varchar(20)
       ,@InputTrancount int = @@trancount
       ,@ActiveJobId bigint
       ,@ExpectedLastUpdated datetimeoffset(7) = (SELECT max(LastUpdated) FROM @SearchParams)

SET @Mode = @Mode +' L='+isnull(convert(varchar(23),@ExpectedLastUpdated,126),'NULL')

BEGIN TRY
  DECLARE @SearchParamsCopy dbo.SearchParamList
  INSERT INTO @SearchParamsCopy SELECT * FROM @SearchParams
  WHILE EXISTS (SELECT * FROM @SearchParamsCopy)
  BEGIN
    SELECT TOP 1 @Uri = Uri, @Status = Status FROM @SearchParamsCopy
    SET @msg = 'Status='+@Status+' Uri='+@Uri
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Start',@Text=@msg
    DELETE FROM @SearchParamsCopy WHERE Uri = @Uri
  END

  IF @InputTrancount = 0 -- transaction can start in MergeResourcesAndSearchParams
  BEGIN
    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE

    BEGIN TRANSACTION
  END

  -- check if job id is valid
  IF @ReindexId IS NOT NULL AND @ReindexId <> 0 -- TODO: remove AND @ReindexId <> 0 after deployment
     AND NOT EXISTS (SELECT 1 FROM dbo.JobQueue WHERE PartitionId = @ReindexId % 16 AND QueueType = 6 AND JobId = @ReindexId AND Status = 1)
    RAISERROR('Reindex job is not running', 18, 127)

  IF @ReindexId IS NULL OR @ReindexId = 0 -- TODO: remove OR @ReindexId = 0 after deployment
  BEGIN
    -- Check if reindex job is running
    EXECUTE dbo.GetActiveJobs @QueueType = 6, @IsExistsCheck = 1, @GroupId = @ActiveJobId OUT
    SET @msg = 'Changes to search parameters are not allowed while a reindex job is in progress. Wait for the reindex job with Id: '+convert(varchar,@ActiveJobId)+' to finish, or cancel it.'
    IF @ActiveJobId IS NOT NULL THROW 50002, @msg, 1

    -- Check for concurrency conflicts using LastUpdated
    SET @MaxLastUpdated = (SELECT max(LastUpdated) FROM dbo.SearchParam)
    IF @MaxLastUpdated <> @ExpectedLastUpdated
    BEGIN
      SET @msg = 'Optimistic concurrency conflict detected : expected last updated = '+convert(varchar(23),@ExpectedLastUpdated,126)+' max last updated = '+convert(varchar(23),@MaxLastUpdated,126);
      THROW 50001, @msg, 1
    END
  END

  MERGE INTO dbo.SearchParam S
    USING (SELECT I.*, J.TargetTable FROM @SearchParams I LEFT JOIN OPENJSON(@SearchParamTargets) WITH (Uri varchar(128), TargetTable varchar(128)) J ON J.Uri = I.Uri) I ON I.Uri = S.Uri
    WHEN MATCHED THEN
      UPDATE
        SET Status = I.Status
           ,LastUpdated = @LastUpdated
           ,IsPartiallySupported = I.IsPartiallySupported
           ,TargetTable = coalesce(I.TargetTable, S.TargetTable)
    WHEN NOT MATCHED BY TARGET THEN
      INSERT   (  Uri,   Status,  LastUpdated,   IsPartiallySupported,   TargetTable)
        VALUES (I.Uri, I.Status, @LastUpdated, I.IsPartiallySupported, I.TargetTable);
  SET @Rows = @@rowcount

  SET @msg = 'LastUpdated='+convert(varchar(23),@LastUpdated,126)

  IF @InputTrancount = 0 COMMIT TRANSACTION

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Action='Merge',@Rows=@Rows,@Text=@msg
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO

CREATE OR ALTER PROCEDURE dbo.MergeResourcesAndSearchParams
     @SearchParams dbo.SearchParamList READONLY
    ,@SearchParamTargets nvarchar(max) = NULL
    ,@ReindexId bigint = NULL
    ,@IsResourceChangeCaptureEnabled bit = 0
    ,@TransactionId bigint = NULL
    ,@Resources dbo.ResourceList READONLY
    ,@ResourceWriteClaims dbo.ResourceWriteClaimList READONLY
    ,@ReferenceSearchParams dbo.ReferenceSearchParamList READONLY
    ,@TokenSearchParams dbo.TokenSearchParamList READONLY
    ,@TokenTexts dbo.TokenTextList READONLY
    ,@StringSearchParams dbo.StringSearchParamList READONLY
    ,@UriSearchParams dbo.UriSearchParamList READONLY
    ,@NumberSearchParams dbo.NumberSearchParamList READONLY
    ,@QuantitySearchParams dbo.QuantitySearchParamList READONLY
    ,@DateTimeSearchParms dbo.DateTimeSearchParamList READONLY
    ,@VectorSearchParams dbo.VectorSearchParamList READONLY
    ,@ReferenceTokenCompositeSearchParams dbo.ReferenceTokenCompositeSearchParamList READONLY
    ,@TokenTokenCompositeSearchParams dbo.TokenTokenCompositeSearchParamList READONLY
    ,@TokenDateTimeCompositeSearchParams dbo.TokenDateTimeCompositeSearchParamList READONLY
    ,@TokenQuantityCompositeSearchParams dbo.TokenQuantityCompositeSearchParamList READONLY
    ,@TokenStringCompositeSearchParams dbo.TokenStringCompositeSearchParamList READONLY
    ,@TokenNumberNumberCompositeSearchParams dbo.TokenNumberNumberCompositeSearchParamList READONLY
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(200) = 'R='+convert(varchar,(SELECT count(*) FROM @Resources))+' SP='+convert(varchar,(SELECT count(*) FROM @SearchParams))
       ,@st datetime = getUTCdate()
       ,@Rows int = 0

BEGIN TRY
  SET TRANSACTION ISOLATION LEVEL SERIALIZABLE

  BEGIN TRANSACTION

  EXECUTE dbo.MergeSearchParams @SearchParams, @ReindexId, @SearchParamTargets

  IF EXISTS (SELECT * FROM @Resources)
    EXECUTE dbo.MergeResources
             @AffectedRows = @Rows OUTPUT
            ,@RaiseExceptionOnConflict = 1
            ,@IsResourceChangeCaptureEnabled = @IsResourceChangeCaptureEnabled
            ,@TransactionId = @TransactionId
            ,@SingleTransaction = 1
            ,@Resources = @Resources
            ,@ResourceWriteClaims = @ResourceWriteClaims
            ,@ReferenceSearchParams = @ReferenceSearchParams
            ,@TokenSearchParams = @TokenSearchParams
            ,@TokenTexts = @TokenTexts
            ,@StringSearchParams = @StringSearchParams
            ,@UriSearchParams = @UriSearchParams
            ,@NumberSearchParams = @NumberSearchParams
            ,@QuantitySearchParams = @QuantitySearchParams
            ,@DateTimeSearchParms = @DateTimeSearchParms
            ,@VectorSearchParams = @VectorSearchParams
            ,@ReferenceTokenCompositeSearchParams = @ReferenceTokenCompositeSearchParams
            ,@TokenTokenCompositeSearchParams = @TokenTokenCompositeSearchParams
            ,@TokenDateTimeCompositeSearchParams = @TokenDateTimeCompositeSearchParams
            ,@TokenQuantityCompositeSearchParams = @TokenQuantityCompositeSearchParams
            ,@TokenStringCompositeSearchParams = @TokenStringCompositeSearchParams
            ,@TokenNumberNumberCompositeSearchParams = @TokenNumberNumberCompositeSearchParams;
  ELSE
    IF @TransactionId IS NOT NULL
      EXECUTE dbo.MergeResourcesCommitTransaction @TransactionId

  COMMIT TRANSACTION

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Action='Merge',@Rows=@Rows
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION;
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
