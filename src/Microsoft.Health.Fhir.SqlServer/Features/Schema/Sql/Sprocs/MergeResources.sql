--DROP PROCEDURE dbo.MergeResources
GO
CREATE PROCEDURE dbo.MergeResources
-- This stored procedure can be used for:
-- 1. Ordinary put with single version per resource in input
-- 2. Put with history preservation (multiple input versions per resource)
-- 3. Copy from one gen2 store to another with ResourceSurrogateId preserved.
    @AffectedRows int = 0 OUT
   ,@RaiseExceptionOnConflict bit = 1
   ,@IsResourceChangeCaptureEnabled bit = 0
   ,@Resources dbo.ResourceList READONLY
   ,@ResourceWriteClaims dbo.ResourceWriteClaimList READONLY
   ,@CompartmentAssignments dbo.CompartmentAssignmentList READONLY
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
       ,@SP varchar(100) = 'MergeResources'
       ,@MaxSequence bigint
       ,@SurrBase bigint
       ,@DummyTop bigint = 9223372036854775807
       ,@InitialTranCount int = @@trancount
       ,@InputRows int = (SELECT count(*) FROM @Resources)

SET @AffectedRows = 0

DECLARE @Mode varchar(100) = 'Input='+convert(varchar,@InputRows)+' TR='+convert(varchar,@InitialTranCount)
                           +' E='+convert(varchar,@RaiseExceptionOnConflict)+' CC='+convert(varchar,@IsResourceChangeCaptureEnabled)

BEGIN TRY
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

  IF @InitialTranCount = 0 BEGIN TRANSACTION
  
  INSERT INTO @ResourceInfos
      (
           ResourceTypeId
          ,SurrogateId
          ,Version
          ,KeepHistory
          ,PreviousVersion
          ,PreviousSurrogateId
      )
    SELECT A.ResourceTypeId
          ,A.ResourceSurrogateId
          ,A.Version
          ,A.KeepHistory
          ,B.Version
          ,B.ResourceSurrogateId
      FROM (SELECT TOP (@DummyTop) * FROM @Resources WHERE HasVersionToCompare = 1) A
           LEFT OUTER JOIN dbo.Resource B WITH (UPDLOCK, HOLDLOCK) 
             ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId AND B.IsHistory = 0
      OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))

  IF @RaiseExceptionOnConflict = 1 AND EXISTS (SELECT * FROM @ResourceInfos WHERE Version <> isnull(PreviousVersion, 0) + 1)
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
    
    DELETE FROM dbo.Resource WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId AND KeepHistory = 0)
    SET @AffectedRows += @@rowcount

    DELETE FROM dbo.ResourceWriteClaim WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE SurrogateId = ResourceSurrogateId)
    SET @AffectedRows += @@rowcount
    DELETE FROM dbo.CompartmentAssignment WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
    SET @AffectedRows += @@rowcount
    DELETE FROM dbo.ReferenceSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
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

  INSERT INTO dbo.Resource 
         ( ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash )
    SELECT ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash
      FROM @Resources
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.ResourceWriteClaim 
         (          ResourceSurrogateId, ClaimTypeId, ClaimValue )
    SELECT DISTINCT ResourceSurrogateId, ClaimTypeId, ClaimValue
      FROM @ResourceWriteClaims
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.CompartmentAssignment 
         (          ResourceTypeId, ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId, IsHistory )
    SELECT DISTINCT ResourceTypeId, ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId,         0
      FROM @CompartmentAssignments
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.ReferenceSearchParam 
         (          ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion, IsHistory )
    SELECT DISTINCT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion,         0
      FROM @ReferenceSearchParams
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.TokenSearchParam 
         (          ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow, IsHistory )
    SELECT DISTINCT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow,         0
      FROM @TokenSearchParams
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.TokenText 
         (          ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, IsHistory )
    SELECT DISTINCT ResourceTypeId, ResourceSurrogateId, SearchParamId, Text,         0
      FROM @TokenTexts
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.StringSearchParam 
         (          ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsHistory, IsMin, IsMax )
    SELECT DISTINCT ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow,         0, IsMin, IsMax
      FROM @StringSearchParams
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.UriSearchParam 
         (          ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri, IsHistory )
    SELECT DISTINCT ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri,         0
      FROM @UriSearchParams
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.NumberSearchParam 
         (          ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue, IsHistory )
    SELECT DISTINCT ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue,         0
      FROM @NumberSearchParams
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.QuantitySearchParam 
         (          ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue, IsHistory )
    SELECT DISTINCT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue,         0
      FROM @QuantitySearchParams
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.DateTimeSearchParam 
         (          ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsHistory, IsMin, IsMax )
    SELECT DISTINCT ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay,         0, IsMin, IsMax
      FROM @DateTimeSearchParms
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.ReferenceTokenCompositeSearchParam 
         (          ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2, IsHistory )
    SELECT DISTINCT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2,         0
      FROM @ReferenceTokenCompositeSearchParams
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.TokenTokenCompositeSearchParam 
         (          ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2, IsHistory )
    SELECT DISTINCT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2,         0
      FROM @TokenTokenCompositeSearchParams
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.TokenDateTimeCompositeSearchParam 
         (          ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2, IsHistory )
    SELECT DISTINCT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2,         0
      FROM @TokenDateTimeCompositeSearchParams
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.TokenQuantityCompositeSearchParam 
         (          ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2, IsHistory )
    SELECT DISTINCT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2,         0
      FROM @TokenQuantityCompositeSearchParams
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.TokenStringCompositeSearchParam 
         (          ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2, IsHistory )
    SELECT DISTINCT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2,         0
      FROM @TokenStringCompositeSearchParams
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.TokenNumberNumberCompositeSearchParam 
         (          ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange, IsHistory )
    SELECT DISTINCT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange,         0
      FROM @TokenNumberNumberCompositeSearchParams
  SET @AffectedRows += @@rowcount

  IF @IsResourceChangeCaptureEnabled = 1 --If the resource change capture feature is enabled, to execute a stored procedure called CaptureResourceChanges to insert resource change data.
  BEGIN
    DECLARE @Ids dbo.ResourceIdForChangesList
    INSERT INTO @Ids
           ( ResourceTypeId, ResourceId, Version, IsDeleted )
      SELECT ResourceTypeId, ResourceId, Version, IsDeleted
        FROM @Resources
        WHERE IsHistory = 0
    EXECUTE dbo.CaptureResourceIdsForChanges @Ids
  END

  IF @InitialTranCount = 0 COMMIT TRANSACTION

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@AffectedRows
END TRY
BEGIN CATCH
  IF @InitialTranCount = 0 AND @@trancount > 0 ROLLBACK TRANSACTION
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
