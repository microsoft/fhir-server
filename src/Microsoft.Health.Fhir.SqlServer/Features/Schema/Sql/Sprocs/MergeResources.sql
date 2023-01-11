--DROP PROCEDURE dbo.MergeResources
GO
CREATE PROCEDURE dbo.MergeResources
-- This stored procedure can be used for:
-- 1. Ordinary put with single version per resource in input
-- 2. Put with history preservation (multiple input versions per resource)
-- 3. Copy from one gen2 store to another with ResourceSurrogateId preserved.
    @KeepHistory bit = 1
   ,@AffectedRows int = 0 OUT
   ,@RaiseExceptionOnConflict bit = 1
   ,@UseResourceRecordIdAsSurrogateId bit = 0
   ,@IsResourceChangeCaptureEnabled bit = 0
   ,@Resources dbo.ResourceList READONLY
   ,@ResourceWriteClaims dbo.BulkResourceWriteClaimTableType_1 READONLY
   ,@CompartmentAssignments dbo.BulkCompartmentAssignmentTableType_1 READONLY
   ,@ReferenceSearchParams dbo.BulkReferenceSearchParamTableType_1 READONLY
   ,@TokenSearchParams dbo.BulkTokenSearchParamTableType_2 READONLY
   ,@TokenTextSearchParams dbo.BulkTokenTextTableType_1 READONLY
   ,@StringSearchParams dbo.BulkStringSearchParamTableType_2 READONLY
   ,@NumberSearchParams dbo.BulkNumberSearchParamTableType_1 READONLY
   ,@QuantitySearchParams dbo.BulkQuantitySearchParamTableType_1 READONLY
   ,@UriSearchParams dbo.BulkUriSearchParamTableType_1 READONLY
   ,@DateTimeSearchParms dbo.BulkDateTimeSearchParamTableType_2 READONLY
   ,@ReferenceTokenCompositeSearchParams dbo.BulkReferenceTokenCompositeSearchParamTableType_2 READONLY
   ,@TokenTokenCompositeSearchParams dbo.BulkTokenTokenCompositeSearchParamTableType_2 READONLY
   ,@TokenDateTimeCompositeSearchParams dbo.BulkTokenDateTimeCompositeSearchParamTableType_2 READONLY
   ,@TokenQuantityCompositeSearchParams dbo.BulkTokenQuantityCompositeSearchParamTableType_2 READONLY
   ,@TokenStringCompositeSearchParams dbo.BulkTokenStringCompositeSearchParamTableType_2 READONLY
   ,@TokenNumberNumberCompositeSearchParams dbo.BulkTokenNumberNumberCompositeSearchParamTableType_2 READONLY
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
                           +' H='+convert(varchar,@KeepHistory)+' E='+convert(varchar,@RaiseExceptionOnConflict)
                           +' CC='+convert(varchar,@IsResourceChangeCaptureEnabled)+' AS='+convert(varchar,@UseResourceRecordIdAsSurrogateId)

BEGIN TRY
  SET @SurrBase = 0 -- if this value stays, surrogate id will be preserved

  IF @UseResourceRecordIdAsSurrogateId = 0
  BEGIN
    EXECUTE dbo.GetResourceSurrogateIdMaxSequence @Count = @InputRows, @MaxSequence = @MaxSequence OUT
    SET @SurrBase = @MaxSequence - @InputRows
  END

  DECLARE @TrueResources AS TABLE
    (
       ResourceTypeId       smallint       NOT NULL
      ,ResourceRecordId     bigint         NOT NULL
      ,Version              int            NOT NULL
      ,HasVersionToCompare  bit            NOT NULL
      ,IsDeleted            bit            NOT NULL
      ,ExistingVersion      int            NULL
      ,PreviousSurrogateId  bigint         NULL

      PRIMARY KEY (ResourceTypeId, ResourceRecordId)
    )

  DECLARE @PreviousSurrogateIds AS TABLE (TypeId smallint NOT NULL, SurrogateId bigint NOT NULL PRIMARY KEY (TypeId, SurrogateId))

  IF @InitialTranCount = 0 BEGIN TRANSACTION
  
  INSERT INTO @TrueResources
      (
           ResourceTypeId
          ,ResourceRecordId
          ,Version
          ,HasVersionToCompare
          ,IsDeleted
          ,ExistingVersion
          ,PreviousSurrogateId
      )
    SELECT A.ResourceTypeId
          ,A.ResourceRecordId
          ,A.Version
          ,A.HasVersionToCompare
          ,A.IsDeleted
          ,B.Version
          ,B.ResourceSurrogateId
      FROM (SELECT TOP (@DummyTop) * FROM @Resources) A
           LEFT OUTER JOIN dbo.Resource B WITH (UPDLOCK, HOLDLOCK) 
             ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId AND B.IsHistory = 0
      OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))

  IF @RaiseExceptionOnConflict = 1
     AND EXISTS (SELECT * FROM @TrueResources WHERE IsDeleted = 0 AND HasVersionToCompare = 1 AND Version <> isnull(ExistingVersion, 0) + 1)
    THROW 50409, 'Resource has been recently updated or added, please compare the resource content in code for any duplicate updates', 1

  INSERT INTO @PreviousSurrogateIds
    SELECT ResourceTypeId, PreviousSurrogateId
      FROM @TrueResources 
      WHERE PreviousSurrogateId IS NOT NULL

  IF @@rowcount > 0
  BEGIN
    IF @keepHistory = 1
      UPDATE dbo.Resource
        SET IsHistory = 1
        WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
    ELSE
      DELETE FROM dbo.Resource WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
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
         ( ResourceTypeId, ResourceId, Version, IsHistory,           ResourceSurrogateId, IsDeleted, RequestMethod, RawResource,                    IsRawResourceMetaSet, SearchParamHash )
    SELECT ResourceTypeId, ResourceId, Version,         0,  @SurrBase + ResourceRecordId, IsDeleted, RequestMethod, RawResource, CASE WHEN Version = 1 THEN 1 ELSE 0 END, SearchParamHash
      FROM @Resources
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.ResourceWriteClaim 
         (          ResourceSurrogateId, ClaimTypeId, ClaimValue )
    SELECT DISTINCT @SurrBase + Offset, ClaimTypeId, ClaimValue
      FROM @ResourceWriteClaims
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.CompartmentAssignment 
         ( ResourceTypeId,          ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId, IsHistory )
    SELECT DISTINCT ResourceTypeId, @SurrBase + ResourceRecordId, CompartmentTypeId, ReferenceResourceId,         0
      FROM @CompartmentAssignments A JOIN (SELECT ResourceTypeId, ResourceRecordId FROM @Resources) B ON B.ResourceRecordId = Offset
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.ReferenceSearchParam 
         ( ResourceTypeId,          ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion, IsHistory )
    SELECT DISTINCT ResourceTypeId, @SurrBase + ResourceRecordId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion,         0
      FROM @ReferenceSearchParams A JOIN (SELECT ResourceTypeId, ResourceRecordId FROM @Resources) B ON B.ResourceRecordId = Offset
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.TokenSearchParam 
         ( ResourceTypeId,          ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow, IsHistory )
    SELECT DISTINCT ResourceTypeId, @SurrBase + ResourceRecordId, SearchParamId, SystemId, Code, CodeOverflow,         0
      FROM @TokenSearchParams A JOIN (SELECT ResourceTypeId, ResourceRecordId FROM @Resources) B ON B.ResourceRecordId = Offset
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.TokenText 
         ( ResourceTypeId,          ResourceSurrogateId, SearchParamId, Text, IsHistory )
    SELECT DISTINCT ResourceTypeId, @SurrBase + ResourceRecordId, SearchParamId, Text,         0
      FROM @TokenTextSearchParams A JOIN (SELECT ResourceTypeId, ResourceRecordId FROM @Resources) B ON B.ResourceRecordId = Offset
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.StringSearchParam 
         ( ResourceTypeId,             ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsHistory, IsMin, IsMax )
    SELECT DISTINCT ResourceTypeId,  @SurrBase + ResourceRecordId, SearchParamId, Text, TextOverflow,         0, IsMin, IsMax
      FROM @StringSearchParams A JOIN (SELECT ResourceTypeId, ResourceRecordId FROM @Resources) B ON B.ResourceRecordId = Offset
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.UriSearchParam 
         ( ResourceTypeId,          ResourceSurrogateId, SearchParamId, Uri, IsHistory )
    SELECT DISTINCT ResourceTypeId, @SurrBase + ResourceRecordId, SearchParamId, Uri,         0
      FROM @UriSearchParams A JOIN (SELECT ResourceTypeId, ResourceRecordId FROM @Resources) B ON B.ResourceRecordId = Offset
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.NumberSearchParam 
         ( ResourceTypeId,          ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue, IsHistory )
    SELECT DISTINCT ResourceTypeId, @SurrBase + ResourceRecordId, SearchParamId, SingleValue, LowValue, HighValue,         0
      FROM @NumberSearchParams A JOIN (SELECT ResourceTypeId, ResourceRecordId FROM @Resources) B ON B.ResourceRecordId = Offset
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.QuantitySearchParam 
         ( ResourceTypeId,          ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue, IsHistory )
    SELECT DISTINCT ResourceTypeId, @SurrBase + ResourceRecordId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue,         0
      FROM @QuantitySearchParams A JOIN (SELECT ResourceTypeId, ResourceRecordId FROM @Resources) B ON B.ResourceRecordId = Offset
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.DateTimeSearchParam 
         ( ResourceTypeId,          ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsHistory, IsMin, IsMax )
    SELECT DISTINCT ResourceTypeId, @SurrBase + ResourceRecordId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay,         0, IsMin, IsMax
      FROM @DateTimeSearchParms A JOIN (SELECT ResourceTypeId, ResourceRecordId FROM @Resources) B ON B.ResourceRecordId = Offset
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.ReferenceTokenCompositeSearchParam 
         ( ResourceTypeId,          ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2, IsHistory )
    SELECT DISTINCT ResourceTypeId, @SurrBase + ResourceRecordId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2,         0
      FROM @ReferenceTokenCompositeSearchParams A JOIN (SELECT ResourceTypeId, ResourceRecordId FROM @Resources) B ON B.ResourceRecordId = Offset
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.TokenTokenCompositeSearchParam 
         ( ResourceTypeId,          ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2, IsHistory )
    SELECT DISTINCT ResourceTypeId, @SurrBase + ResourceRecordId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2,         0
      FROM @TokenTokenCompositeSearchParams A JOIN (SELECT ResourceTypeId, ResourceRecordId FROM @Resources) B ON B.ResourceRecordId = Offset
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.TokenDateTimeCompositeSearchParam 
         ( ResourceTypeId,          ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2, IsHistory )
    SELECT DISTINCT ResourceTypeId, @SurrBase + ResourceRecordId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2,         0
      FROM @TokenDateTimeCompositeSearchParams A JOIN (SELECT ResourceTypeId, ResourceRecordId FROM @Resources) B ON B.ResourceRecordId = Offset
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.TokenQuantityCompositeSearchParam 
         ( ResourceTypeId,          ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2, IsHistory )
    SELECT DISTINCT ResourceTypeId, @SurrBase + ResourceRecordId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2,         0
      FROM @TokenQuantityCompositeSearchParams A JOIN (SELECT ResourceTypeId, ResourceRecordId FROM @Resources) B ON B.ResourceRecordId = Offset
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.TokenStringCompositeSearchParam 
         ( ResourceTypeId,          ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2, IsHistory )
    SELECT DISTINCT ResourceTypeId, @SurrBase + ResourceRecordId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2,         0
      FROM @TokenStringCompositeSearchParams A JOIN (SELECT ResourceTypeId, ResourceRecordId FROM @Resources) B ON B.ResourceRecordId = Offset
  SET @AffectedRows += @@rowcount

  INSERT INTO dbo.TokenNumberNumberCompositeSearchParam 
         ( ResourceTypeId,          ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange, IsHistory )
    SELECT DISTINCT ResourceTypeId, @SurrBase + ResourceRecordId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange,         0
      FROM @TokenNumberNumberCompositeSearchParams A JOIN (SELECT ResourceTypeId, ResourceRecordId FROM @Resources) B ON B.ResourceRecordId = Offset
  SET @AffectedRows += @@rowcount

  SELECT ResourceTypeId, @SurrBase + ResourceRecordId, ResourceId
    FROM @Resources

  IF @IsResourceChangeCaptureEnabled = 1 --If the resource change capture feature is enabled, to execute a stored procedure called CaptureResourceChanges to insert resource change data.
  BEGIN
    DECLARE @Ids dbo.ResourceIdForChangesList
    INSERT INTO @Ids
           ( ResourceTypeId, ResourceId, Version, IsDeleted )
      SELECT ResourceTypeId, ResourceId, Version, IsDeleted
        FROM @Resources
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
