CREATE PROCEDURE dbo.HardDeleteSearchParameter
    @SearchParameterUrl VARCHAR(256)
AS
SET NOCOUNT ON;

DECLARE @SP VARCHAR(100) = OBJECT_NAME(@@PROCID);
DECLARE @Mode VARCHAR(200) = 'URL=' + @SearchParameterUrl;
DECLARE @st DATETIME = GETUTCDATE();
DECLARE @ResourceTypeId SMALLINT;
DECLARE @ResourceId VARCHAR(64);
DECLARE @DeletedVersionCount INT = 0;
DECLARE @EventText NVARCHAR(3500);
DECLARE @RawResourceJson NVARCHAR(MAX);

BEGIN TRY
    -- Validate input
    IF @SearchParameterUrl IS NULL OR LEN(@SearchParameterUrl) = 0
    BEGIN
        SET @EventText = 'SearchParameter URL parameter is required';
        EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st, @Text = @EventText;
        THROW 50400, 'SearchParameter URL is required', 1;
    END

    -- Get the ResourceTypeId for SearchParameter
    SELECT @ResourceTypeId = ResourceTypeId 
    FROM dbo.ResourceType 
    WHERE Name = 'SearchParameter';

    -- Validate that SearchParameter resource type exists
    IF @ResourceTypeId IS NULL
    BEGIN
        SET @EventText = 'SearchParameter resource type not found in ResourceType table';
        EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st, @Text = @EventText;
        THROW 50404, 'SearchParameter resource type not found', 1;
    END

    -- Step 1: Find the ResourceId by searching for the URL in the resource JSON
    -- Try active (non-deleted, non-history) version first
    SELECT TOP 1
        @ResourceId       = r.ResourceId,
        @RawResourceJson  = fr.FhirResource
    FROM dbo.Resource AS r
    CROSS APPLY (
        -- Safely decompress only if RawResource looks like GZip (0x1F8B)
        SELECT RawText =
            CASE 
                WHEN r.RawResource IS NOT NULL
                    AND SUBSTRING(r.RawResource, 1, 2) = 0x1F8B
                THEN CAST(DECOMPRESS(r.RawResource) AS VARCHAR(MAX))
                ELSE NULL
            END
    ) AS x
    CROSS APPLY (
        -- Strip UTF-8 BOM if present
        SELECT FhirResource =
            CASE 
                WHEN x.RawText IS NOT NULL
                    AND LEFT(x.RawText, 3) = CHAR(0xEF) + CHAR(0xBB) + CHAR(0xBF)
                THEN SUBSTRING(x.RawText, 4, LEN(x.RawText) - 3)
                ELSE x.RawText
            END
    ) AS fr
    WHERE r.ResourceTypeId = @ResourceTypeId
        AND r.IsDeleted      = 0
        AND r.IsHistory      = 0
        AND fr.FhirResource IS NOT NULL
        AND JSON_VALUE(fr.FhirResource, '$.url') COLLATE Latin1_General_CS_AS
            = @SearchParameterUrl COLLATE Latin1_General_CS_AS;


    -- If not found in active version, try historical versions (handles soft-delete scenario)
    IF @ResourceId IS NULL
    BEGIN
        SELECT TOP 1
            @ResourceId       = r.ResourceId,
            @RawResourceJson  = fr.FhirResource
        FROM dbo.Resource AS r
        CROSS APPLY (
            -- Safely decompress only if RawResource looks like GZip (0x1F8B)
            SELECT RawText =
                CASE 
                    WHEN r.RawResource IS NOT NULL
                     AND SUBSTRING(r.RawResource, 1, 2) = 0x1F8B
                    THEN CAST(DECOMPRESS(r.RawResource) AS VARCHAR(MAX))
                    ELSE NULL
                END
        ) AS x
        CROSS APPLY (
            -- Strip UTF-8 BOM if present
            SELECT FhirResource =
                CASE 
                    WHEN x.RawText IS NOT NULL
                     AND LEFT(x.RawText, 3) = CHAR(0xEF) + CHAR(0xBB) + CHAR(0xBF)
                    THEN SUBSTRING(x.RawText, 4, LEN(x.RawText) - 3)
                    ELSE x.RawText
                END
        ) AS fr
        WHERE r.ResourceTypeId = @ResourceTypeId
          AND r.IsHistory      = 1
          AND fr.FhirResource IS NOT NULL
          AND JSON_VALUE(fr.FhirResource, '$.url') COLLATE Latin1_General_CS_AS
              = @SearchParameterUrl COLLATE Latin1_General_CS_AS
        ORDER BY r.ResourceSurrogateId DESC;

        IF @ResourceId IS NOT NULL
        BEGIN
            SET @EventText = 'Found SearchParameter from historical version (IsHistory=1) - soft-deleted scenario';
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Start = @st, @Text = @EventText;
        END
    END
    ELSE
    BEGIN
        SET @EventText = 'Found SearchParameter from active resource (IsDeleted=0, IsHistory=0)';
        EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Start = @st, @Text = @EventText;
    END

    -- Validate that we found a SearchParameter with this URL
    IF @ResourceId IS NULL
    BEGIN
        SET @EventText = 'SearchParameter not found for URL: ' + @SearchParameterUrl;
        EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st, @Text = @EventText;
        THROW 50404, 'SearchParameter not found for the specified URL', 1;
    END

    -- Log the start of the operation
    SET @EventText = 'Deleting SearchParameter: ' + @ResourceId + ' (URL: ' + @SearchParameterUrl + ')';
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Start', @Start = @st, @Text = @EventText;

    -- Count how many versions exist (for logging)
    SELECT @DeletedVersionCount = COUNT(*)
    FROM dbo.Resource
    WHERE ResourceTypeId = @ResourceTypeId 
      AND ResourceId = @ResourceId;

    -- *** BEGIN TRANSACTION - Start the atomic operation ***
    BEGIN TRANSACTION;

    -- Step 2: Delete the SearchParameter resource and all its search parameter indices
    -- This uses the standard HardDeleteResource procedure with standard hard delete parameters
    EXECUTE dbo.HardDeleteResource 
        @ResourceTypeId = @ResourceTypeId,
        @ResourceId = @ResourceId,
        @KeepCurrentVersion = 0,  -- Delete all versions
        @IsResourceChangeCaptureEnabled = 0;  -- Standard hard delete

    -- Step 3: Delete the SearchParam registry entry with case-sensitive comparison
    -- The SearchParam.Uri column uses Latin1_General_100_CS_AS collation (case-sensitive)
    DELETE FROM dbo.SearchParam 
    WHERE Uri = @SearchParameterUrl;
    
    IF @@ROWCOUNT > 0
    BEGIN
        SET @EventText = 'Successfully deleted SearchParam registry entry for URL: ' + @SearchParameterUrl;
    END
    ELSE
    BEGIN
        -- This is a warning, not an error - the entry may have already been deleted
        SET @EventText = 'WARNING: No SearchParam registry entry found for URL: ' + @SearchParameterUrl + ' (may have been deleted previously)';
    END

    -- *** COMMIT TRANSACTION - All operations succeeded ***
    COMMIT TRANSACTION;

    -- Log successful completion
    SET @EventText = @EventText + ' | Deleted ' + CONVERT(VARCHAR, @DeletedVersionCount) + ' resource version(s) for ResourceId: ' + @ResourceId;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Text = @EventText, @Rows = @DeletedVersionCount;

    -- Return the ResourceId and URL for reference
    SELECT 
        @ResourceId AS DeletedResourceId,
        @SearchParameterUrl AS DeletedSearchParameterUrl,
        @DeletedVersionCount AS DeletedVersionCount;

END TRY
BEGIN CATCH
    -- *** ROLLBACK TRANSACTION - Something failed, undo all changes ***
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    -- Log the error
    DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE();
    DECLARE @ErrorSeverity INT = ERROR_SEVERITY();
    DECLARE @ErrorState INT = ERROR_STATE();
    
    SET @EventText = 'HardDelete failed for SearchParameter URL: ' + @SearchParameterUrl + 
                     CASE WHEN @ResourceId IS NOT NULL THEN ' (ResourceId: ' + @ResourceId + ')' ELSE '' END + 
                     '. Error: ' + @ErrorMessage;
    
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st, @Text = @EventText;
    
    -- Re-throw the error with context
    THROW;
END CATCH;
GO
