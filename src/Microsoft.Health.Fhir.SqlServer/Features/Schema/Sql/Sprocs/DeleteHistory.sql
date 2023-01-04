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

    SET @MinSurrogateId = 0
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
