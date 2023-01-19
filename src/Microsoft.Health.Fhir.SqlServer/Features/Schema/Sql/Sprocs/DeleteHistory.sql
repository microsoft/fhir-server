--DROP PROCEDURE dbo.DeleteHistory
GO
CREATE PROCEDURE dbo.DeleteHistory @DeleteResources bit = 0, @Reset bit = 0, @DisableLogEvent bit = 0
AS
set nocount on
DECLARE @SP varchar(100) = 'DeleteHistory'
       ,@Mode varchar(100) = 'D='+isnull(convert(varchar,@DeleteResources),'NULL')+' R='+isnull(convert(varchar,@Reset),'NULL')
       ,@st datetime = getUTCdate()
       ,@Id varchar(100) = 'DeleteHistory.LastProcessed.TypeId.SurrogateId'
       ,@ResourceTypeId smallint
       ,@SurrogateId bigint
       ,@RowsToProcess int
       ,@ProcessedResources int = 0
       ,@DeletedResources int = 0
       ,@DeletedSearchParams int = 0
       ,@ReportDate datetime = getUTCdate()

BEGIN TRY
  IF @DisableLogEvent = 0 
    INSERT INTO dbo.Parameters (Id, Char) SELECT @SP, 'LogEvent'
  ELSE 
    DELETE FROM dbo.Parameters WHERE Id = @SP

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Start'

  INSERT INTO dbo.Parameters (Id, Char) SELECT @Id, '0.0' WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = @Id)

  DECLARE @LastProcessed varchar(100) = CASE WHEN @Reset = 0 THEN (SELECT Char FROM dbo.Parameters WHERE Id = @Id) ELSE '0.0' END

  DECLARE @Types TABLE (ResourceTypeId smallint PRIMARY KEY, Name varchar(100))
  DECLARE @SurrogateIds TABLE (ResourceSurrogateId bigint PRIMARY KEY, IsHistory bit)
  
  INSERT INTO @Types EXECUTE dbo.GetUsedResourceTypes
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Run',@Target='@Types',@Action='Insert',@Rows=@@rowcount

  SET @ResourceTypeId = substring(@LastProcessed, 1, charindex('.', @LastProcessed) - 1) -- (SELECT value FROM string_split(@LastProcessed, '.', 1) WHERE ordinal = 1)
  SET @SurrogateId = substring(@LastProcessed, charindex('.', @LastProcessed) + 1, 255) -- (SELECT value FROM string_split(@LastProcessed, '.', 1) WHERE ordinal = 2)

  DELETE FROM @Types WHERE ResourceTypeId < @ResourceTypeId
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Run',@Target='@Types',@Action='Delete',@Rows=@@rowcount

  WHILE EXISTS (SELECT * FROM @Types) -- Processing in ASC order
  BEGIN
    SET @ResourceTypeId = (SELECT TOP 1 ResourceTypeId FROM @Types ORDER BY ResourceTypeId)

    SET @ProcessedResources = 0
    SET @DeletedResources = 0
    SET @DeletedSearchParams = 0
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
            AND ResourceSurrogateId > @SurrogateId
          ORDER BY
               ResourceSurrogateId
      SET @RowsToProcess = @@rowcount
      SET @ProcessedResources += @RowsToProcess

      IF @RowsToProcess > 0
        SET @SurrogateId = (SELECT max(ResourceSurrogateId) FROM @SurrogateIds)

      SET @LastProcessed = convert(varchar,@ResourceTypeId)+'.'+convert(varchar,@SurrogateId)

      DELETE FROM @SurrogateIds WHERE IsHistory = 0
      
      IF EXISTS (SELECT * FROM @SurrogateIds)
      BEGIN
        DELETE FROM dbo.ResourceWriteClaim WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
        SET @DeletedSearchParams += @@rowcount

        DELETE FROM dbo.CompartmentAssignment WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
        SET @DeletedSearchParams += @@rowcount

        DELETE FROM dbo.ReferenceSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
        SET @DeletedSearchParams += @@rowcount

        DELETE FROM dbo.TokenSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
        SET @DeletedSearchParams += @@rowcount

        DELETE FROM dbo.TokenText WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
        SET @DeletedSearchParams += @@rowcount

        DELETE FROM dbo.StringSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
        SET @DeletedSearchParams += @@rowcount

        DELETE FROM dbo.UriSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
        SET @DeletedSearchParams += @@rowcount

        DELETE FROM dbo.NumberSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
        SET @DeletedSearchParams += @@rowcount

        DELETE FROM dbo.QuantitySearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
        SET @DeletedSearchParams += @@rowcount

        DELETE FROM dbo.DateTimeSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
        SET @DeletedSearchParams += @@rowcount

        DELETE FROM dbo.ReferenceTokenCompositeSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
        SET @DeletedSearchParams += @@rowcount

        DELETE FROM dbo.TokenTokenCompositeSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
        SET @DeletedSearchParams += @@rowcount

        DELETE FROM dbo.TokenDateTimeCompositeSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
        SET @DeletedSearchParams += @@rowcount

        DELETE FROM dbo.TokenQuantityCompositeSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
        SET @DeletedSearchParams += @@rowcount

        DELETE FROM dbo.TokenStringCompositeSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
        SET @DeletedSearchParams += @@rowcount

        DELETE FROM dbo.TokenNumberNumberCompositeSearchParam WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
        SET @DeletedSearchParams += @@rowcount

        IF @DeleteResources = 1
        BEGIN
          DELETE FROM dbo.Resource WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds)
          SET @DeletedResources += @@rowcount
        END
      END

      UPDATE dbo.Parameters SET Char = @LastProcessed WHERE Id = @Id

      IF datediff(second, @ReportDate, getUTCdate()) > 60
      BEGIN
        EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Run',@Target='Resource',@Action='Select',@Rows=@ProcessedResources,@Text=@LastProcessed
        EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Run',@Target='*SearchParam',@Action='Delete',@Rows=@DeletedSearchParams,@Text=@LastProcessed
        IF @DeleteResources = 1 EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Run',@Target='Resource',@Action='Delete',@Rows=@DeletedResources,@Text=@LastProcessed
        SET @ReportDate = getUTCdate()
        SET @ProcessedResources = 0
        SET @DeletedSearchParams = 0
        SET @DeletedResources = 0
      END
    END

    DELETE FROM @Types WHERE ResourceTypeId = @ResourceTypeId

    SET @SurrogateId = 0
  END

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Run',@Target='Resource',@Action='Select',@Rows=@ProcessedResources,@Text=@LastProcessed
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Run',@Target='*SearchParam',@Action='Delete',@Rows=@DeletedSearchParams,@Text=@LastProcessed
  IF @DeleteResources = 1 EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Run',@Target='Resource',@Action='Delete',@Rows=@DeletedResources,@Text=@LastProcessed

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
--EXECUTE dbo.DeleteHistory 1
--SELECT * FROM Parameters WHERE Id = 'DeleteHistory.LastProcessed.TypeId.SurrogateId'
--SELECT TOP 100 * FROM EventLog WHERE EventDate > dateadd(minute,-10,getUTCdate()) AND Process = 'DeleteHistory' ORDER BY EventDate DESC
--INSERT INTO Parameters (Id, Char) SELECT 'DeleteHistory','LogEvent'
