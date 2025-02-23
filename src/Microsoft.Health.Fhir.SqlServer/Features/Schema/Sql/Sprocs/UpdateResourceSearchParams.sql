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
