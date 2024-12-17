CREATE PROCEDURE dbo.HardDeleteResource
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
        --AND RawResource <> 0xF -- Cannot check this as resource can be stored in ADLS
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
    DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.StringReferenceSearchParams B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
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
