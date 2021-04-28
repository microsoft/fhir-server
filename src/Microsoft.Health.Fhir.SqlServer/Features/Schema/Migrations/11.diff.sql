/*************************************************************
    Populates ReferenceResourceSurrogateId column on ReferenceSearchParam for the entire database in batches.

**************************************************************/

SET NOCOUNT ON

DECLARE @batchSize int = 10000
DECLARE @resourceTypeId smallint = 0

WHILE @resourceTypeId < 150
BEGIN

    DECLARE @currentResourceSurrogateId bigint = 0

    WHILE 1 = 1
    BEGIN
       
        UPDATE dbo.ReferenceSearchParam
        SET dbo.ReferenceSearchParam.ReferenceResourceSurrogateId = res.ResourceSurrogateId,
            @currentResourceSurrogateId = res.ResourceSurrogateId
        FROM (
            SELECT TOP (@batchSize) *
            FROM dbo.Resource
            WHERE ResourceTypeId = @resourceTypeId
                AND ResourceSurrogateId > @currentResourceSurrogateId
                AND IsHistory = 0
                AND IsDeleted = 0
            ORDER BY ResourceSurrogateId ASC
        ) AS res  
        WHERE dbo.ReferenceSearchParam.ReferenceResourceTypeId = @resourceTypeId
            AND dbo.ReferenceSearchParam.ReferenceResourceId = res.ResourceId
            AND dbo.ReferenceSearchParam.IsHistory = 0
            AND dbo.ReferenceSearchParam.BaseUri IS NULL
            AND dbo.ReferenceSearchParam.ReferenceResourceSurrogateId IS NULL
                
        IF @@ROWCOUNT = 0
            BREAK;

        DECLARE @message varchar(max) = 'Completed batch of setting ReferenceResourceSurrogateId for resource type ' + Convert(varchar(max), @resourceTypeId) + ' up to ResourceSurrogateId ' + Convert(varchar(max), @currentResourceSurrogateId)
        EXEC dbo.LogSchemaMigrationProgress @message
    END

     DECLARE @batchFinishedMessage varchar(max) = 'Completed resource type ' + Convert(varchar(max), @resourceTypeId) + ' up to ResourceSurrogateId ' + Convert(varchar(max), @currentResourceSurrogateId)
     EXEC dbo.LogSchemaMigrationProgress @batchFinishedMessage

     DECLARE @typeFinishedMessage varchar(max) = 'Completed setting ReferenceResourceSurrogateId for resource type ' + Convert(varchar(max), @resourceTypeId)
     EXEC dbo.LogSchemaMigrationProgress @typeFinishedMessage

    SET @resourceTypeId = @resourceTypeId + 1
END


