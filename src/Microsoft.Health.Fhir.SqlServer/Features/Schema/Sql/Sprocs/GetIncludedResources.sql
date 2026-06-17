--DROP PROCEDURE dbo.GetIncludedResources
GO
CREATE PROCEDURE dbo.GetIncludedResources
    @SourceResources dbo.ResourceKeyList READONLY,              -- Original search results
    @IterateSourceResources dbo.ResourceKeyList READONLY,       -- Resources from previous includes (for iterate operations)
    @IncludeSpecifications dbo.IncludeSpecificationList READONLY,
    @IncludeCount int,
    @LastCompletedIncludeId int = NULL,                         -- Last fully completed include spec
    @IncludesContinuationToken varchar(500) = NULL              -- Position within current include spec
AS
set nocount on

DECLARE @st datetime = getUTCdate()
       ,@SP varchar(100) = 'GetIncludedResources'
       ,@Mode varchar(200)
       ,@InputRows int
       ,@IterateSourceRows int
       ,@IncludeSpecCount int
       ,@ContinuationResourceTypeId smallint = NULL
       ,@ContinuationResourceSurrogateId bigint = NULL

SELECT @InputRows = count(*) FROM @SourceResources
SELECT @IterateSourceRows = count(*) FROM @IterateSourceResources
SELECT @IncludeSpecCount = count(*) FROM @IncludeSpecifications

-- Parse continuation token if provided (format: "ResourceTypeId|ResourceSurrogateId")
-- This token only tracks position within the CURRENT include spec being processed
IF @IncludesContinuationToken IS NOT NULL AND @IncludesContinuationToken <> ''
BEGIN
    DECLARE @TokenParts TABLE (PartIndex int, PartValue varchar(250))

    INSERT INTO @TokenParts
    SELECT ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1, value
    FROM STRING_SPLIT(@IncludesContinuationToken, '|')

    SELECT @ContinuationResourceTypeId = CAST(PartValue AS smallint)
    FROM @TokenParts WHERE PartIndex = 0

    SELECT @ContinuationResourceSurrogateId = CAST(PartValue AS bigint)
    FROM @TokenParts WHERE PartIndex = 1
END

SET @Mode = 'Sources=' + CONVERT(varchar, @InputRows) + ' IterSrc=' + CONVERT(varchar, @IterateSourceRows) + ' Specs=' + CONVERT(varchar, @IncludeSpecCount) + ' Count=' + CONVERT(varchar, @IncludeCount)

BEGIN TRY
    -- Combined source set: original sources + iterate sources from previous pages
    DECLARE @AllSourceResources TABLE
    (
        ResourceTypeId smallint NOT NULL,
        ResourceSurrogateId bigint NOT NULL,
        ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
        IsIterateSource bit NOT NULL,  -- 1 if from previous page's includes, 0 if original
        PRIMARY KEY (ResourceTypeId, ResourceSurrogateId)
    )

    -- Output table for current page only
    DECLARE @PageResults TABLE
    (
        ResourceTypeId smallint NOT NULL,
        ResourceSurrogateId bigint NOT NULL,
        ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
        IncludeId int NOT NULL,
        ExecutionLevel int NOT NULL,
        PRIMARY KEY (ResourceTypeId, ResourceSurrogateId)
    )

    -- Load original search sources (for non-iterate includes)
    INSERT INTO @AllSourceResources (ResourceTypeId, ResourceSurrogateId, ResourceId, IsIterateSource)
    SELECT DISTINCT 
        s.ResourceTypeId, 
        r.ResourceSurrogateId,
        r.ResourceId,
        0  -- Original sources
    FROM @SourceResources s
    JOIN dbo.Resource r WITH (INDEX = IX_Resource_ResourceTypeId_ResourceId)
        ON r.ResourceTypeId = s.ResourceTypeId 
        AND r.ResourceId = s.ResourceId
        AND r.IsHistory = 0
        AND r.IsDeleted = 0

    -- Load iterate sources from previous pages (for iterate includes)
    -- These are resources found by includes in earlier pages that may be referenced by iterate includes
    INSERT INTO @AllSourceResources (ResourceTypeId, ResourceSurrogateId, ResourceId, IsIterateSource)
    SELECT DISTINCT 
        s.ResourceTypeId, 
        r.ResourceSurrogateId,
        r.ResourceId,
        1  -- Iterate sources
    FROM @IterateSourceResources s
    JOIN dbo.Resource r WITH (INDEX = IX_Resource_ResourceTypeId_ResourceId)
        ON r.ResourceTypeId = s.ResourceTypeId 
        AND r.ResourceId = s.ResourceId
        AND r.IsHistory = 0
        AND r.IsDeleted = 0
    WHERE NOT EXISTS (
        SELECT 1 FROM @AllSourceResources asr
        WHERE asr.ResourceTypeId = r.ResourceTypeId
        AND asr.ResourceSurrogateId = r.ResourceSurrogateId
    )

    -- Determine execution order for include specifications
    DECLARE @ExecutionOrder TABLE
    (
        ExecutionLevel int NOT NULL,
        IncludeId int NOT NULL,
        SourceResourceTypeId smallint NOT NULL,
        SearchParamId smallint NULL,
        TargetResourceTypeId smallint NULL,
        IsReversed bit NOT NULL,
        IsIterate bit NOT NULL,
        IsWildCard bit NOT NULL,
        PRIMARY KEY (ExecutionLevel, IncludeId)
    )

    -- Level 0: All non-iterate includes
    INSERT INTO @ExecutionOrder
    SELECT 
        0 as ExecutionLevel,
        IncludeId,
        SourceResourceTypeId,
        SearchParamId,
        TargetResourceTypeId,
        IsReversed,
        IsIterate,
        IsWildCard
    FROM @IncludeSpecifications
    WHERE IsIterate = 0

    -- Level 1+: Iterate includes in dependency order
    DECLARE @CurrentExecLevel int = 1
    DECLARE @MaxExecLevel int = 10
    DECLARE @RemainingIterates int = (SELECT COUNT(*) FROM @IncludeSpecifications WHERE IsIterate = 1)

    WHILE @RemainingIterates > 0 AND @CurrentExecLevel <= @MaxExecLevel
    BEGIN
        INSERT INTO @ExecutionOrder
        SELECT TOP (@RemainingIterates)
            @CurrentExecLevel as ExecutionLevel,
            i.IncludeId,
            i.SourceResourceTypeId,
            i.SearchParamId,
            i.TargetResourceTypeId,
            i.IsReversed,
            i.IsIterate,
            i.IsWildCard
        FROM @IncludeSpecifications i
        WHERE i.IsIterate = 1
        AND NOT EXISTS (
            SELECT 1 FROM @ExecutionOrder eo WHERE eo.IncludeId = i.IncludeId
        )

        DECLARE @AddedCount int = @@ROWCOUNT
        IF @AddedCount = 0
            BREAK

        SET @RemainingIterates = @RemainingIterates - @AddedCount
        SET @CurrentExecLevel = @CurrentExecLevel + 1
    END

    -- Processing state
    DECLARE @CurrentIncludeId int
    DECLARE @CurrentExecutionLevel int
    DECLARE @CurrentSourceType smallint
    DECLARE @CurrentSearchParamId smallint
    DECLARE @CurrentTargetType smallint
    DECLARE @CurrentIsReversed bit
    DECLARE @CurrentIsIterate bit
    DECLARE @CurrentIsWildCard bit
    DECLARE @PageFull bit = 0
    DECLARE @RemainingSlots int = @IncludeCount + 1

    -- Start from first include after last completed, or from beginning
    DECLARE @StartIncludeId int = ISNULL(@LastCompletedIncludeId, -1) + 1

    -- Process includes in order until page is full
    DECLARE include_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT IncludeId, ExecutionLevel, SourceResourceTypeId, SearchParamId, TargetResourceTypeId, 
           IsReversed, IsIterate, IsWildCard
    FROM @ExecutionOrder
    WHERE IncludeId >= @StartIncludeId
    ORDER BY ExecutionLevel, IncludeId

    OPEN include_cursor
    FETCH NEXT FROM include_cursor INTO 
        @CurrentIncludeId, @CurrentExecutionLevel, @CurrentSourceType, @CurrentSearchParamId, 
        @CurrentTargetType, @CurrentIsReversed, @CurrentIsIterate, @CurrentIsWildCard

    WHILE @@FETCH_STATUS = 0 AND @PageFull = 0
    BEGIN
        -- Determine which sources to use for this include
        -- Non-iterate: Use only original sources
        -- Iterate: Use both original AND iterate sources (from previous pages)
        DECLARE @UseIterateSources bit = @CurrentIsIterate

        IF @CurrentIsReversed = 0
        BEGIN
            -- Forward include (_include)
            INSERT INTO @PageResults (ResourceTypeId, ResourceSurrogateId, ResourceId, IncludeId, ExecutionLevel)
            SELECT TOP (@RemainingSlots)
                target.ResourceTypeId,
                target.ResourceSurrogateId,
                target.ResourceId,
                @CurrentIncludeId,
                @CurrentExecutionLevel
            FROM @AllSourceResources asr
            JOIN dbo.ReferenceSearchParam ref WITH (INDEX = IXC_ReferenceSearchParam)
                ON ref.ResourceTypeId = asr.ResourceTypeId
                AND ref.ResourceSurrogateId = asr.ResourceSurrogateId
            JOIN dbo.Resource target WITH (INDEX = IX_Resource_ResourceTypeId_ResourceId)
                ON target.ResourceTypeId = ref.ReferenceResourceTypeId
                AND target.ResourceId = ref.ReferenceResourceId
                AND target.IsHistory = 0
                AND target.IsDeleted = 0
            WHERE 
                -- For non-iterate, only use original sources; for iterate, use all sources
                (@UseIterateSources = 1 OR asr.IsIterateSource = 0)
                AND (@CurrentSourceType IS NULL OR asr.ResourceTypeId = @CurrentSourceType)
                AND (@CurrentIsWildCard = 1 OR @CurrentSearchParamId IS NULL OR ref.SearchParamId = @CurrentSearchParamId)
                AND (@CurrentTargetType IS NULL OR target.ResourceTypeId = @CurrentTargetType)
                -- Resume from continuation point if this is the first include after last completed
                AND (@CurrentIncludeId > @StartIncludeId
                     OR @ContinuationResourceTypeId IS NULL 
                     OR target.ResourceTypeId > @ContinuationResourceTypeId
                     OR (target.ResourceTypeId = @ContinuationResourceTypeId AND target.ResourceSurrogateId > @ContinuationResourceSurrogateId))
                -- Exclude resources already in page
                AND NOT EXISTS (
                    SELECT 1 FROM @PageResults pr
                    WHERE pr.ResourceTypeId = target.ResourceTypeId
                    AND pr.ResourceSurrogateId = target.ResourceSurrogateId
                )
                -- Exclude original and iterate sources (don't return them again)
                AND NOT EXISTS (
                    SELECT 1 FROM @AllSourceResources asr2
                    WHERE asr2.ResourceTypeId = target.ResourceTypeId
                    AND asr2.ResourceSurrogateId = target.ResourceSurrogateId
                )
            ORDER BY target.ResourceTypeId, target.ResourceSurrogateId
        END
        ELSE
        BEGIN
            -- Reverse include (_revinclude)
            INSERT INTO @PageResults (ResourceTypeId, ResourceSurrogateId, ResourceId, IncludeId, ExecutionLevel)
            SELECT TOP (@RemainingSlots)
                source.ResourceTypeId,
                source.ResourceSurrogateId,
                source.ResourceId,
                @CurrentIncludeId,
                @CurrentExecutionLevel
            FROM @AllSourceResources asr
            JOIN dbo.ReferenceSearchParam ref WITH (INDEX = IXU_ReferenceResourceId_ReferenceResourceTypeId_SearchParamId_BaseUri_ResourceSurrogateId_ResourceTypeId)
                ON ref.ReferenceResourceTypeId = asr.ResourceTypeId
                AND ref.ReferenceResourceId = asr.ResourceId
            JOIN dbo.Resource source WITH (INDEX = IX_Resource_ResourceTypeId_ResourceId)
                ON source.ResourceTypeId = ref.ResourceTypeId
                AND source.ResourceSurrogateId = ref.ResourceSurrogateId
                AND source.IsHistory = 0
                AND source.IsDeleted = 0
            WHERE 
                -- For non-iterate, only use original sources; for iterate, use all sources
                (@UseIterateSources = 1 OR asr.IsIterateSource = 0)
                AND (@CurrentTargetType IS NULL OR asr.ResourceTypeId = @CurrentTargetType)
                AND (@CurrentSourceType IS NULL OR source.ResourceTypeId = @CurrentSourceType)
                AND (@CurrentIsWildCard = 1 OR @CurrentSearchParamId IS NULL OR ref.SearchParamId = @CurrentSearchParamId)
                -- Resume from continuation point if this is the first include after last completed
                AND (@CurrentIncludeId > @StartIncludeId
                     OR @ContinuationResourceTypeId IS NULL 
                     OR source.ResourceTypeId > @ContinuationResourceTypeId
                     OR (source.ResourceTypeId = @ContinuationResourceTypeId AND source.ResourceSurrogateId > @ContinuationResourceSurrogateId))
                -- Exclude resources already in page
                AND NOT EXISTS (
                    SELECT 1 FROM @PageResults pr
                    WHERE pr.ResourceTypeId = source.ResourceTypeId
                    AND pr.ResourceSurrogateId = source.ResourceSurrogateId
                )
                -- Exclude original and iterate sources
                AND NOT EXISTS (
                    SELECT 1 FROM @AllSourceResources asr2
                    WHERE asr2.ResourceTypeId = source.ResourceTypeId
                    AND asr2.ResourceSurrogateId = source.ResourceSurrogateId
                )
            ORDER BY source.ResourceTypeId, source.ResourceSurrogateId
        END

        -- Update remaining slots
        SET @RemainingSlots = (@IncludeCount + 1) - (SELECT COUNT(*) FROM @PageResults)
        IF @RemainingSlots <= 0
            SET @PageFull = 1

        FETCH NEXT FROM include_cursor INTO 
            @CurrentIncludeId, @CurrentExecutionLevel, @CurrentSourceType, @CurrentSearchParamId, 
            @CurrentTargetType, @CurrentIsReversed, @CurrentIsIterate, @CurrentIsWildCard
    END

    CLOSE include_cursor
    DEALLOCATE include_cursor

    -- Return the page of results
    SELECT 
        r.ResourceTypeId,
        r.ResourceId,
        r.ResourceSurrogateId,
        r.Version,
        r.IsDeleted,
        r.IsHistory,
        r.RawResource,
        r.IsRawResourceMetaSet,
        r.SearchParamHash
    FROM @PageResults pr
    JOIN dbo.Resource r
        ON r.ResourceTypeId = pr.ResourceTypeId
        AND r.ResourceSurrogateId = pr.ResourceSurrogateId
    ORDER BY pr.IncludeId, pr.ResourceTypeId, pr.ResourceSurrogateId
    OPTION (MAXDOP 1)

    EXECUTE dbo.LogEvent @Process=@SP, @Mode=@Mode, @Status='End', @Start=@st, @Rows=@@rowcount

END TRY
BEGIN CATCH
    IF error_number() = 1750 THROW

    EXECUTE dbo.LogEvent @Process=@SP, @Mode=@Mode, @Status='Error', @Start=@st

    DECLARE @ErrorMessage nvarchar(4000) = ERROR_MESSAGE()
    DECLARE @ErrorSeverity int = ERROR_SEVERITY()
    DECLARE @ErrorState int = ERROR_STATE()

    RAISERROR(@ErrorMessage, @ErrorSeverity, @ErrorState)
END CATCH

GO
