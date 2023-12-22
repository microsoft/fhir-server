ALTER PROCEDURE dbo.SwitchPartitionsIn @Tbl varchar(100), @OneResourceTypeId smallint = NULL, @InTbl varchar(100) = NULL
WITH EXECUTE AS 'dbo'
AS
set nocount on
DECLARE @SP varchar(100) = 'SwitchPartitionsIn'
       ,@Mode varchar(200) = 'Tbl='+isnull(@Tbl,'NULL')+' ORT='+isnull(convert(varchar,@OneResourceTypeId),'NULL')+' InTbl='+isnull(@InTbl,'NULL')
       ,@st datetime = getUTCdate()
       ,@ResourceTypeId smallint
       ,@Rows bigint
       ,@Txt varchar(1000)
       ,@TblInt varchar(100)
       ,@Ind varchar(200)
       ,@IndId int
       ,@DataComp varchar(100)

DECLARE @Indexes TABLE (IndId int PRIMARY KEY, name varchar(200))
DECLARE @ResourceTypes TABLE (ResourceTypeId smallint PRIMARY KEY)

BEGIN TRY
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Start'

  IF @Tbl IS NULL RAISERROR('@Tbl IS NULL', 18, 127)

  SET @InTbl = isnull(@InTbl,@Tbl)

  -- Partitioned table should be either empty, then rebuilt indexes, or all indexes are already built
  INSERT INTO @Indexes SELECT index_id, name FROM sys.indexes WHERE object_id = object_id(@InTbl) AND is_disabled = 1
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target='@Indexes',@Action='Insert',@Rows=@@rowcount

  WHILE EXISTS (SELECT * FROM @Indexes) -- This is irrelevant if @InTbl is specified
  BEGIN
    SELECT TOP 1 @IndId = IndId, @Ind = name FROM @Indexes ORDER BY IndId

    SET @DataComp = CASE WHEN (SELECT PropertyValue FROM dbo.IndexProperties WHERE TableName=@Tbl AND IndexName=@Ind)='PAGE' THEN ' PARTITION = ALL WITH (DATA_COMPRESSION = PAGE)' ELSE '' END
    SET @Txt = 'IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('''+@Tbl+''') AND name = '''+@Ind+''' AND is_disabled = 1) ALTER INDEX '+@Ind+' ON dbo.'+@Tbl+' REBUILD'+@DataComp
    EXECUTE(@Txt)
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@Ind,@Action='Rebuild',@Text=@Txt

    DELETE FROM @Indexes WHERE IndId = @IndId
  END

  INSERT INTO @ResourceTypes
    SELECT ResourceTypeId
      FROM (SELECT ResourceTypeId = convert(smallint,reverse(substring(revName,1,charindex('_',revName)-1))) -- break by last _
              FROM (SELECT *, revName = reverse(name) FROM sys.objects) O
              WHERE name LIKE @Tbl+'[_]%[0-9]'
                AND (EXISTS (SELECT * FROM sysindexes WHERE id = O.object_id AND indid IN (0,1) AND rows > 0) OR @OneResourceTypeId IS NOT NULL)
           ) A
      WHERE @OneResourceTypeId IS NULL OR @OneResourceTypeId = ResourceTypeId
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target='#ResourceTypes',@Action='Select Into',@Rows=@@rowcount

  WHILE EXISTS (SELECT * FROM @ResourceTypes)
  BEGIN
    SET @ResourceTypeId = (SELECT TOP 1 ResourceTypeId FROM @ResourceTypes)
    SET @TblInt = @Tbl+'_'+convert(varchar,@ResourceTypeId)
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@TblInt

    SET @Txt = 'ALTER TABLE dbo.'+@TblInt+' SWITCH TO dbo.'+@InTbl+' PARTITION $partition.PartitionFunction_ResourceTypeId('+convert(varchar,@ResourceTypeId)+')'
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@InTbl,@Action='Switch in start',@Text=@Txt
    EXECUTE(@Txt)
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@InTbl,@Action='Switch in',@Text=@Txt

    -- Sanity check
    IF EXISTS (SELECT * FROM sysindexes WHERE id = object_id(@TblInt) AND rows > 0)
    BEGIN
      SET @Txt = @TblInt+' is not empty after switch'
      RAISERROR(@Txt, 18, 127)
    END

    EXECUTE('DROP TABLE dbo.'+@TblInt)
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@TblInt,@Action='Drop'

    DELETE FROM @ResourceTypes WHERE ResourceTypeId = @ResourceTypeId
  END

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
ALTER PROCEDURE dbo.SwitchPartitionsOut @Tbl varchar(100), @RebuildClustered bit = 0, @OneResourceTypeId smallint = NULL, @OutTbl varchar(100) = NULL
WITH EXECUTE AS 'dbo'
AS
set nocount on
DECLARE @SP varchar(100) = 'SwitchPartitionsOut'
       ,@Mode varchar(200) = 'Tbl='+isnull(@Tbl,'NULL')+' ND='+isnull(convert(varchar,@RebuildClustered),'NULL')+' ORT='+isnull(convert(varchar,@OneResourceTypeId),'NULL')+' OutTbl='+isnull(@OutTbl,'NULL')
       ,@st datetime = getUTCdate()
       ,@ResourceTypeId smallint
       ,@Rows bigint
       ,@Txt varchar(max)
       ,@TblInt varchar(100)
       ,@IndId int
       ,@Ind varchar(200)
       ,@Name varchar(100)
       ,@CheckName varchar(200)
       ,@CheckDefinition varchar(200)

DECLARE @Indexes TABLE (IndId int PRIMARY KEY, name varchar(200), IsDisabled bit)
DECLARE @IndexesRT TABLE (IndId int PRIMARY KEY, name varchar(200), IsDisabled bit)
DECLARE @ResourceTypes TABLE (ResourceTypeId smallint PRIMARY KEY, partition_number_roundtrip int, partition_number int, row_count bigint)
DECLARE @Names TABLE (name varchar(100) PRIMARY KEY)
DECLARE @CheckConstraints TABLE (CheckName varchar(200), CheckDefinition varchar(200))

BEGIN TRY
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Start'

  IF @Tbl IS NULL RAISERROR('@Tbl IS NULL', 18, 127)
  IF @RebuildClustered IS NULL RAISERROR('@RebuildClustered IS NULL', 18, 127)

  INSERT INTO @Indexes SELECT index_id, name, is_disabled FROM sys.indexes WHERE object_id = object_id(@Tbl) AND (is_disabled = 0 OR @RebuildClustered = 1)
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target='@Indexes',@Action='Insert',@Rows=@@rowcount

  INSERT INTO @ResourceTypes 
    SELECT ResourceTypeId = partition_number - 1
          ,partition_number_roundtrip = $partition.PartitionFunction_ResourceTypeId(partition_number - 1)
          ,partition_number
          ,row_count
      FROM sys.dm_db_partition_stats 
      WHERE object_id = object_id(@Tbl) 
        AND index_id = 1
        AND (row_count > 0 OR @OneResourceTypeId IS NOT NULL)
        AND (@OneResourceTypeId IS NULL OR @OneResourceTypeId = partition_number - 1)
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target='@ResourceTypes',@Action='Insert',@Rows=@@rowcount,@Text='For partition switch'

  -- Sanity check
  IF EXISTS (SELECT * FROM @ResourceTypes WHERE partition_number_roundtrip <> partition_number) RAISERROR('Partition sanity check failed', 18, 127)

  WHILE EXISTS (SELECT * FROM @ResourceTypes)
  BEGIN
    SELECT TOP 1 @ResourceTypeId = ResourceTypeId, @Rows = row_count FROM @ResourceTypes ORDER BY ResourceTypeId
    SET @TblInt = isnull(@OutTbl,@Tbl)+'_'+convert(varchar,@ResourceTypeId)
    SET @Txt = 'Starting @ResourceTypeId='+convert(varchar,@ResourceTypeId)+' row_count='+convert(varchar,@Rows)
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Text=@Txt

    IF NOT EXISTS (SELECT * FROM sysindexes WHERE id = object_id(@TblInt) AND rows > 0)
    BEGIN
      IF object_id(@TblInt) IS NOT NULL
      BEGIN
        EXECUTE('DROP TABLE dbo.'+@TblInt)
        EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@TblInt,@Action='Drop'
      END

      EXECUTE('SELECT * INTO dbo.'+@TblInt+' FROM dbo.'+@Tbl+' WHERE 1 = 2')
      EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@TblInt,@Action='Select Into',@Rows=@@rowcount

      DELETE FROM @CheckConstraints     
      INSERT INTO @CheckConstraints SELECT name, definition FROM sys.check_constraints WHERE parent_object_id = object_id(@Tbl) 
      WHILE EXISTS (SELECT * FROM @CheckConstraints)
      BEGIN
        SELECT TOP 1 @CheckName = CheckName, @CheckDefinition = CheckDefinition FROM @CheckConstraints
        SET @Txt = 'ALTER TABLE '+@TblInt+' ADD CONSTRAINT '+replace(@CheckName,@Tbl,@TblInt)+' CHECK '+@CheckDefinition
        EXECUTE(@Txt)
        EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@TblInt,@Action='ALTER',@Text=@Txt

        DELETE FROM @CheckConstraints WHERE CheckName=@CheckName
      END

      DELETE FROM @Names
      INSERT INTO @Names SELECT name FROM sys.columns WHERE object_id = object_id(@Tbl) AND is_sparse = 1
      WHILE EXISTS (SELECT * FROM @Names) 
      BEGIN
        SET @Name = (SELECT TOP 1 name FROM @Names ORDER BY name)

        -- This is not generic but works for old and current schema
        SET @Txt = (SELECT 'ALTER TABLE dbo.'+@TblInt+' ALTER COLUMN '+@Name+' '+T.name+'('+convert(varchar,C.precision)+','+convert(varchar,C.scale)+') SPARSE NULL'
                      FROM sys.types T JOIN sys.columns C ON C.system_type_id = T.system_type_id
                      WHERE C.object_id = object_id(@Tbl)
                        AND C.name = @Name
                   )
        EXECUTE(@Txt)
        EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@TblInt,@Action='ALTER',@Text=@Txt

        DELETE FROM @Names WHERE name = @Name
      END
    END

    -- Create all indexes/pks, exclude disabled 
    INSERT INTO @IndexesRT SELECT * FROM @Indexes WHERE IsDisabled = 0
    WHILE EXISTS (SELECT * FROM @IndexesRT)
    BEGIN
      SELECT TOP 1 @IndId = IndId, @Ind = name FROM @IndexesRT ORDER BY IndId

      IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id(@TblInt) AND name = @Ind)
      BEGIN 
        EXECUTE dbo.GetIndexCommands @Tbl = @Tbl, @Ind = @Ind, @AddPartClause = 0, @IncludeClustered = 1, @Txt = @Txt OUT

        SET @Txt = replace(@Txt,'['+@Tbl+']',@TblInt)
        EXECUTE(@Txt)
        EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@TblInt,@Action='Create Index',@Text=@Txt
      END

      DELETE FROM @IndexesRT WHERE IndId = @IndId
    END

    SET @Txt = 'ALTER TABLE dbo.'+@TblInt+' ADD CONSTRAINT CHK_'+@TblInt+'_ResourceTypeId_'+convert(varchar,@ResourceTypeId)+' CHECK (ResourceTypeId >= '+convert(varchar,@ResourceTypeId)+' AND ResourceTypeId < '+convert(varchar,@ResourceTypeId)+' + 1)' -- this matches partition function
    EXECUTE(@Txt)
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@Tbl,@Action='Add check',@Text=@Txt

    -- Switch out
    SET @Txt = 'ALTER TABLE dbo.'+@Tbl+' SWITCH PARTITION $partition.PartitionFunction_ResourceTypeId('+convert(varchar,@ResourceTypeId)+') TO dbo.'+@TblInt
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@Tbl,@Action='Switch out start',@Text=@Txt
    EXECUTE(@Txt)
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@Tbl,@Action='Switch out end',@Text=@Txt

    DELETE FROM @ResourceTypes WHERE ResourceTypeId = @ResourceTypeId
  END

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
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
  DELETE FROM A FROM dbo.ResourceWriteClaim A WHERE EXISTS (SELECT * FROM @Ids B WHERE B.ResourceSurrogateId = A.ResourceSurrogateId)
  DELETE FROM A FROM dbo.ReferenceSearchParam A WHERE EXISTS (SELECT * FROM @Ids B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId)
  DELETE FROM A FROM dbo.TokenSearchParamHighCard A WHERE EXISTS (SELECT * FROM @Ids B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId)
  DELETE FROM A FROM dbo.TokenSearchParam A WHERE EXISTS (SELECT * FROM @Ids B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId)
  DELETE FROM A FROM dbo.TokenText A WHERE EXISTS (SELECT * FROM @Ids B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId)
  DELETE FROM A FROM dbo.StringSearchParam A WHERE EXISTS (SELECT * FROM @Ids B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId)
  DELETE FROM A FROM dbo.UriSearchParam A WHERE EXISTS (SELECT * FROM @Ids B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId)
  DELETE FROM A FROM dbo.NumberSearchParam A WHERE EXISTS (SELECT * FROM @Ids B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId)
  DELETE FROM A FROM dbo.QuantitySearchParam A WHERE EXISTS (SELECT * FROM @Ids B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId)
  DELETE FROM A FROM dbo.DateTimeSearchParam A WHERE EXISTS (SELECT * FROM @Ids B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId)
  DELETE FROM A FROM dbo.ReferenceTokenCompositeSearchParam A WHERE EXISTS (SELECT * FROM @Ids B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId)
  DELETE FROM A FROM dbo.TokenTokenCompositeSearchParam A WHERE EXISTS (SELECT * FROM @Ids B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId)
  DELETE FROM A FROM dbo.TokenDateTimeCompositeSearchParam A WHERE EXISTS (SELECT * FROM @Ids B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId)
  DELETE FROM A FROM dbo.TokenQuantityCompositeSearchParam A WHERE EXISTS (SELECT * FROM @Ids B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId)
  DELETE FROM A FROM dbo.TokenStringCompositeSearchParam A WHERE EXISTS (SELECT * FROM @Ids B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId)
  DELETE FROM A FROM dbo.TokenNumberNumberCompositeSearchParam A WHERE EXISTS (SELECT * FROM @Ids B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId)

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
    DELETE FROM dbo.ResourceWriteClaim WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds) OPTION (MAXDOP 1)
    DELETE FROM dbo.ReferenceSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds) OPTION (MAXDOP 1)
    DELETE FROM dbo.TokenSearchParamHighCard WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds) OPTION (MAXDOP 1)
    DELETE FROM dbo.TokenSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds) OPTION (MAXDOP 1)
    DELETE FROM dbo.TokenText WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds) OPTION (MAXDOP 1)
    DELETE FROM dbo.StringSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds) OPTION (MAXDOP 1)
    DELETE FROM dbo.UriSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds) OPTION (MAXDOP 1)
    DELETE FROM dbo.NumberSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds) OPTION (MAXDOP 1)
    DELETE FROM dbo.QuantitySearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds) OPTION (MAXDOP 1)
    DELETE FROM dbo.DateTimeSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds) OPTION (MAXDOP 1)
    DELETE FROM dbo.ReferenceTokenCompositeSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds) OPTION (MAXDOP 1)
    DELETE FROM dbo.TokenTokenCompositeSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds) OPTION (MAXDOP 1)
    DELETE FROM dbo.TokenDateTimeCompositeSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds) OPTION (MAXDOP 1)
    DELETE FROM dbo.TokenQuantityCompositeSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds) OPTION (MAXDOP 1)
    DELETE FROM dbo.TokenStringCompositeSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds) OPTION (MAXDOP 1)
    DELETE FROM dbo.TokenNumberNumberCompositeSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds) OPTION (MAXDOP 1)
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
IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('StringSearchParam') AND name = 'IX_StringSearchParam_SearchParamId_TextWithOverflow')
  DROP INDEX IX_StringSearchParam_SearchParamId_TextWithOverflow ON dbo.StringSearchParam
GO
IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('StringSearchParam') AND name = 'IXC_StringSearchParam')
  EXECUTE sp_rename 'StringSearchParam.IXC_StringSearchParam','IXC_ResourceTypeId_ResourceSurrogateId_SearchParamId'
GO
IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('StringSearchParam') AND name = 'IX_StringSearchParam_SearchParamId_Text')
  EXECUTE sp_rename 'StringSearchParam.IX_StringSearchParam_SearchParamId_Text','IX_ResourceTypeId_SearchParamId_Text_ResourceSurrogateId_INCLUDE_TextOverflow_IsMin_IsMax_WHERE_IsHistory_0'
GO
IF EXISTS (SELECT * FROM sys.objects WHERE type = 'u' AND name = 'StringSearchParam')
  EXECUTE sp_rename 'StringSearchParam', 'StringSearchParam_Partitioned'
GO
IF EXISTS (SELECT * FROM sys.objects WHERE type = 'v' AND name = 'StringSearchParam')
  DROP VIEW dbo.StringSearchParam
GO
IF object_id('tempdb..#RTs') IS NOT NULL DROP TABLE #RTs
GO
DECLARE @Template varchar(max) = '
IF object_id(''StringSearchParam_XXX'') IS NULL
  CREATE TABLE dbo.StringSearchParam_XXX
  (
      ResourceTypeId       smallint      NOT NULL
     ,ResourceSurrogateId  bigint        NOT NULL
     ,SearchParamId        smallint      NOT NULL
     ,Text                 nvarchar(256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL 
     ,TextOverflow         nvarchar(max) COLLATE Latin1_General_100_CI_AI_SC NULL
     ,IsHistory            bit           NOT NULL DEFAULT 0
     ,IsMin                bit           NOT NULL
     ,IsMax                bit           NOT NULL

     ,CONSTRAINT CHK_StringSearchParam_XXX_ResourceTypeId_XXX CHECK (ResourceTypeId = XXX)
  )

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id(''StringSearchParam_XXX'') AND name = ''IXC_ResourceTypeId_ResourceSurrogateId_SearchParamId'')
  CREATE CLUSTERED INDEX IXC_ResourceTypeId_ResourceSurrogateId_SearchParamId ON dbo.StringSearchParam_XXX (ResourceTypeId, ResourceSurrogateId, SearchParamId) 
    WITH (DATA_COMPRESSION = PAGE)

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id(''StringSearchParam_XXX'') AND name = ''IX_ResourceTypeId_SearchParamId_Text_ResourceSurrogateId_INCLUDE_TextOverflow_IsMin_IsMax_WHERE_IsHistory_0'')
  CREATE INDEX IX_ResourceTypeId_SearchParamId_Text_ResourceSurrogateId_INCLUDE_TextOverflow_IsMin_IsMax_WHERE_IsHistory_0 
    ON dbo.StringSearchParam_XXX (ResourceTypeId, SearchParamId, Text, ResourceSurrogateId) INCLUDE (TextOverflow, IsMin, IsMax) WHERE IsHistory = 0
    WITH (DATA_COMPRESSION = PAGE)'
       ,@CreateTable varchar(max)
       ,@CreateView varchar(max) = '
CREATE VIEW dbo.StringSearchParam
AS
SELECT ResourceTypeId,ResourceSurrogateId,SearchParamId,Text,TextOverflow,IsMin,IsMax,IsHistory = convert(bit,0) FROM dbo.StringSearchParam_Partitioned'
       ,@InsertTrigger varchar(max) = '
CREATE TRIGGER dbo.StringSearchParamIns ON dbo.StringSearchParam INSTEAD OF INSERT
AS
set nocount on
INSERT INTO dbo.StringSearchParam_Partitioned 
        (ResourceTypeId,ResourceSurrogateId,SearchParamId,Text,TextOverflow,IsMin,IsMax) 
  SELECT ResourceTypeId,ResourceSurrogateId,SearchParamId,Text,TextOverflow,IsMin,IsMax 
    FROM Inserted 
    WHERE ResourceTypeId NOT IN (4,14,15,19,28,35,40,44,53,61,62,76,79,96,100,103,108,110,138)'
       ,@DeleteTrigger varchar(max) = '
CREATE TRIGGER dbo.StringSearchParamDel ON dbo.StringSearchParam INSTEAD OF DELETE
AS
set nocount on
DELETE FROM dbo.StringSearchParam_Partitioned WHERE EXISTS (SELECT * FROM (SELECT T = ResourceTypeId, S = ResourceSurrogateId FROM Deleted) A WHERE T = ResourceTypeId AND S = ResourceSurrogateId)'

SELECT RT
  INTO #RTs
  FROM (
SELECT RT = 4
UNION SELECT 14
UNION SELECT 15
UNION SELECT 19
UNION SELECT 28
UNION SELECT 35
UNION SELECT 40
UNION SELECT 44
UNION SELECT 53
UNION SELECT 61
UNION SELECT 62
UNION SELECT 76
UNION SELECT 79
UNION SELECT 96
UNION SELECT 100
UNION SELECT 103
UNION SELECT 108
UNION SELECT 110
UNION SELECT 138
      ) A

DECLARE @RT varchar(100)
WHILE EXISTS (SELECT * FROM #RTs)
BEGIN
  SET @RT = (SELECT TOP 1 RT FROM #RTs)
  SET @CreateTable = @Template
  SET @CreateTable = replace(@CreateTable,'XXX',@RT)
  --PRINT @CreateTable
  EXECUTE(@CreateTable)
  
  SET @CreateView = @CreateView + '
UNION ALL SELECT ResourceTypeId,ResourceSurrogateId,SearchParamId,Text,TextOverflow,IsMin,IsMax,IsHistory = convert(bit,0) FROM dbo.StringSearchParam_'+@RT

  SET @InsertTrigger = @InsertTrigger + '
INSERT INTO dbo.StringSearchParam_'+@RT+' 
        (ResourceTypeId,ResourceSurrogateId,SearchParamId,Text,TextOverflow,IsMin,IsMax) 
  SELECT ResourceTypeId,ResourceSurrogateId,SearchParamId,Text,TextOverflow,IsMin,IsMax 
    FROM Inserted 
    WHERE ResourceTypeId = '+@RT

  SET @DeleteTrigger = @DeleteTrigger + '
DELETE FROM dbo.StringSearchParam_'+@RT+' WHERE ResourceTypeId = '+@RT+' AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM Deleted WHERE ResourceTypeId = '+@RT+')'

  DELETE FROM #RTs WHERE RT = @RT
END

--PRINT @CreateView
EXECUTE(@CreateView)
EXECUTE(@InsertTrigger)
EXECUTE(@DeleteTrigger)
GO
IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenSearchParam') AND name = 'IXC_TokenSearchParam')
  EXECUTE sp_rename 'TokenSearchParam.IXC_TokenSearchParam','IXC_ResourceTypeId_ResourceSurrogateId_SearchParamId'
GO
IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenSearchParam') AND name = 'IX_TokenSeachParam_SearchParamId_Code_SystemId')
  EXECUTE sp_rename 'TokenSearchParam.IX_TokenSeachParam_SearchParamId_Code_SystemId','IX_ResourceTypeId_SearchParamId_Code_ResourceSurrogateId_INCLUDE_SystemId_WHERE_IsHistory_0'
GO
IF EXISTS (SELECT * FROM sys.objects WHERE type = 'u' AND name = 'TokenSearchParam')
  EXECUTE sp_rename 'TokenSearchParam', 'TokenSearchParam_Partitioned'
GO
IF EXISTS (SELECT * FROM sys.objects WHERE type = 'v' AND name = 'TokenSearchParam')
  DROP VIEW dbo.TokenSearchParam
GO
IF object_id('tempdb..#RTs') IS NOT NULL DROP TABLE #RTs
GO
DECLARE @Template varchar(max) = '
IF object_id(''TokenSearchParam_XXX'') IS NULL
  CREATE TABLE dbo.TokenSearchParam_XXX
  (
      ResourceTypeId       smallint     NOT NULL
     ,ResourceSurrogateId  bigint       NOT NULL
     ,SearchParamId        smallint     NOT NULL
     ,SystemId             int          NULL
     ,Code                 varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL
     ,IsHistory            bit          NOT NULL DEFAULT 0
     ,CodeOverflow         varchar(max) COLLATE Latin1_General_100_CS_AS NULL
   
     ,CONSTRAINT CHK_TokenSearchParam_XXX_CodeOverflow CHECK (len(Code) = 256 OR CodeOverflow IS NULL)
     ,CONSTRAINT CHK_TokenSearchParam_XXX_ResourceTypeId_XXX CHECK (ResourceTypeId = XXX)
  )

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id(''TokenSearchParam_XXX'') AND name = ''IXC_ResourceSurrogateId_SearchParamId'')
  CREATE CLUSTERED INDEX IXC_ResourceSurrogateId_SearchParamId ON dbo.TokenSearchParam_XXX (ResourceSurrogateId, SearchParamId) 
    WITH (DATA_COMPRESSION = PAGE)

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id(''TokenSearchParam_XXX'') AND name = ''IX_ResourceTypeId_SearchParamId_Code_ResourceSurrogateId_INCLUDE_SystemId_WHERE_IsHistory_0'')
  CREATE INDEX IX_ResourceTypeId_SearchParamId_Code_ResourceSurrogateId_INCLUDE_SystemId_WHERE_IsHistory_0
    ON dbo.TokenSearchParam_XXX (ResourceTypeId, SearchParamId, Code, ResourceSurrogateId) INCLUDE (SystemId) WHERE IsHistory = 0
    WITH (DATA_COMPRESSION = PAGE)'
       ,@CreateTable varchar(max)
       ,@CreateView varchar(max) = '
CREATE VIEW dbo.TokenSearchParam
AS
SELECT ResourceTypeId,ResourceSurrogateId,SearchParamId,SystemId,Code,CodeOverflow, IsHistory = convert(bit,0) FROM dbo.TokenSearchParam_Partitioned'
       ,@InsertTrigger varchar(max) = '
CREATE TRIGGER dbo.TokenSearchParamIns ON dbo.TokenSearchParam INSTEAD OF INSERT
AS
set nocount on
INSERT INTO dbo.TokenSearchParam_Partitioned 
        (ResourceTypeId,ResourceSurrogateId,SearchParamId,SystemId,Code,CodeOverflow) 
  SELECT ResourceTypeId,ResourceSurrogateId,SearchParamId,SystemId,Code,CodeOverflow
    FROM Inserted 
    WHERE ResourceTypeId NOT IN (4,14,15,19,28,35,40,44,53,61,62,76,79,96,100,103,108,110,138)'
       ,@DeleteTrigger varchar(max) = '
CREATE TRIGGER dbo.TokenSearchParamDel ON dbo.TokenSearchParam INSTEAD OF DELETE
AS
set nocount on
DELETE FROM dbo.TokenSearchParam_Partitioned WHERE EXISTS (SELECT * FROM (SELECT T = ResourceTypeId, S = ResourceSurrogateId FROM Deleted) A WHERE T = ResourceTypeId AND S = ResourceSurrogateId)'

SELECT RT
  INTO #RTs
  FROM (
SELECT RT = 4
UNION SELECT 14
UNION SELECT 15
UNION SELECT 19
UNION SELECT 28
UNION SELECT 35
UNION SELECT 40
UNION SELECT 44
UNION SELECT 53
UNION SELECT 61
UNION SELECT 62
UNION SELECT 76
UNION SELECT 79
UNION SELECT 96
UNION SELECT 100
UNION SELECT 103
UNION SELECT 108
UNION SELECT 110
UNION SELECT 138
      ) A

DECLARE @RT varchar(100)
WHILE EXISTS (SELECT * FROM #RTs)
BEGIN
  SET @RT = (SELECT TOP 1 RT FROM #RTs)
  SET @CreateTable = @Template
  SET @CreateTable = replace(@CreateTable,'XXX',@RT)
  --PRINT @CreateTable
  EXECUTE(@CreateTable)
  
  SET @CreateView = @CreateView + '
UNION ALL SELECT ResourceTypeId,ResourceSurrogateId,SearchParamId,SystemId,Code,CodeOverflow, IsHistory = convert(bit,0) FROM dbo.TokenSearchParam_'+@RT

  SET @InsertTrigger = @InsertTrigger + '
INSERT INTO dbo.TokenSearchParam_'+@RT+' 
        (ResourceTypeId,ResourceSurrogateId,SearchParamId,SystemId,Code,CodeOverflow) 
  SELECT ResourceTypeId,ResourceSurrogateId,SearchParamId,SystemId,Code,CodeOverflow 
    FROM Inserted 
    WHERE ResourceTypeId = '+@RT

  SET @DeleteTrigger = @DeleteTrigger + '
DELETE FROM dbo.TokenSearchParam_'+@RT+' WHERE ResourceTypeId = '+@RT+' AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM Deleted WHERE ResourceTypeId = '+@RT+')'

  DELETE FROM #RTs WHERE RT = @RT
END

--PRINT @CreateView
EXECUTE(@CreateView)
EXECUTE(@InsertTrigger)
EXECUTE(@DeleteTrigger)
GO
