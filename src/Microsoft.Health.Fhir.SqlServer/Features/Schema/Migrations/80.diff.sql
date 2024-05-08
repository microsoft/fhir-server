set nocount on

INSERT INTO dbo.Parameters (Id, Char) SELECT 'SearchParamsDeleteHistory', 'LogEvent'
EXECUTE dbo.LogEvent @Process='SearchParamsDeleteHistory',@Status='Start'

DECLARE @Types TABLE (ResourceTypeId smallint PRIMARY KEY, Name varchar(100))
DECLARE @Tables TABLE (Name varchar(100))

DECLARE @ResourceTypeId smallint
       ,@Process varchar(100) = 'SearchParamsDeleteHistory'
       ,@Id varchar(100) = 'SearchParamsDeleteHistory.LastProcessed.TypeId.SurrogateId.SearchParamId'
       ,@SurrogateId bigint
       ,@Rows int
       ,@MaxSurrogateId bigint = datediff_big(millisecond,'0001-01-01',sysUTCdatetime()) * 80000
       ,@CurrentMaxSurrogateId bigint
       ,@LastProcessed varchar(100)
       ,@st datetime
       ,@Table varchar(100)
       ,@SQL nvarchar(max)
       ,@ClusteredIndexRows bigint
       ,@FilteredIndexRows bigint

-- restart
--DELETE FROM Parameters WHERE Id = @Id

IF object_id('tempdb..#ids') IS NULL
  SELECT ResourceSurrogateId INTO #Ids FROM dbo.ReferenceSearchParam WHERE 1 = 2

BEGIN TRY
  INSERT INTO dbo.Parameters (Id, Char) SELECT @Id, '0.0.0' WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = @Id)

  SET @LastProcessed = (SELECT Char FROM dbo.Parameters WHERE Id = @Id)

  -- Ignore previously updated quantity and number tables
  INSERT INTO @Tables SELECT name FROM sys.objects WHERE type = 'u' AND name LIKE '%SearchParam' AND name <> 'SearchParam' AND name NOT LIKE '%Number%' AND name NOT LIKE '%Quantity%' ORDER BY name
  
  SET @Table = (SELECT value FROM string_split(@LastProcessed, '.', 1) WHERE ordinal = 1) --substring(@LastProcessed, 1, charindex('.', @LastProcessed) - 1)
  DELETE FROM @Tables WHERE Name < @Table
  EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Target='@Tables',@Action='Delete',@Rows=@@rowcount

  SET @ResourceTypeId = (SELECT value FROM string_split(@LastProcessed, '.', 1) WHERE ordinal = 2) --substring(@LastProcessed, 1, charindex('.', @LastProcessed) - 1)
  SET @SurrogateId = (SELECT value FROM string_split(@LastProcessed, '.', 1) WHERE ordinal = 3) --substring(@LastProcessed, charindex('.', @LastProcessed) + 1, 255)

  WHILE EXISTS (SELECT * FROM @Tables) -- Processing in ASC order
  BEGIN
    SET @Table = (SELECT TOP 1 Name FROM @Tables ORDER BY Name)

    INSERT INTO @Types EXECUTE dbo.GetUsedResourceTypes
    EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Target='@Types',@Action='Insert',@Rows=@@rowcount

    DELETE FROM @Types WHERE ResourceTypeId < @ResourceTypeId
    EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Target='@Types',@Action='Delete',@Rows=@@rowcount

    WHILE EXISTS (SELECT * FROM @Types) -- Processing in ASC order
    BEGIN
      SET @ResourceTypeId = (SELECT TOP 1 ResourceTypeId FROM @Types ORDER BY ResourceTypeId)
      SET @LastProcessed = @Table+'.'+convert(varchar,@ResourceTypeId)+'.'+convert(varchar,@SurrogateId)

      SET @FilteredIndexRows = (SELECT sum(row_count) 
                                  FROM sys.dm_db_partition_stats 
                                  WHERE object_id = object_id(@Table) 
                                    AND index_id > 1 
                                    AND index_id = (SELECT TOP 1 index_id FROM sys.indexes WHERE object_id = object_id(@Table) AND index_id > 1 AND replace(replace(replace(replace(filter_definition,'[',''),']',''),'(',''),')','') = 'IsHistory=0')
                                    AND partition_number = $PARTITION.PartitionFunction_ResourceTypeId(@ResourceTypeId)
                                  GROUP BY 
                                       index_id
                               )
      EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Mode=@LastProcessed,@Target=@Table,@Action='Count',@Rows=@FilteredIndexRows,@Text='Filtered Index'
      SET @ClusteredIndexRows = (SELECT sum(row_count) FROM sys.dm_db_partition_stats WHERE object_id = object_id(@Table) AND index_id = 1 AND partition_number = $PARTITION.PartitionFunction_ResourceTypeId(@ResourceTypeId))
      EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Mode=@LastProcessed,@Target=@Table,@Action='Count',@Rows=@ClusteredIndexRows,@Text='Clustered'

      IF @ClusteredIndexRows = @FilteredIndexRows
      BEGIN
        EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Mode=@LastProcessed,@Target=@Table,@Action='Count',@Rows=@ClusteredIndexRows,@Text='Clustered=Filteted'
        UPDATE dbo.Parameters SET Char = @Table+'.'+convert(varchar,@ResourceTypeId)+'.'+convert(varchar,@MaxSurrogateId) WHERE Id = @Id
      END
      ELSE
      BEGIN
        SET @CurrentMaxSurrogateId = 0
        WHILE @CurrentMaxSurrogateId IS NOT NULL
        BEGIN -- @SurrogateId
          SET @LastProcessed = @Table+'.'+convert(varchar,@ResourceTypeId)+'.'+convert(varchar,@SurrogateId)
        
          TRUNCATE TABLE #Ids
          SET @st = getUTCdate()
          SET @SQL = N'
INSERT INTO #Ids
  SELECT TOP 100000 
         ResourceSurrogateId
    FROM dbo.'+@Table+' WITH (INDEX = 1)
    WHERE ResourceTypeId = @ResourceTypeId 
      AND ResourceSurrogateId >= @SurrogateId
      AND IsHistory = 1
    ORDER BY 
         ResourceSurrogateId, SearchParamId
            '
          EXECUTE sp_executeSQL @SQL, N'@ResourceTypeId smallint, @SurrogateId bigint', @ResourceTypeId = @ResourceTypeId, @SurrogateId = @SurrogateId
          EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Mode=@LastProcessed,@Target=@Table,@Action='Select',@Rows=@@rowcount,@Start=@st

          SET @CurrentMaxSurrogateId = NULL
          SELECT @CurrentMaxSurrogateId = max(ResourceSurrogateId) FROM #Ids

          IF @CurrentMaxSurrogateId IS NOT NULL
          BEGIN
            SET @LastProcessed = @Table+'.'+convert(varchar,@ResourceTypeId)+'.'+convert(varchar,@CurrentMaxSurrogateId)

            SET @st = getUTCdate()
            SET @SQL = N'
DELETE FROM dbo.'+@Table+'
  WHERE ResourceTypeId = @ResourceTypeId 
  AND ResourceSurrogateId >= @SurrogateId AND ResourceSurrogateId <= @CurrentMaxSurrogateId
  AND IsHistory = 1
            '
            EXECUTE sp_executeSQL @SQL, N'@ResourceTypeId smallint, @SurrogateId bigint, @CurrentMaxSurrogateId bigint', @ResourceTypeId = @ResourceTypeId, @SurrogateId = @SurrogateId, @CurrentMaxSurrogateId = @CurrentMaxSurrogateId
            EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Mode=@LastProcessed,@Target=@Table,@Action='Delete',@Rows=@@rowcount,@Start=@st

            SET @SurrogateId = @CurrentMaxSurrogateId
          END

          UPDATE dbo.Parameters SET Char = @Table+'.'+convert(varchar,@ResourceTypeId)+'.'+convert(varchar,isnull(@CurrentMaxSurrogateId,@MaxSurrogateId)) WHERE Id = @Id
        END -- @SurrogateId
      END -- skip if rowcounts match

      DELETE FROM @Types WHERE ResourceTypeId = @ResourceTypeId

      SET @SurrogateId = 0
    END -- @Types

    DELETE FROM @Tables WHERE Name = @Table

    SET @ResourceTypeId = 0
  END -- @Tables
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@Process,@Status='Error';
  THROW
END CATCH

EXECUTE dbo.LogEvent @Process='SearchParamsDeleteHistory',@Status='End'

--SELECT TOP 10000 * FROM EventLog WHERE EventDate > dateadd(hour,-1,getUTCdate()) AND Process='SearchParamsDeleteHistory' ORDER BY EventDate DESC, EventId DESC
--SELECT * FROM Parameters WHERE Id = 'SearchParamsDeleteHistory.LastProcessed.TypeId.SurrogateId.SearchParamId'
--SELECT ','+name FROM sys.columns WHERE object_id = object_id('TokenSearchParam') ORDER BY column_id
--INSERT INTO ReferenceSearchParam
--     (   
--         ResourceTypeId
--        ,ResourceSurrogateId
--        ,SearchParamId
--        ,BaseUri
--        ,ReferenceResourceTypeId
--        ,ReferenceResourceId
--        ,ReferenceResourceVersion
--        ,IsHistory 
--    )
--  SELECT TOP 100000 
--         ResourceTypeId
--        ,ResourceSurrogateId - 1e10
--        ,SearchParamId
--        ,BaseUri
--        ,ReferenceResourceTypeId
--        ,ReferenceResourceId
--        ,ReferenceResourceVersion
--        ,IsHistory = 1
--    FROM ReferenceSearchParam 
--    WHERE ResourceTypeId = 96 
--INSERT INTO TokenSearchParam
--     (   
--         ResourceTypeId
--        ,ResourceSurrogateId
--        ,SearchParamId
--        ,SystemId
--        ,Code
--        ,IsHistory
--        ,CodeOverflow
--    )
--  SELECT TOP 100000 
--         ResourceTypeId
--        ,ResourceSurrogateId - 1e10
--        ,SearchParamId
--        ,SystemId
--        ,Code
--        ,IsHistory = 1
--        ,CodeOverflow
--    FROM TokenSearchParam 
--    WHERE ResourceTypeId = 96 
