INSERT INTO dbo.Parameters (Id, Char) SELECT 'SearchParamsDeleteHistoryV2', 'LogEvent'
EXECUTE dbo.LogEvent @Process='SearchParamsDeleteHistoryV2',@Status='Start'

DECLARE @Types TABLE (ResourceTypeId smallint PRIMARY KEY, Name varchar(100))
DECLARE @Tables TABLE (Name varchar(100))

DECLARE @ResourceTypeId smallint
       ,@Process varchar(100) = 'SearchParamsDeleteHistoryV2'
       ,@Id varchar(100) = 'SearchParamsDeleteHistoryV2.LastProcessed.TypeId.SurrogateId.SearchParamId'
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

BEGIN TRY
    INSERT INTO dbo.Parameters (Id, Char) SELECT @Id, '0.0.0' WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = @Id)
    SET @LastProcessed = (SELECT Char FROM dbo.Parameters WHERE Id = @Id)

    INSERT INTO @Tables SELECT name FROM sys.objects WHERE type = 'u' AND name LIKE '%SearchParam' AND name <> 'SearchParam' ORDER BY name
  
    SET @Table = (SELECT value FROM string_split(@LastProcessed, '.', 1) WHERE ordinal = 1) --substring(@LastProcessed, 1, charindex('.', @LastProcessed) - 1)
    DELETE FROM @Tables WHERE Name < @Table
    EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Target='@Tables',@Action='Delete',@Rows=@@rowcount

    SET @ResourceTypeId = (SELECT value FROM string_split(@LastProcessed, '.', 1) WHERE ordinal = 2) --substring(@LastProcessed, 1, charindex('.', @LastProcessed) - 1)
    SET @SurrogateId = (SELECT value FROM string_split(@LastProcessed, '.', 1) WHERE ordinal = 3) --substring(@LastProcessed, charindex('.', @LastProcessed) + 1, 255)

    WHILE EXISTS (SELECT * FROM @Tables) -- Processing in ASC order
    BEGIN    
        SET @Table = (SELECT TOP 1 Name FROM @Tables ORDER BY Name)

        INSERT INTO @Types EXECUTE dbo.GetUsedResourceTypes
        DELETE FROM @Types WHERE ResourceTypeId < @ResourceTypeId

        WHILE EXISTS (SELECT * FROM @Types) -- Processing in ASC order
        BEGIN
            SET @ResourceTypeId = (SELECT TOP 1 ResourceTypeId FROM @Types ORDER BY ResourceTypeId)

            SET @CurrentMaxSurrogateId = 0
            WHILE @CurrentMaxSurrogateId IS NOT NULL
            BEGIN -- @SurrogateId
                TRUNCATE TABLE #Ids
                SET @st = getUTCdate()
                SET @SQL = N'
                INSERT INTO #Ids
                SELECT TOP 100000 ResourceSurrogateId
                FROM dbo.'+@Table+' WITH (INDEX = 1)
                WHERE ResourceTypeId = @ResourceTypeId 
                    AND ResourceSurrogateId >= @SurrogateId
                    AND IsHistory = 1
                ORDER BY ResourceSurrogateId, SearchParamId'

                EXECUTE sp_executeSQL @SQL, N'@ResourceTypeId smallint, @SurrogateId bigint', @ResourceTypeId = @ResourceTypeId, @SurrogateId = @SurrogateId

                SET @CurrentMaxSurrogateId = NULL
                SELECT @CurrentMaxSurrogateId = max(ResourceSurrogateId) FROM #Ids
                
                IF @CurrentMaxSurrogateId IS NOT NULL
                BEGIN
                    SET @SQL = N'
                    DELETE FROM dbo.'+@Table+' 
                    WHERE ResourceTypeId = @ResourceTypeId 
                        AND ResourceSurrogateId >= @SurrogateId 
                        AND ResourceSurrogateId <= @CurrentMaxSurrogateId 
                        AND ResourceSurrogateId IN (
                            SELECT ResourceSurrogateId FROM dbo.Resource 
                            WHERE IsHistory = 1 
                                AND ResourceSurrogateId >= @SurrogateId 
                                AND ResourceSurrogateId <= @CurrentMaxSurrogateId)'

                    EXECUTE sp_executeSQL @SQL, N'@ResourceTypeId smallint, @SurrogateId bigint, @CurrentMaxSurrogateId bigint', @ResourceTypeId = @ResourceTypeId, @SurrogateId = @SurrogateId, @CurrentMaxSurrogateId = @CurrentMaxSurrogateId

                    SET @SurrogateId = @CurrentMaxSurrogateId
                END
                UPDATE dbo.Parameters SET Char = @Table+'.'+convert(varchar,@ResourceTypeId)+'.'+convert(varchar,isnull(@CurrentMaxSurrogateId,@MaxSurrogateId)) WHERE Id = @Id
            END -- @SurrogateId
            
            DELETE FROM @Types WHERE ResourceTypeId = @ResourceTypeId

            SET @SurrogateId = 0
        END -- @Types

        DELETE FROM @Tables WHERE Name = @Table
        
        SET @ResourceTypeId = 0
    END -- @Tables
END TRY
BEGIN CATCH
    IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
    EXECUTE dbo.LogEvent @Process='SearchParamsDeleteHistoryV2',@Status='Error';
    THROW
END CATCH

EXECUTE dbo.LogEvent @Process='SearchParamsDeleteHistoryV2',@Status='End'
