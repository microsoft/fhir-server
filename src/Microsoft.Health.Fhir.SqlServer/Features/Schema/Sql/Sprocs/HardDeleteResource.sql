CREATE PROCEDURE dbo.HardDeleteResource
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

  IF @IsResourceChangeCaptureEnabled = 0
    DELETE dbo.Resource
      OUTPUT deleted.ResourceSurrogateId INTO @SurrogateIds
      WHERE ResourceTypeId = @ResourceTypeId
        AND ResourceId = @ResourceId
        AND (@KeepCurrentVersion = 0 OR IsHistory = 1)
        AND RawResource <> 0xF
  ELSE
  BEGIN
    UPDATE dbo.Resource
      SET IsHistory = 1
         ,RawResource = 0xF -- "invisible" value
         ,SearchParamHash = NULL
         ,HistoryTransactionId = @TransactionId
      OUTPUT deleted.ResourceSurrogateId INTO @SurrogateIds
      WHERE ResourceTypeId = @ResourceTypeId
        AND ResourceId = @ResourceId
        AND (@KeepCurrentVersion = 0 OR IsHistory = 1)
        AND RawResource <> 0xF
  END

  IF @KeepCurrentVersion = 0
  BEGIN
    DELETE dbo.ResourceWriteClaim WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
    DELETE dbo.CompartmentAssignment WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
    DELETE dbo.ReferenceSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
    DELETE dbo.TokenSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
    DELETE dbo.TokenText WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
    DELETE dbo.StringSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
    DELETE dbo.UriSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
    DELETE dbo.NumberSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
    DELETE dbo.QuantitySearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
    DELETE dbo.DateTimeSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
    DELETE dbo.ReferenceTokenCompositeSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
    DELETE dbo.TokenTokenCompositeSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
    DELETE dbo.TokenDateTimeCompositeSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
    DELETE dbo.TokenQuantityCompositeSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
    DELETE dbo.TokenStringCompositeSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
    DELETE dbo.TokenNumberNumberCompositeSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
  END
  
  IF @@trancount > 0 COMMIT TRANSACTION

  IF @IsResourceChangeCaptureEnabled = 1 EXECUTE dbo.MergeResourcesCommitTransaction @TransactionId
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
