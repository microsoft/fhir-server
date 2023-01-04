--DROP PROCEDURE dbo.DeleteHistory
GO
CREATE PROCEDURE dbo.DeleteHistory @DeleteResources bit = 0
AS
set nocount on
DECLARE @SP varchar(100) = 'DeleteHistory'
       ,@Mode varchar(100) = 'DR='+isnull(convert(varchar,@DeleteResources),'NULL')
       ,@msg varchar(100)
       ,@st datetime = getUTCdate()
       ,@Rows int = 0
       ,@ResourceTypeId smallint
       ,@MinSurrogateId bigint = 0
       ,@MinResourceTypeId smallint = 0
       ,@RowsToProcess int
       ,@Id varchar(100) = 'DeleteHistory.LastProcessed.TypeId.SurrogateId'

DECLARE @LastProcessed varchar(100) = (SELECT Char FROM dbo.Parameters WHERE Id = @Id)

BEGIN TRY
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Start'

  IF @LastProcessed IS NULL
    INSERT INTO dbo.Parameters (Id, Char) SELECT @Id, '0.0'

  DECLARE @Types TABLE (ResourceTypeId smallint PRIMARY KEY, Name varchar(100))
  DECLARE @SurrogateIds TABLE (ResourceSurrogateId bigint PRIMARY KEY, IsHistory bit)
  
  INSERT INTO @Types 
    EXECUTE dbo.GetUsedResourceTypes
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Run',@Target='@Types',@Action='Insert',@Rows=@@rowcount

  IF @LastProcessed IS NOT NULL
  BEGIN
    SET @MinResourceTypeId = (SELECT value FROM string_split(@LastProcessed, '.', 1) WHERE ordinal = 1)
    SET @MinSurrogateId = (SELECT value FROM string_split(@LastProcessed, '.', 1) WHERE ordinal = 2)
  END

  DELETE FROM @Types WHERE ResourceTypeId < @MinResourceTypeId
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Run',@Target='@Types',@Action='Delete',@Rows=@@rowcount

  WHILE EXISTS (SELECT * FROM @Types) -- Processing in ASC order
  BEGIN
    SET @ResourceTypeId = (SELECT TOP 1 ResourceTypeId FROM @Types ORDER BY ResourceTypeId)

    SET @RowsToProcess = 1
    WHILE @RowsToProcess > 0
    BEGIN
      DELETE FROM @SurrogateIds

      INSERT INTO @SurrogateIds
        SELECT TOP 10000
               ResourceSurrogateId
              ,IsHistory
          FROM dbo.Resource
          WHERE ResourceTypeId = @ResourceTypeId
            AND ResourceSurrogateId > @MinSurrogateId
          ORDER BY
               ResourceSurrogateId
      SET @RowsToProcess = @@rowcount

      IF @RowsToProcess > 0
        SET @MinSurrogateId = (SELECT max(ResourceSurrogateId) FROM @SurrogateIds)

      DELETE FROM @SurrogateIds WHERE IsHistory = 0
      
      IF EXISTS (SELECT * FROM @SurrogateIds)
      BEGIN
        DELETE FROM dbo.ResourceWriteClaim WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
        SET @Rows += @@rowcount

        DELETE FROM dbo.CompartmentAssignment WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
        SET @Rows += @@rowcount

        DELETE FROM dbo.ReferenceSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
        SET @Rows += @@rowcount

        DELETE FROM dbo.TokenSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
        SET @Rows += @@rowcount

        DELETE FROM dbo.TokenText WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
        SET @Rows += @@rowcount

        DELETE FROM dbo.StringSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
        SET @Rows += @@rowcount

        DELETE FROM dbo.UriSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
        SET @Rows += @@rowcount

        DELETE FROM dbo.NumberSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
        SET @Rows += @@rowcount

        DELETE FROM dbo.QuantitySearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
        SET @Rows += @@rowcount

        DELETE FROM dbo.DateTimeSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
        SET @Rows += @@rowcount

        DELETE FROM dbo.ReferenceTokenCompositeSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
        SET @Rows += @@rowcount

        DELETE FROM dbo.TokenTokenCompositeSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
        SET @Rows += @@rowcount

        DELETE FROM dbo.TokenDateTimeCompositeSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
        SET @Rows += @@rowcount

        DELETE FROM dbo.TokenQuantityCompositeSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
        SET @Rows += @@rowcount

        DELETE FROM dbo.TokenStringCompositeSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
        SET @Rows += @@rowcount

        DELETE FROM dbo.TokenNumberNumberCompositeSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
        SET @Rows += @@rowcount

        IF @DeleteResources = 1
        BEGIN
          DELETE FROM dbo.Resource WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
          SET @Rows += @@rowcount
        END
      END
      
      SET @msg = convert(varchar,@ResourceTypeId)+'.'+convert(varchar,@MinSurrogateId)
      UPDATE dbo.Parameters SET Char = @msg WHERE Id = @Id

      EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Run',@Action='Delete',@Rows=@Rows,@Text=@msg
    END

    DELETE FROM @Types WHERE ResourceTypeId = @ResourceTypeId
  END

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
--EXECUTE dbo.DeleteHistory
--SELECT * FROM Parameters WHERE Id = 'DeleteHistory.LastProcessed.TypeId.SurrogateId'
--SELECT TOP 100 * FROM EventLog WHERE EventDate > dateadd(minute,-10,getUTCdate()) AND Process = 'DeleteHistory' ORDER BY EventDate DESC
--INSERT INTO Parameters (Id, Char) SELECT 'DeleteHistory','LogEvent'
GO
CREATE OR ALTER PROCEDURE dbo.UpsertResource_7
    @baseResourceSurrogateId bigint,
    @resourceTypeId smallint,
    @resourceId varchar(64),
    @eTag int = NULL,
    @allowCreate bit,
    @isDeleted bit,
    @keepHistory bit,
    @requireETagOnUpdate bit,
    @requestMethod varchar(10),
    @searchParamHash varchar(64),
    @rawResource varbinary(max),
    @resourceWriteClaims dbo.BulkResourceWriteClaimTableType_1 READONLY,
    @compartmentAssignments dbo.BulkCompartmentAssignmentTableType_1 READONLY,
    @referenceSearchParams dbo.BulkReferenceSearchParamTableType_1 READONLY,
    @tokenSearchParams dbo.BulkTokenSearchParamTableType_2 READONLY,
    @tokenTextSearchParams dbo.BulkTokenTextTableType_1 READONLY,
    @stringSearchParams dbo.BulkStringSearchParamTableType_2 READONLY,
    @numberSearchParams dbo.BulkNumberSearchParamTableType_1 READONLY,
    @quantitySearchParams dbo.BulkQuantitySearchParamTableType_1 READONLY,
    @uriSearchParams dbo.BulkUriSearchParamTableType_1 READONLY,
    @dateTimeSearchParms dbo.BulkDateTimeSearchParamTableType_2 READONLY,
    @referenceTokenCompositeSearchParams dbo.BulkReferenceTokenCompositeSearchParamTableType_2 READONLY,
    @tokenTokenCompositeSearchParams dbo.BulkTokenTokenCompositeSearchParamTableType_2 READONLY,
    @tokenDateTimeCompositeSearchParams dbo.BulkTokenDateTimeCompositeSearchParamTableType_2 READONLY,
    @tokenQuantityCompositeSearchParams dbo.BulkTokenQuantityCompositeSearchParamTableType_2 READONLY,
    @tokenStringCompositeSearchParams dbo.BulkTokenStringCompositeSearchParamTableType_2 READONLY,
    @tokenNumberNumberCompositeSearchParams dbo.BulkTokenNumberNumberCompositeSearchParamTableType_2 READONLY,
    @isResourceChangeCaptureEnabled bit = 0,
    @comparedVersion int = NULL
AS
set nocount on
set xact_abort on

-- variables for the existing version of the resource that will be replaced
DECLARE @previousResourceSurrogateId bigint
       ,@previousVersion bigint
       ,@previousIsDeleted bit
       ,@version int -- the version of the resource being written
       ,@resourceSurrogateId bigint
       ,@InitialTranCount int = @@trancount

IF @InitialTranCount = 0 BEGIN TRANSACTION

SELECT @previousResourceSurrogateId = ResourceSurrogateId
      ,@previousVersion = Version
      ,@previousIsDeleted = IsDeleted
  FROM dbo.Resource WITH (UPDLOCK, HOLDLOCK) -- This should place a range lock on a row in the IX_Resource_ResourceTypeId_ResourceId nonclustered filtered index
  WHERE ResourceTypeId = @resourceTypeId
    AND ResourceId = @resourceId
    AND IsHistory = 0

IF @previousResourceSurrogateId IS NULL -- There is no previous version
  SET @version = 1
ELSE
BEGIN -- There is a previous version so @previousVersion will not be null
  IF @isDeleted = 0 -- When not a delete
  BEGIN
    IF @comparedVersion IS NULL OR @comparedVersion <> @previousVersion
    BEGIN
      -- If @comparedVersion is null then resource was recently added
      -- Otherwise if @comparedVersion doesn't match the @previousVersion in the DB means the version we compared in the code is not the latest version anymore
      -- Go back to code and compare the latest
      THROW 50409, 'Resource has been recently updated or added, please compare the resource content in code for any duplicate updates', 1
    END
  END

  SET @version = @previousVersion + 1
  IF @keepHistory = 1
    UPDATE dbo.Resource SET IsHistory = 1 WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId -- Set the existing resource as history
  ELSE
    DELETE FROM dbo.Resource WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

  -- delete history from index tables because we do not search on them
  DELETE FROM dbo.ResourceWriteClaim WHERE ResourceSurrogateId = @previousResourceSurrogateId
  DELETE FROM dbo.CompartmentAssignment WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId
  DELETE FROM dbo.ReferenceSearchParam WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId
  DELETE FROM dbo.TokenSearchParam WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId
  DELETE FROM dbo.TokenText WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId
  DELETE FROM dbo.StringSearchParam WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId
  DELETE FROM dbo.UriSearchParam WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId
  DELETE FROM dbo.NumberSearchParam WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId
  DELETE FROM dbo.QuantitySearchParam WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId
  DELETE FROM dbo.DateTimeSearchParam WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId
  DELETE FROM dbo.ReferenceTokenCompositeSearchParam WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId
  DELETE FROM dbo.TokenTokenCompositeSearchParam WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId
  DELETE FROM dbo.TokenDateTimeCompositeSearchParam WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId
  DELETE FROM dbo.TokenQuantityCompositeSearchParam WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId
  DELETE FROM dbo.TokenStringCompositeSearchParam WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId
  DELETE FROM dbo.TokenNumberNumberCompositeSearchParam WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId
END

SET @resourceSurrogateId = @baseResourceSurrogateId + (NEXT VALUE FOR ResourceSurrogateIdUniquifierSequence)

INSERT INTO dbo.Resource (ResourceTypeId,  ResourceId,  Version, IsHistory,  ResourceSurrogateId,  IsDeleted,  RequestMethod,  RawResource,                     IsRawResourceMetaSet,  SearchParamHash)
  SELECT                 @resourceTypeId, @resourceId, @version,         0, @resourceSurrogateId, @isDeleted, @requestMethod, @rawResource, CASE WHEN @version = 1 THEN 1 ELSE 0 END, @searchParamHash

INSERT INTO dbo.ResourceWriteClaim (ResourceSurrogateId, ClaimTypeId, ClaimValue)
  SELECT                           @resourceSurrogateId, ClaimTypeId, ClaimValue
    FROM @resourceWriteClaims

INSERT INTO dbo.CompartmentAssignment (ResourceTypeId,  ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId, IsHistory)
  SELECT DISTINCT                     @resourceTypeId, @resourceSurrogateId, CompartmentTypeId, ReferenceResourceId,         0
    FROM @compartmentAssignments

INSERT INTO dbo.ReferenceSearchParam (ResourceTypeId,  ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion, IsHistory)
  SELECT DISTINCT                    @resourceTypeId, @resourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion,         0
    FROM @referenceSearchParams

INSERT INTO dbo.TokenSearchParam (ResourceTypeId,  ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow, IsHistory)
  SELECT DISTINCT                @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow,         0
    FROM @tokenSearchParams

INSERT INTO dbo.TokenText (ResourceTypeId,  ResourceSurrogateId, SearchParamId, Text, IsHistory)
  SELECT DISTINCT         @resourceTypeId, @resourceSurrogateId, SearchParamId, Text,         0
    FROM @tokenTextSearchParams

INSERT INTO dbo.StringSearchParam (ResourceTypeId,  ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsHistory, IsMin, IsMax)
  SELECT DISTINCT                 @resourceTypeId, @resourceSurrogateId, SearchParamId, Text, TextOverflow,         0, IsMin, IsMax
    FROM @stringSearchParams

INSERT INTO dbo.UriSearchParam (ResourceTypeId,  ResourceSurrogateId, SearchParamId, Uri, IsHistory)
  SELECT DISTINCT              @resourceTypeId, @resourceSurrogateId, SearchParamId, Uri,         0
    FROM @uriSearchParams

INSERT INTO dbo.NumberSearchParam (ResourceTypeId,  ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue, IsHistory)
  SELECT DISTINCT                 @resourceTypeId, @resourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue,         0
    FROM @numberSearchParams

INSERT INTO dbo.QuantitySearchParam (ResourceTypeId,  ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue, IsHistory)
  SELECT DISTINCT                   @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue,         0
    FROM @quantitySearchParams

INSERT INTO dbo.DateTimeSearchParam (ResourceTypeId,  ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsHistory, IsMin, IsMax)
  SELECT DISTINCT                   @resourceTypeId, @resourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay,         0, IsMin, IsMax
    FROM @dateTimeSearchParms

INSERT INTO dbo.ReferenceTokenCompositeSearchParam (ResourceTypeId,  ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2, IsHistory)
  SELECT DISTINCT                                  @resourceTypeId, @resourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2,         0
    FROM @referenceTokenCompositeSearchParams

INSERT INTO dbo.TokenTokenCompositeSearchParam (ResourceTypeId,  ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2, IsHistory)
  SELECT DISTINCT                              @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2,         0
    FROM @tokenTokenCompositeSearchParams

INSERT INTO dbo.TokenDateTimeCompositeSearchParam (ResourceTypeId,  ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2, IsHistory)
  SELECT DISTINCT                                 @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2,         0
    FROM @tokenDateTimeCompositeSearchParams

INSERT INTO dbo.TokenQuantityCompositeSearchParam (ResourceTypeId,  ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2, IsHistory)
  SELECT DISTINCT                                 @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2,         0
    FROM @tokenQuantityCompositeSearchParams

INSERT INTO dbo.TokenStringCompositeSearchParam (ResourceTypeId,  ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2, IsHistory)
  SELECT DISTINCT                               @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2,         0
    FROM @tokenStringCompositeSearchParams

INSERT INTO dbo.TokenNumberNumberCompositeSearchParam (ResourceTypeId,  ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange, IsHistory)
  SELECT DISTINCT                                     @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange,         0
    FROM @tokenNumberNumberCompositeSearchParams

SELECT @version

IF @isResourceChangeCaptureEnabled = 1 --If the resource change capture feature is enabled, to execute a stored procedure called CaptureResourceChanges to insert resource change data.
  EXECUTE dbo.CaptureResourceChanges @isDeleted = @isDeleted, @version = @version, @resourceId = @resourceId, @resourceTypeId = @resourceTypeId

IF @InitialTranCount = 0 COMMIT TRANSACTION

GO
