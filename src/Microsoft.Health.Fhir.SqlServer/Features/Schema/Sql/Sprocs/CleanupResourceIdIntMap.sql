--DROP PROCEDURE dbo.CleanupResourceIdIntMap
GO
CREATE PROCEDURE dbo.CleanupResourceIdIntMap @ResetAfter bit = 0
AS
set nocount on
DECLARE @SP varchar(100) = 'CleanupResourceIdIntMap'
       ,@Mode varchar(100) = 'R='+isnull(convert(varchar,@ResetAfter),'NULL')
       ,@st datetime = getUTCdate()
       ,@Id varchar(100) = 'CleanupResourceIdIntMap.LastProcessed.TypeId.ResourceIdInt'
       ,@ResourceTypeId smallint
       ,@ResourceIdInt bigint
       ,@RowsToProcess int
       ,@ProcessedRows int = 0
       ,@DeletedRows int = 0
       ,@ReportDate datetime = getUTCdate()
DECLARE @LastProcessed varchar(100) = isnull((SELECT Char FROM dbo.Parameters WHERE Id = @Id),'0.0')

BEGIN TRY
  INSERT INTO dbo.Parameters (Id, Char) SELECT @SP, 'LogEvent'

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Start'

  INSERT INTO dbo.Parameters (Id, Char) SELECT @Id, '0.0'

  DECLARE @Types TABLE (ResourceTypeId smallint PRIMARY KEY, Name varchar(100))
  DECLARE @ResourceIdInts TABLE (ResourceIdInt bigint PRIMARY KEY)
  
  INSERT INTO @Types EXECUTE dbo.GetUsedResourceTypes
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Run',@Target='@Types',@Action='Insert',@Rows=@@rowcount

  SET @ResourceTypeId = substring(@LastProcessed, 1, charindex('.', @LastProcessed) - 1) -- (SELECT value FROM string_split(@LastProcessed, '.', 1) WHERE ordinal = 1)
  SET @ResourceIdInt = substring(@LastProcessed, charindex('.', @LastProcessed) + 1, 255) -- (SELECT value FROM string_split(@LastProcessed, '.', 1) WHERE ordinal = 2)

  DELETE FROM @Types WHERE ResourceTypeId < @ResourceTypeId
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Run',@Target='@Types',@Action='Delete',@Rows=@@rowcount

  WHILE EXISTS (SELECT * FROM @Types) -- Processing in ASC order
  BEGIN
    SET @ResourceTypeId = (SELECT TOP 1 ResourceTypeId FROM @Types ORDER BY ResourceTypeId)

    SET @ProcessedRows = 0
    SET @DeletedRows = 0
    SET @RowsToProcess = 1
    WHILE @RowsToProcess > 0
    BEGIN
      DELETE FROM @ResourceIdInts

      INSERT INTO @ResourceIdInts
        SELECT TOP 100000
               ResourceIdInt
          FROM dbo.ResourceIdIntMap
          WHERE ResourceTypeId = @ResourceTypeId
            AND ResourceIdInt > @ResourceIdInt
          ORDER BY
               ResourceIdInt
      SET @RowsToProcess = @@rowcount
      SET @ProcessedRows += @RowsToProcess

      IF @RowsToProcess > 0
        SET @ResourceIdInt = (SELECT max(ResourceIdInt) FROM @ResourceIdInts)

      SET @LastProcessed = convert(varchar,@ResourceTypeId)+'.'+convert(varchar,@ResourceIdInt)

      DELETE FROM A FROM @ResourceIdInts A WHERE EXISTS (SELECT * FROM dbo.CurrentResources B WHERE B.ResourceTypeId = @ResourceTypeId AND B.ResourceIdInt = A.ResourceIdInt)
      EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Run',@Target='@ResourceIdInts.Current',@Action='Delete',@Rows=@@rowcount,@Text=@LastProcessed

      DELETE FROM A FROM @ResourceIdInts A WHERE EXISTS (SELECT * FROM dbo.HistoryResources B WHERE B.ResourceTypeId = @ResourceTypeId AND B.ResourceIdInt = A.ResourceIdInt)
      EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Run',@Target='@ResourceIdInts.History',@Action='Delete',@Rows=@@rowcount,@Text=@LastProcessed

      DELETE FROM A FROM @ResourceIdInts A WHERE EXISTS (SELECT * FROM dbo.ResourceReferenceSearchParams B WHERE B.ReferenceResourceTypeId = @ResourceTypeId AND B.ReferenceResourceIdInt = A.ResourceIdInt)
      EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Run',@Target='@ResourceIdInts.Reference',@Action='Delete',@Rows=@@rowcount,@Text=@LastProcessed

      IF EXISTS (SELECT * FROM @ResourceIdInts)
      BEGIN
        DELETE FROM A FROM dbo.ResourceIdIntMap A WHERE A.ResourceTypeId = @ResourceTypeId AND EXISTS (SELECT * FROM @ResourceIdInts B WHERE B.ResourceIdInt = A.ResourceIdInt)
        SET @DeletedRows += @@rowcount
      END

      UPDATE dbo.Parameters SET Char = @LastProcessed WHERE Id = @Id

      IF datediff(second, @ReportDate, getUTCdate()) > 60
      BEGIN
        EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Run',@Target='ResourceIdIntMap',@Action='Select',@Rows=@ProcessedRows,@Text=@LastProcessed
        SET @ReportDate = getUTCdate()
        SET @ProcessedRows = 0
      END
    END

    DELETE FROM @Types WHERE ResourceTypeId = @ResourceTypeId

    SET @ResourceIdInt = 0
  END

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Run',@Target='ResourceIdIntMap',@Action='Delete',@Rows=@DeletedRows,@Text=@LastProcessed

  IF @ResetAfter = 1 DELETE FROM dbo.Parameters WHERE Id = @Id

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
--EXECUTE dbo.CleanupResourceIdIntMap 1
--SELECT * FROM Parameters WHERE Id = 'CleanupResourceIdIntMap.LastProcessed.TypeId.ResourceIdInt'
--SELECT TOP 100 * FROM EventLog WHERE EventDate > dateadd(minute,-10,getUTCdate()) AND Process = 'CleanupResourceIdIntMap' ORDER BY EventDate DESC
--INSERT INTO Parameters (Id, Char) SELECT 'CleanupResourceIdIntMap','LogEvent'
