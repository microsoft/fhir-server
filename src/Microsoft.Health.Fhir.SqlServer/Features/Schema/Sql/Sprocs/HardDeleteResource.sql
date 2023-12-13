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
    -- PAGLOCK allows deallocation of empty page without waiting for ghost cleanup 
    DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.ResourceWriteClaim B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.ReferenceSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.TokenSearchParamHighCard B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
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
