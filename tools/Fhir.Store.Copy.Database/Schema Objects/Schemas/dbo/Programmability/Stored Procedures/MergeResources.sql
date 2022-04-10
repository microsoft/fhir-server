--DROP PROCEDURE dbo.MergeResources 
GO
CREATE PROCEDURE dbo.MergeResources
  @SingleTransaction bit = 1
 ,@SimpleInsert bit = 1
 ,@Resources ResourceList READONLY
 ,@ReferenceSearchParams ReferenceSearchParamList READONLY
 ,@TokenSearchParams TokenSearchParamList READONLY
 ,@CompartmentAssignments CompartmentAssignmentList READONLY
 ,@TokenTexts TokenTextList READONLY
 ,@DateTimeSearchParams DateTimeSearchParamList READONLY
 ,@TokenQuantityCompositeSearchParams TokenQuantityCompositeSearchParamList READONLY
 ,@QuantitySearchParams QuantitySearchParamList READONLY
 ,@StringSearchParams StringSearchParamList READONLY
 ,@TokenTokenCompositeSearchParams TokenTokenCompositeSearchParamList READONLY
 ,@TokenStringCompositeSearchParams TokenStringCompositeSearchParamList READONLY
 ,@AffectedRows int = NULL OUT
AS
set nocount on
DECLARE @st datetime = getUTCdate()
       ,@SP varchar(100) = 'MergeResources'
       ,@ResourceTypeId smallint
       ,@InputRows int
       ,@OldRows int = 0
       ,@MaxSequence bigint
       ,@Offset bigint
       ,@DummyTop bigint = 9223372036854775807

SELECT @ResourceTypeId = max(ResourceTypeId), @InputRows = count(*) FROM @Resources -- validate whether "all resources have same RT" assumption is acually needed

DECLARE @Mode varchar(100) = 'RT='+convert(varchar,@ResourceTypeId)+' Rows='+convert(varchar,@InputRows)+' TR='+convert(varchar,@SingleTransaction)+' SI='+convert(varchar,@SimpleInsert)

SET @AffectedRows = 0

BEGIN TRY
  EXECUTE dbo.GetResourceSurrogateIdMaxSequence @Count = @InputRows, @MaxSequence = @MaxSequence OUT

  SET @Offset = @MaxSequence - @InputRows

  DECLARE @TrueResources AS TABLE
    (
       ResourceSurrogateId  bigint         NOT NULL PRIMARY KEY
      ,PreviousSurrogateId  bigint         NULL
      ,ResourceTypeId       smallint       NOT NULL
      ,ResourceId           varchar(64)    COLLATE Latin1_General_100_CS_AS NOT NULL -- Collation here should not matter as we don't do any ResourceId comparisons with @TrueResources
      ,Version              int            NOT NULL
      ,RequestMethod        varchar(10)    NULL
      ,RawResource          varbinary(max) NOT NULL
      ,IsRawResourceMetaSet bit            NOT NULL
      ,SearchParamHash      varchar(64)    NULL
    )

  DECLARE @PreviousSurrogateIds AS TABLE (SurrogateId bigint PRIMARY KEY, TypeId smallint NOT NULL)
  DECLARE @CurrentOffsets AS TABLE (Offset bigint PRIMARY KEY)

  IF @SimpleInsert = 1
    INSERT INTO @TrueResources
        (
             ResourceSurrogateId
            ,PreviousSurrogateId
            ,ResourceTypeId
            ,ResourceId
            ,Version
            ,RequestMethod
            ,RawResource
            ,IsRawResourceMetaSet
            ,SearchParamHash
        )
      SELECT A.ResourceSurrogateId
            ,PreviousSurrogateId = NULL
            ,A.ResourceTypeId
            ,A.ResourceId
            ,Version = 0
            ,A.RequestMethod
            ,A.RawResource
            ,A.IsRawResourceMetaSet
            ,A.SearchParamHash
        FROM (SELECT TOP (@DummyTop) * FROM @Resources) A
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
  ELSE
    INSERT INTO @TrueResources
        (
             ResourceSurrogateId
            ,PreviousSurrogateId
            ,ResourceTypeId
            ,ResourceId
            ,Version
            ,RequestMethod
            ,RawResource
            ,IsRawResourceMetaSet
            ,SearchParamHash
        )
      SELECT A.ResourceSurrogateId
            ,PreviousSurrogateId = B.ResourceSurrogateId
            ,A.ResourceTypeId
            ,A.ResourceId
            ,Version = isnull(B.Version + 1, 0)
            ,A.RequestMethod
            ,A.RawResource
            ,A.IsRawResourceMetaSet
            ,A.SearchParamHash
        FROM (SELECT TOP (@DummyTop) * FROM @Resources) A
             LEFT OUTER JOIN dbo.Resource B ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceId = A.ResourceId AND B.IsHistory = 0 -- How do we handle input matching deleted record?
        WHERE B.ResourceId IS NULL -- OR A.RawResource <> B.RawResource -- raw resource contains updated date and cannot be compared with input as-is. we need to fix this. 
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))

  INSERT INTO @PreviousSurrogateIds
    SELECT PreviousSurrogateId, ResourceTypeId
      FROM @TrueResources 
      WHERE PreviousSurrogateId IS NOT NULL
  SET @OldRows = @@rowcount
  
  IF @OldRows > 0
    INSERT INTO @CurrentOffsets SELECT ResourceSurrogateId FROM @TrueResources

  -- This transaction assumes that a given resource is processed only by one thread
  IF @SingleTransaction = 1 BEGIN TRANSACTION
  
  IF @OldRows > 0
  BEGIN
    UPDATE dbo.Resource
      SET IsHistory = 1
      WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
    SET @AffectedRows = @AffectedRows + @@rowcount

    DELETE FROM dbo.TokenSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
    SET @AffectedRows = @AffectedRows + @@rowcount

    DELETE FROM dbo.CompartmentAssignment WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
    SET @AffectedRows = @AffectedRows + @@rowcount

    DELETE FROM dbo.TokenText WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
    SET @AffectedRows = @AffectedRows + @@rowcount

    DELETE FROM dbo.DateTimeSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
    SET @AffectedRows = @AffectedRows + @@rowcount

    DELETE FROM dbo.TokenQuantityCompositeSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
    SET @AffectedRows = @AffectedRows + @@rowcount

    DELETE FROM dbo.QuantitySearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
    SET @AffectedRows = @AffectedRows + @@rowcount

    DELETE FROM dbo.StringSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
    SET @AffectedRows = @AffectedRows + @@rowcount

    DELETE FROM dbo.TokenTokenCompositeSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
    SET @AffectedRows = @AffectedRows + @@rowcount

    DELETE FROM dbo.TokenStringCompositeSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
    SET @AffectedRows = @AffectedRows + @@rowcount

    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Start=@st,@Rows=@AffectedRows,@Text='Old rows'
  END

  INSERT INTO dbo.Resource
          ( ResourceTypeId,          ResourceSurrogateId,ResourceId,Version,IsHistory,IsDeleted,RequestMethod,RawResource,IsRawResourceMetaSet,SearchParamHash)
    SELECT @ResourceTypeId,@Offset + ResourceSurrogateId,ResourceId,Version,        0,        0,RequestMethod,RawResource,IsRawResourceMetaSet,SearchParamHash
      FROM @TrueResources
      WHERE PreviousSurrogateId IS NULL
  SET @AffectedRows = @AffectedRows + @@rowcount

  IF @OldRows = 0 -- use simple inserts
  BEGIN
    INSERT INTO dbo.ReferenceSearchParam
            ( ResourceTypeId,          ResourceSurrogateId,SearchParamId,BaseUri,ReferenceResourceTypeId,ReferenceResourceId,ReferenceResourceVersion,IsHistory)
      SELECT @ResourceTypeId,@Offset + ResourceSurrogateId,SearchParamId,BaseUri,ReferenceResourceTypeId,ReferenceResourceId,ReferenceResourceVersion,        0
        FROM @ReferenceSearchParams
    SET @AffectedRows = @AffectedRows + @@rowcount

    INSERT INTO dbo.TokenSearchParam
            ( ResourceTypeId,          ResourceSurrogateId,SearchParamId,SystemId,Code,IsHistory)
      SELECT @ResourceTypeId,@Offset + ResourceSurrogateId,SearchParamId,SystemId,Code,        0
        FROM @TokenSearchParams
    SET @AffectedRows = @AffectedRows + @@rowcount

    INSERT INTO dbo.CompartmentAssignment
            ( ResourceTypeId,          ResourceSurrogateId,CompartmentTypeId,ReferenceResourceId,IsHistory)
      SELECT @ResourceTypeId,@Offset + ResourceSurrogateId,CompartmentTypeId,ReferenceResourceId,        0
        FROM @CompartmentAssignments
    SET @AffectedRows = @AffectedRows + @@rowcount

    INSERT INTO dbo.TokenText
            ( ResourceTypeId,          ResourceSurrogateId,SearchParamId,Text,IsHistory)
      SELECT @ResourceTypeId,@Offset + ResourceSurrogateId,SearchParamId,Text,        0
        FROM @TokenTexts
    SET @AffectedRows = @AffectedRows + @@rowcount

    INSERT INTO dbo.DateTimeSearchParam
            ( ResourceTypeId,          ResourceSurrogateId,SearchParamId,StartDateTime,EndDateTime,IsLongerThanADay,IsHistory,IsMin,IsMax)
      SELECT @ResourceTypeId,@Offset + ResourceSurrogateId,SearchParamId,StartDateTime,EndDateTime,IsLongerThanADay,        0,IsMin,IsMax
        FROM @DateTimeSearchParams
    SET @AffectedRows = @AffectedRows + @@rowcount

    INSERT INTO dbo.TokenQuantityCompositeSearchParam
            ( ResourceTypeId,          ResourceSurrogateId,SearchParamId,SystemId1,Code1,SystemId2,QuantityCodeId2,SingleValue2,LowValue2,HighValue2,IsHistory)
      SELECT @ResourceTypeId,@Offset + ResourceSurrogateId,SearchParamId,SystemId1,Code1,SystemId2,QuantityCodeId2,SingleValue2,LowValue2,HighValue2,        0
        FROM @TokenQuantityCompositeSearchParams
    SET @AffectedRows = @AffectedRows + @@rowcount

    INSERT INTO dbo.QuantitySearchParam
            ( ResourceTypeId,          ResourceSurrogateId,SearchParamId,SystemId,QuantityCodeId,SingleValue,LowValue,HighValue,IsHistory)
      SELECT @ResourceTypeId,@Offset + ResourceSurrogateId,SearchParamId,SystemId,QuantityCodeId,SingleValue,LowValue,HighValue,        0
        FROM @QuantitySearchParams
    SET @AffectedRows = @AffectedRows + @@rowcount

    INSERT INTO dbo.StringSearchParam
            ( ResourceTypeId,          ResourceSurrogateId,SearchParamId,Text,TextOverflow,IsHistory,IsMin,IsMax)
      SELECT @ResourceTypeId,@Offset + ResourceSurrogateId,SearchParamId,Text,TextOverflow,        0,IsMin,IsMax
        FROM @StringSearchParams
    SET @AffectedRows = @AffectedRows + @@rowcount

    INSERT INTO dbo.TokenTokenCompositeSearchParam
            ( ResourceTypeId,          ResourceSurrogateId,SearchParamId,SystemId1,Code1,SystemId2,Code2,IsHistory)
      SELECT @ResourceTypeId,@Offset + ResourceSurrogateId,SearchParamId,SystemId1,Code1,SystemId2,Code2,        0
        FROM @TokenTokenCompositeSearchParams
    SET @AffectedRows = @AffectedRows + @@rowcount

    INSERT INTO dbo.TokenStringCompositeSearchParam
            ( ResourceTypeId,          ResourceSurrogateId,SearchParamId,SystemId1,Code1,Text2,TextOverflow2,IsHistory)
      SELECT @ResourceTypeId,@Offset + ResourceSurrogateId,SearchParamId,SystemId1,Code1,Text2,TextOverflow2,        0
        FROM @TokenStringCompositeSearchParams
    SET @AffectedRows = @AffectedRows + @@rowcount
  END
  ELSE
  BEGIN
    INSERT INTO dbo.ReferenceSearchParam
            (ResourceTypeId,          ResourceSurrogateId,SearchParamId,BaseUri,ReferenceResourceTypeId,ReferenceResourceId,ReferenceResourceVersion,IsHistory)
      SELECT ResourceTypeId,@Offset + ResourceSurrogateId,SearchParamId,BaseUri,ReferenceResourceTypeId,ReferenceResourceId,ReferenceResourceVersion,        0
        FROM @ReferenceSearchParams
        WHERE EXISTS (SELECT * FROM @CurrentOffsets WHERE Offset = ResourceSurrogateId)
    SET @AffectedRows = @AffectedRows + @@rowcount

    INSERT INTO dbo.TokenSearchParam
            (  ResourceTypeId,          ResourceSurrogateId,SearchParamId,SystemId,Code,IsHistory)
      SELECT ResourceTypeId,@Offset + ResourceSurrogateId,SearchParamId,SystemId,Code,        0
        FROM @TokenSearchParams
        WHERE EXISTS (SELECT * FROM @CurrentOffsets WHERE Offset = ResourceSurrogateId)
    SET @AffectedRows = @AffectedRows + @@rowcount

    INSERT INTO dbo.CompartmentAssignment
            (ResourceTypeId,          ResourceSurrogateId,CompartmentTypeId,ReferenceResourceId,IsHistory)
      SELECT ResourceTypeId,@Offset + ResourceSurrogateId,CompartmentTypeId,ReferenceResourceId,        0
        FROM @CompartmentAssignments
        WHERE EXISTS (SELECT * FROM @CurrentOffsets WHERE Offset = ResourceSurrogateId)
    SET @AffectedRows = @AffectedRows + @@rowcount

    INSERT INTO dbo.TokenText
            (ResourceTypeId,          ResourceSurrogateId,SearchParamId,Text,IsHistory)
      SELECT ResourceTypeId,@Offset + ResourceSurrogateId,SearchParamId,Text,        0
        FROM @TokenTexts
        WHERE EXISTS (SELECT * FROM @CurrentOffsets WHERE Offset = ResourceSurrogateId)
    SET @AffectedRows = @AffectedRows + @@rowcount

    INSERT INTO dbo.DateTimeSearchParam
            (ResourceTypeId,          ResourceSurrogateId,SearchParamId,StartDateTime,EndDateTime,IsLongerThanADay,IsHistory,IsMin,IsMax)
      SELECT ResourceTypeId,@Offset + ResourceSurrogateId,SearchParamId,StartDateTime,EndDateTime,IsLongerThanADay,        0,IsMin,IsMax
        FROM @DateTimeSearchParams
        WHERE EXISTS (SELECT * FROM @CurrentOffsets WHERE Offset = ResourceSurrogateId)
    SET @AffectedRows = @AffectedRows + @@rowcount

    INSERT INTO dbo.TokenQuantityCompositeSearchParam
            (ResourceTypeId,          ResourceSurrogateId,SearchParamId,SystemId1,Code1,SystemId2,QuantityCodeId2,SingleValue2,LowValue2,HighValue2,IsHistory)
      SELECT ResourceTypeId,@Offset + ResourceSurrogateId,SearchParamId,SystemId1,Code1,SystemId2,QuantityCodeId2,SingleValue2,LowValue2,HighValue2,        0
        FROM @TokenQuantityCompositeSearchParams
        WHERE EXISTS (SELECT * FROM @CurrentOffsets WHERE Offset = ResourceSurrogateId)
    SET @AffectedRows = @AffectedRows + @@rowcount

    INSERT INTO dbo.QuantitySearchParam
            (ResourceTypeId,          ResourceSurrogateId,SearchParamId,SystemId,QuantityCodeId,SingleValue,LowValue,HighValue,IsHistory)
      SELECT ResourceTypeId,@Offset + ResourceSurrogateId,SearchParamId,SystemId,QuantityCodeId,SingleValue,LowValue,HighValue,        0
        FROM @QuantitySearchParams
        WHERE EXISTS (SELECT * FROM @CurrentOffsets WHERE Offset = ResourceSurrogateId)
    SET @AffectedRows = @AffectedRows + @@rowcount

    INSERT INTO dbo.StringSearchParam
            (ResourceTypeId,          ResourceSurrogateId,SearchParamId,Text,TextOverflow,IsHistory,IsMin,IsMax)
      SELECT ResourceTypeId,@Offset + ResourceSurrogateId,SearchParamId,Text,TextOverflow,        0,IsMin,IsMax
        FROM @StringSearchParams
        WHERE EXISTS (SELECT * FROM @CurrentOffsets WHERE Offset = ResourceSurrogateId)
    SET @AffectedRows = @AffectedRows + @@rowcount

    INSERT INTO dbo.TokenTokenCompositeSearchParam
            (ResourceTypeId,          ResourceSurrogateId,SearchParamId,SystemId1,Code1,SystemId2,Code2,IsHistory)
      SELECT ResourceTypeId,@Offset + ResourceSurrogateId,SearchParamId,SystemId1,Code1,SystemId2,Code2,        0
        FROM @TokenTokenCompositeSearchParams
        WHERE EXISTS (SELECT * FROM @CurrentOffsets WHERE Offset = ResourceSurrogateId)
    SET @AffectedRows = @AffectedRows + @@rowcount

    INSERT INTO dbo.TokenStringCompositeSearchParam
            (ResourceTypeId,          ResourceSurrogateId,SearchParamId,SystemId1,Code1,Text2,TextOverflow2,IsHistory)
      SELECT ResourceTypeId,@Offset + ResourceSurrogateId,SearchParamId,SystemId1,Code1,Text2,TextOverflow2,        0
        FROM @TokenStringCompositeSearchParams
        WHERE EXISTS (SELECT * FROM @CurrentOffsets WHERE Offset = ResourceSurrogateId)
    SET @AffectedRows = @AffectedRows + @@rowcount
  END

  IF @SingleTransaction = 1 COMMIT TRANSACTION

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@AffectedRows,@Text=@OldRows
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st
END CATCH
GO
