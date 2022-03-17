--DROP PROCEDURE dbo.InsertResources 
GO
CREATE PROCEDURE dbo.InsertResources 
  @Resources ResourceList READONLY
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
       ,@SP varchar(100) = 'InsertResources'
       ,@Mode varchar(100) = (SELECT 'RT='+convert(varchar,min(ResourceTypeId))
                                    +' MinR='+convert(varchar,min(ResourceSurrogateId))
                                    +' MaxR='+convert(varchar,max(ResourceSurrogateId))
                                    +' Cnt='+convert(varchar,count(*)) 
                                FROM @Resources)

SET @AffectedRows = 0

BEGIN TRY
  INSERT INTO dbo.Resource
          (ResourceTypeId,ResourceSurrogateId,ResourceId,Version,IsHistory,IsDeleted,RequestMethod,RawResource,IsRawResourceMetaSet,SearchParamHash)
    SELECT ResourceTypeId,ResourceSurrogateId,ResourceId,Version,IsHistory,IsDeleted,RequestMethod,RawResource,IsRawResourceMetaSet,SearchParamHash
      FROM @Resources A
      WHERE NOT EXISTS (SELECT * FROM dbo.Resource B WITH (INDEX = 1) WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId)
      OPTION (MAXDOP 1, LOOP JOIN)
  SET @AffectedRows = @@rowcount

  --TODO: Correct transaction
  IF @AffectedRows > 0
  BEGIN
    INSERT INTO dbo.ReferenceSearchParam
            (ResourceTypeId,ResourceSurrogateId,SearchParamId,BaseUri,ReferenceResourceTypeId,ReferenceResourceId,ReferenceResourceVersion,IsHistory)
      SELECT ResourceTypeId,ResourceSurrogateId,SearchParamId,BaseUri,ReferenceResourceTypeId,ReferenceResourceId,ReferenceResourceVersion,IsHistory
        FROM @ReferenceSearchParams A
    SET @AffectedRows = @AffectedRows + @@rowcount

    INSERT INTO dbo.TokenSearchParam
            (ResourceTypeId,ResourceSurrogateId,SearchParamId,SystemId,Code,IsHistory)
      SELECT ResourceTypeId,ResourceSurrogateId,SearchParamId,SystemId,Code,IsHistory
        FROM @TokenSearchParams A
    SET @AffectedRows = @AffectedRows + @@rowcount

    INSERT INTO dbo.CompartmentAssignment
            (ResourceTypeId,ResourceSurrogateId,CompartmentTypeId,ReferenceResourceId,IsHistory)
      SELECT ResourceTypeId,ResourceSurrogateId,CompartmentTypeId,ReferenceResourceId,IsHistory
        FROM @CompartmentAssignments A
    SET @AffectedRows = @AffectedRows + @@rowcount

    INSERT INTO dbo.TokenText
            (ResourceTypeId,ResourceSurrogateId,SearchParamId,Text,IsHistory)
      SELECT ResourceTypeId,ResourceSurrogateId,SearchParamId,Text,IsHistory
        FROM @TokenTexts A
    SET @AffectedRows = @AffectedRows + @@rowcount

    INSERT INTO dbo.DateTimeSearchParam
            (ResourceTypeId,ResourceSurrogateId,SearchParamId,StartDateTime,EndDateTime,IsLongerThanADay,IsHistory,IsMin,IsMax)
      SELECT ResourceTypeId,ResourceSurrogateId,SearchParamId,StartDateTime,EndDateTime,IsLongerThanADay,IsHistory,IsMin,IsMax
        FROM @DateTimeSearchParams A
    SET @AffectedRows = @AffectedRows + @@rowcount

    INSERT INTO dbo.TokenQuantityCompositeSearchParam
            (ResourceTypeId,ResourceSurrogateId,SearchParamId,SystemId1,Code1,SystemId2,QuantityCodeId2,SingleValue2,LowValue2,HighValue2,IsHistory)
      SELECT ResourceTypeId,ResourceSurrogateId,SearchParamId,SystemId1,Code1,SystemId2,QuantityCodeId2,SingleValue2,LowValue2,HighValue2,IsHistory
        FROM @TokenQuantityCompositeSearchParams A
    SET @AffectedRows = @AffectedRows + @@rowcount

    INSERT INTO dbo.QuantitySearchParam
            (ResourceTypeId,ResourceSurrogateId,SearchParamId,SystemId,QuantityCodeId,SingleValue,LowValue,HighValue,IsHistory)
      SELECT ResourceTypeId,ResourceSurrogateId,SearchParamId,SystemId,QuantityCodeId,SingleValue,LowValue,HighValue,IsHistory
        FROM @QuantitySearchParams A
    SET @AffectedRows = @AffectedRows + @@rowcount

    INSERT INTO dbo.StringSearchParam
            (ResourceTypeId,ResourceSurrogateId,SearchParamId,Text,TextOverflow,IsHistory,IsMin,IsMax)
      SELECT ResourceTypeId,ResourceSurrogateId,SearchParamId,Text,TextOverflow,IsHistory,IsMin,IsMax
        FROM @StringSearchParams A
    SET @AffectedRows = @AffectedRows + @@rowcount

    INSERT INTO dbo.TokenTokenCompositeSearchParam
            (ResourceTypeId,ResourceSurrogateId,SearchParamId,SystemId1,Code1,SystemId2,Code2,IsHistory)
      SELECT ResourceTypeId,ResourceSurrogateId,SearchParamId,SystemId1,Code1,SystemId2,Code2,IsHistory
        FROM @TokenTokenCompositeSearchParams A
    SET @AffectedRows = @AffectedRows + @@rowcount

    INSERT INTO dbo.TokenStringCompositeSearchParam
            (ResourceTypeId,ResourceSurrogateId,SearchParamId,SystemId1,Code1,Text2,TextOverflow2,IsHistory)
      SELECT ResourceTypeId,ResourceSurrogateId,SearchParamId,SystemId1,Code1,Text2,TextOverflow2,IsHistory
        FROM @TokenStringCompositeSearchParams A
    SET @AffectedRows = @AffectedRows + @@rowcount
  END

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@AffectedRows
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st
END CATCH
GO
