CREATE PROCEDURE dbo.HardDeleteSearchParameter
    @SearchParameterUrl varchar(256)
AS
SET NOCOUNT ON

DECLARE @SP varchar(100) = object_name(@@PROCID)
DECLARE @Mode varchar(200) = 'URL=' + @SearchParameterUrl
DECLARE @st datetime = getutcdate()
DECLARE @ResourceTypeId smallint
DECLARE @ResourceId varchar(64)
DECLARE @DeletedVersionCount int = 0
DECLARE @TotalDeletedVersionCount int = 0
DECLARE @EventText nvarchar(3500)
DECLARE @RawResourceJson nvarchar(max)
DECLARE @ResourceCount int = 0

-- Table variable to store all matching ResourceIds
DECLARE @ResourceIdsToDelete TABLE (
    ResourceId varchar(64),
    ResourceSurrogateId bigint
)

BEGIN TRY
    -- Validate input
    IF @SearchParameterUrl IS NULL OR len(@SearchParameterUrl) = 0
    BEGIN
        SET @EventText = 'SearchParameter URL parameter is required'
        EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st, @Text = @EventText;
        THROW 50400, 'SearchParameter URL is required', 1
    END

    -- Get the ResourceTypeId for SearchParameter
    SELECT @ResourceTypeId = ResourceTypeId 
    FROM dbo.ResourceType 
    WHERE Name = 'SearchParameter'

    -- Validate that SearchParameter resource type exists
    IF @ResourceTypeId IS NULL
    BEGIN
        SET @EventText = 'SearchParameter resource type not found in ResourceType table'
        EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st, @Text = @EventText;
        THROW 50404, 'SearchParameter resource type not found', 1
    END

    -- Step 1: Find all ResourceIds by searching for the URL in the resource JSON
    -- Try active (non-deleted, non-history) versions first
    INSERT INTO @ResourceIdsToDelete (ResourceId, ResourceSurrogateId)
    SELECT 
       r.ResourceId,
       r.ResourceSurrogateId
    FROM
        (SELECT ResourceId,            
                FhirResource =
                CASE 
                    WHEN RawResource != 0xF
                    THEN cast(decompress(RawResource) AS varchar(max))
                    ELSE NULL
                END, 
                ResourceSurrogateId
          FROM dbo.Resource
          WHERE ResourceTypeId = @ResourceTypeId
            AND IsDeleted      = 0
            AND IsHistory      = 0) r
    WHERE r.FhirResource LIKE '%' + @SearchParameterUrl + '%' COLLATE Latin1_General_CS_AS

    -- If not found in active version, try historical versions (handles soft-delete scenario)
    IF NOT EXISTS (SELECT 1 FROM @ResourceIdsToDelete)
    BEGIN
        INSERT INTO @ResourceIdsToDelete (ResourceId, ResourceSurrogateId)
        SELECT 
           r.ResourceId,
           r.ResourceSurrogateId
        FROM
            (SELECT ResourceId,            
                    FhirResource =
                    CASE 
                        WHEN RawResource != 0xF
                        THEN cast(decompress(RawResource) AS varchar(max))
                        ELSE NULL
                    END, 
                    ResourceSurrogateId
              FROM dbo.Resource
              WHERE ResourceTypeId = @ResourceTypeId
                AND IsDeleted      = 0
                AND IsHistory      = 1) r
        WHERE r.FhirResource LIKE '%' + @SearchParameterUrl + '%' COLLATE Latin1_General_CS_AS        
        ORDER BY r.ResourceSurrogateId DESC

        IF EXISTS (SELECT 1 FROM @ResourceIdsToDelete)
        BEGIN
            SET @EventText = 'Found SearchParameter(s) from historical versions (IsDeleted=0, IsHistory=1) - soft-deleted scenario'
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Start = @st, @Text = @EventText
        END
    END
    ELSE
    BEGIN
        SET @EventText = 'Found SearchParameter(s) from active resources (IsDeleted=0, IsHistory=0)'
        EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Start = @st, @Text = @EventText
    END

    -- Get the count of resources to delete
    SELECT @ResourceCount = count(*) FROM @ResourceIdsToDelete

    -- Log if no resources found, but continue to delete from SearchParam table
    IF @ResourceCount = 0
    BEGIN
        SET @EventText = 'No SearchParameter resources found for URL: ' + @SearchParameterUrl + ', will attempt to delete from SearchParam registry'
        EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Start = @st, @Text = @EventText
    END
    ELSE
    BEGIN
        -- Log the start of the operation
        SET @EventText = 'Deleting ' + convert(varchar, @ResourceCount) + ' SearchParameter resource(s) for URL: ' + @SearchParameterUrl
        EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Start', @Start = @st, @Text = @EventText
    END

    -- *** BEGIN TRANSACTION - Start the atomic operation ***
    BEGIN TRANSACTION

    -- Step 2: Delete each SearchParameter resource and all its search parameter indices (if any found)
    IF @ResourceCount > 0
    BEGIN
        DECLARE resource_cursor CURSOR LOCAL FAST_FORWARD FOR
            SELECT ResourceId FROM @ResourceIdsToDelete

        OPEN resource_cursor
        FETCH NEXT FROM resource_cursor INTO @ResourceId

        WHILE @@FETCH_STATUS = 0
        BEGIN
            -- Count how many versions exist for this resource (for logging)
            SELECT @DeletedVersionCount = count(*)
            FROM dbo.Resource
            WHERE ResourceTypeId = @ResourceTypeId 
              AND ResourceId = @ResourceId

            -- Delete the SearchParameter resource using standard hard delete parameters
            EXECUTE dbo.HardDeleteResource 
                @ResourceTypeId = @ResourceTypeId,
                @ResourceId = @ResourceId,
                @KeepCurrentVersion = 0,  -- Delete all versions
                @IsResourceChangeCaptureEnabled = 0  -- Standard hard delete

            SET @TotalDeletedVersionCount = @TotalDeletedVersionCount + @DeletedVersionCount

            SET @EventText = 'Deleted ' + convert(varchar, @DeletedVersionCount) + ' version(s) for ResourceId: ' + @ResourceId
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Start = @st, @Text = @EventText

            FETCH NEXT FROM resource_cursor INTO @ResourceId
        END

        CLOSE resource_cursor
        DEALLOCATE resource_cursor
    END

    -- Step 3: Delete the SearchParam registry entry with case-sensitive comparison
    -- This always runs, regardless of whether we found resources or not
    DELETE FROM dbo.SearchParam 
    WHERE Uri = @SearchParameterUrl COLLATE Latin1_General_CS_AS
    
    IF @@ROWCOUNT > 0
    BEGIN
        SET @EventText = 'Successfully deleted SearchParam registry entry for URL: ' + @SearchParameterUrl
    END
    ELSE
    BEGIN
        -- This is a warning, not an error - the entry may have already been deleted
        SET @EventText = 'WARNING: No SearchParam registry entry found for URL: ' + @SearchParameterUrl + ' (may have been deleted previously)'
    END

    -- *** COMMIT TRANSACTION - All operations succeeded ***
    COMMIT TRANSACTION

    -- Log successful completion
    SET @EventText = @EventText + ' | Deleted ' + convert(varchar, @TotalDeletedVersionCount) + ' total resource version(s) across ' + convert(varchar, @ResourceCount) + ' resource(s)'
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Text = @EventText, @Rows = @TotalDeletedVersionCount

    -- Return summary information
    SELECT 
        @SearchParameterUrl AS DeletedSearchParameterUrl,
        @ResourceCount AS DeletedResourceCount,
        @TotalDeletedVersionCount AS TotalDeletedVersionCount

END TRY
BEGIN CATCH
    -- *** ROLLBACK TRANSACTION - Something failed, undo all changes ***
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION

    -- Close and deallocate cursor if still open
    IF cursor_status('local', 'resource_cursor') >= 0
    BEGIN
        CLOSE resource_cursor
        DEALLOCATE resource_cursor
    END

    -- Log the error
    DECLARE @ErrorMessage nvarchar(4000) = error_message()
    DECLARE @ErrorSeverity int = error_severity()
    DECLARE @ErrorState int = error_state()
    
    SET @EventText = 'HardDelete failed for SearchParameter URL: ' + @SearchParameterUrl + 
                     CASE WHEN @ResourceId IS NOT NULL THEN ' (last processed ResourceId: ' + @ResourceId + ')' ELSE '' END + 
                     '. Error: ' + @ErrorMessage
    
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st, @Text = @EventText;
    THROW
END CATCH
GO
