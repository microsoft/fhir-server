ALTER PROCEDURE dbo.MergeResources @AffectedRows INT=0 OUTPUT, @RaiseExceptionOnConflict BIT=1, @IsResourceChangeCaptureEnabled BIT=0, @TransactionId BIGINT=NULL, @SingleTransaction BIT=1, @Resources dbo.ResourceList READONLY, @ResourceWriteClaims dbo.ResourceWriteClaimList READONLY, @ReferenceSearchParams dbo.ReferenceSearchParamList READONLY, @TokenSearchParams dbo.TokenSearchParamList READONLY, @TokenTexts dbo.TokenTextList READONLY, @StringSearchParams dbo.StringSearchParamList READONLY, @UriSearchParams dbo.UriSearchParamList READONLY, @NumberSearchParams dbo.NumberSearchParamList READONLY, @QuantitySearchParams dbo.QuantitySearchParamList READONLY, @DateTimeSearchParms dbo.DateTimeSearchParamList READONLY, @ReferenceTokenCompositeSearchParams dbo.ReferenceTokenCompositeSearchParamList READONLY, @TokenTokenCompositeSearchParams dbo.TokenTokenCompositeSearchParamList READONLY, @TokenDateTimeCompositeSearchParams dbo.TokenDateTimeCompositeSearchParamList READONLY, @TokenQuantityCompositeSearchParams dbo.TokenQuantityCompositeSearchParamList READONLY, @TokenStringCompositeSearchParams dbo.TokenStringCompositeSearchParamList READONLY, @TokenNumberNumberCompositeSearchParams dbo.TokenNumberNumberCompositeSearchParamList READONLY
AS
SET NOCOUNT ON;
DECLARE @st AS DATETIME = getUTCdate(), @SP AS VARCHAR (100) = object_name(@@procid), @DummyTop AS BIGINT = 9223372036854775807, @InitialTranCount AS INT = @@trancount, @IsRetry AS BIT = 0;
DECLARE @Mode AS VARCHAR (200) = isnull((SELECT 'RT=[' + CONVERT (VARCHAR, min(ResourceTypeId)) + ',' + CONVERT (VARCHAR, max(ResourceTypeId)) + '] Sur=[' + CONVERT (VARCHAR, min(ResourceSurrogateId)) + ',' + CONVERT (VARCHAR, max(ResourceSurrogateId)) + '] V=' + CONVERT (VARCHAR, max(Version)) + ' Rows=' + CONVERT (VARCHAR, count(*))
                                         FROM   @Resources), 'Input=Empty');
SET @Mode += ' E=' + CONVERT (VARCHAR, @RaiseExceptionOnConflict) + ' CC=' + CONVERT (VARCHAR, @IsResourceChangeCaptureEnabled) + ' IT=' + CONVERT (VARCHAR, @InitialTranCount) + ' T=' + isnull(CONVERT (VARCHAR, @TransactionId), 'NULL') + ' ST=' + CONVERT (VARCHAR, @SingleTransaction);
SET @AffectedRows = 0;
BEGIN TRY
    DECLARE @Existing AS TABLE (
        ResourceTypeId SMALLINT NOT NULL,
        SurrogateId    BIGINT   NOT NULL PRIMARY KEY (ResourceTypeId, SurrogateId));
    DECLARE @ResourceInfos AS TABLE (
        ResourceTypeId      SMALLINT NOT NULL,
        SurrogateId         BIGINT   NOT NULL,
        Version             INT      NOT NULL,
        KeepHistory         BIT      NOT NULL,
        PreviousVersion     INT      NULL,
        PreviousSurrogateId BIGINT   NULL PRIMARY KEY (ResourceTypeId, SurrogateId));
    DECLARE @PreviousSurrogateIds AS TABLE (
        TypeId      SMALLINT NOT NULL,
        SurrogateId BIGINT   NOT NULL PRIMARY KEY (TypeId, SurrogateId),
        KeepHistory BIT     );
    IF @InitialTranCount = 0
        BEGIN
            IF EXISTS (SELECT *
                       FROM   @Resources AS A
                              INNER JOIN
                              dbo.Resource AS B
                              ON B.ResourceTypeId = A.ResourceTypeId
                                 AND B.ResourceSurrogateId = A.ResourceSurrogateId)
                BEGIN
                    BEGIN TRANSACTION;
                    INSERT INTO @Existing (ResourceTypeId, SurrogateId)
                    SELECT B.ResourceTypeId,
                           B.ResourceSurrogateId
                    FROM   (SELECT TOP (@DummyTop) *
                            FROM   @Resources) AS A
                           INNER JOIN
                           dbo.Resource AS B WITH (ROWLOCK, HOLDLOCK)
                           ON B.ResourceTypeId = A.ResourceTypeId
                              AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    WHERE  B.IsHistory = 0
                           AND B.ResourceId = A.ResourceId
                           AND B.Version = A.Version
                    OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1));
                    IF @@rowcount = (SELECT count(*)
                                     FROM   @Resources)
                        SET @IsRetry = 1;
                    IF @IsRetry = 0
                        COMMIT TRANSACTION;
                END
        END
    SET @Mode += ' R=' + CONVERT (VARCHAR, @IsRetry);
    IF @SingleTransaction = 1
       AND @@trancount = 0
        BEGIN TRANSACTION;
    IF @IsRetry = 0
        BEGIN
            INSERT INTO @ResourceInfos (ResourceTypeId, SurrogateId, Version, KeepHistory, PreviousVersion, PreviousSurrogateId)
            SELECT A.ResourceTypeId,
                   A.ResourceSurrogateId,
                   A.Version,
                   A.KeepHistory,
                   B.Version,
                   B.ResourceSurrogateId
            FROM   (SELECT TOP (@DummyTop) *
                    FROM   @Resources
                    WHERE  HasVersionToCompare = 1) AS A
                   LEFT OUTER JOIN
                   dbo.Resource AS B
                   ON B.ResourceTypeId = A.ResourceTypeId
                      AND B.ResourceId = A.ResourceId
                      AND B.IsHistory = 0
            OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1));
            IF @RaiseExceptionOnConflict = 1
               AND EXISTS (SELECT *
                           FROM   @ResourceInfos
                           WHERE  (PreviousVersion IS NOT NULL
                                   AND Version <= PreviousVersion)
                                  OR (PreviousSurrogateId IS NOT NULL
                                      AND SurrogateId <= PreviousSurrogateId))
                THROW 50409, 'Resource has been recently updated or added, please compare the resource content in code for any duplicate updates', 1;
            INSERT INTO @PreviousSurrogateIds
            SELECT ResourceTypeId,
                   PreviousSurrogateId,
                   KeepHistory
            FROM   @ResourceInfos
            WHERE  PreviousSurrogateId IS NOT NULL;
            IF @@rowcount > 0
                BEGIN
                    UPDATE dbo.Resource
                    SET    IsHistory = 1
                    WHERE  EXISTS (SELECT *
                                   FROM   @PreviousSurrogateIds
                                   WHERE  TypeId = ResourceTypeId
                                          AND SurrogateId = ResourceSurrogateId
                                          AND KeepHistory = 1);
                    SET @AffectedRows += @@rowcount;
                    IF @IsResourceChangeCaptureEnabled = 1
                       AND NOT EXISTS (SELECT *
                                       FROM   dbo.Parameters
                                       WHERE  Id = 'InvisibleHistory.IsEnabled'
                                              AND Number = 0)
                        UPDATE dbo.Resource
                        SET    IsHistory            = 1,
                               RawResource          = 0xF,
                               SearchParamHash      = NULL,
                               HistoryTransactionId = @TransactionId
                        WHERE  EXISTS (SELECT *
                                       FROM   @PreviousSurrogateIds
                                       WHERE  TypeId = ResourceTypeId
                                              AND SurrogateId = ResourceSurrogateId
                                              AND KeepHistory = 0);
                    ELSE
                        DELETE dbo.Resource
                        WHERE  EXISTS (SELECT *
                                       FROM   @PreviousSurrogateIds
                                       WHERE  TypeId = ResourceTypeId
                                              AND SurrogateId = ResourceSurrogateId
                                              AND KeepHistory = 0);
                    SET @AffectedRows += @@rowcount;
                    DELETE dbo.ResourceWriteClaim
                    WHERE  EXISTS (SELECT *
                                   FROM   @PreviousSurrogateIds
                                   WHERE  SurrogateId = ResourceSurrogateId);
                    SET @AffectedRows += @@rowcount;
                    DELETE dbo.ReferenceSearchParam
                    WHERE  EXISTS (SELECT *
                                   FROM   @PreviousSurrogateIds
                                   WHERE  TypeId = ResourceTypeId
                                          AND SurrogateId = ResourceSurrogateId);
                    SET @AffectedRows += @@rowcount;
                    DELETE dbo.TokenSearchParam
                    WHERE  EXISTS (SELECT *
                                   FROM   @PreviousSurrogateIds
                                   WHERE  TypeId = ResourceTypeId
                                          AND SurrogateId = ResourceSurrogateId);
                    SET @AffectedRows += @@rowcount;
                    DELETE dbo.TokenText
                    WHERE  EXISTS (SELECT *
                                   FROM   @PreviousSurrogateIds
                                   WHERE  TypeId = ResourceTypeId
                                          AND SurrogateId = ResourceSurrogateId);
                    SET @AffectedRows += @@rowcount;
                    DELETE dbo.StringSearchParam
                    WHERE  EXISTS (SELECT *
                                   FROM   @PreviousSurrogateIds
                                   WHERE  TypeId = ResourceTypeId
                                          AND SurrogateId = ResourceSurrogateId);
                    SET @AffectedRows += @@rowcount;
                    DELETE dbo.UriSearchParam
                    WHERE  EXISTS (SELECT *
                                   FROM   @PreviousSurrogateIds
                                   WHERE  TypeId = ResourceTypeId
                                          AND SurrogateId = ResourceSurrogateId);
                    SET @AffectedRows += @@rowcount;
                    DELETE dbo.NumberSearchParam
                    WHERE  EXISTS (SELECT *
                                   FROM   @PreviousSurrogateIds
                                   WHERE  TypeId = ResourceTypeId
                                          AND SurrogateId = ResourceSurrogateId);
                    SET @AffectedRows += @@rowcount;
                    DELETE dbo.QuantitySearchParam
                    WHERE  EXISTS (SELECT *
                                   FROM   @PreviousSurrogateIds
                                   WHERE  TypeId = ResourceTypeId
                                          AND SurrogateId = ResourceSurrogateId);
                    SET @AffectedRows += @@rowcount;
                    DELETE dbo.DateTimeSearchParam
                    WHERE  EXISTS (SELECT *
                                   FROM   @PreviousSurrogateIds
                                   WHERE  TypeId = ResourceTypeId
                                          AND SurrogateId = ResourceSurrogateId);
                    SET @AffectedRows += @@rowcount;
                    DELETE dbo.ReferenceTokenCompositeSearchParam
                    WHERE  EXISTS (SELECT *
                                   FROM   @PreviousSurrogateIds
                                   WHERE  TypeId = ResourceTypeId
                                          AND SurrogateId = ResourceSurrogateId);
                    SET @AffectedRows += @@rowcount;
                    DELETE dbo.TokenTokenCompositeSearchParam
                    WHERE  EXISTS (SELECT *
                                   FROM   @PreviousSurrogateIds
                                   WHERE  TypeId = ResourceTypeId
                                          AND SurrogateId = ResourceSurrogateId);
                    SET @AffectedRows += @@rowcount;
                    DELETE dbo.TokenDateTimeCompositeSearchParam
                    WHERE  EXISTS (SELECT *
                                   FROM   @PreviousSurrogateIds
                                   WHERE  TypeId = ResourceTypeId
                                          AND SurrogateId = ResourceSurrogateId);
                    SET @AffectedRows += @@rowcount;
                    DELETE dbo.TokenQuantityCompositeSearchParam
                    WHERE  EXISTS (SELECT *
                                   FROM   @PreviousSurrogateIds
                                   WHERE  TypeId = ResourceTypeId
                                          AND SurrogateId = ResourceSurrogateId);
                    SET @AffectedRows += @@rowcount;
                    DELETE dbo.TokenStringCompositeSearchParam
                    WHERE  EXISTS (SELECT *
                                   FROM   @PreviousSurrogateIds
                                   WHERE  TypeId = ResourceTypeId
                                          AND SurrogateId = ResourceSurrogateId);
                    SET @AffectedRows += @@rowcount;
                    DELETE dbo.TokenNumberNumberCompositeSearchParam
                    WHERE  EXISTS (SELECT *
                                   FROM   @PreviousSurrogateIds
                                   WHERE  TypeId = ResourceTypeId
                                          AND SurrogateId = ResourceSurrogateId);
                    SET @AffectedRows += @@rowcount;
                END
            INSERT INTO dbo.Resource (ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash, TransactionId)
            SELECT ResourceTypeId,
                   ResourceId,
                   Version,
                   IsHistory,
                   ResourceSurrogateId,
                   IsDeleted,
                   RequestMethod,
                   RawResource,
                   IsRawResourceMetaSet,
                   SearchParamHash,
                   @TransactionId
            FROM   @Resources;
            SET @AffectedRows += @@rowcount;
            INSERT INTO dbo.ResourceWriteClaim (ResourceSurrogateId, ClaimTypeId, ClaimValue)
            SELECT ResourceSurrogateId,
                   ClaimTypeId,
                   ClaimValue
            FROM   @ResourceWriteClaims;
            SET @AffectedRows += @@rowcount;
            INSERT INTO dbo.ReferenceSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion)
            SELECT ResourceTypeId,
                   ResourceSurrogateId,
                   SearchParamId,
                   BaseUri,
                   ReferenceResourceTypeId,
                   ReferenceResourceId,
                   ReferenceResourceVersion
            FROM   @ReferenceSearchParams;
            SET @AffectedRows += @@rowcount;
            INSERT INTO dbo.TokenSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow)
            SELECT ResourceTypeId,
                   ResourceSurrogateId,
                   SearchParamId,
                   SystemId,
                   Code,
                   CodeOverflow
            FROM   @TokenSearchParams;
            SET @AffectedRows += @@rowcount;
            INSERT INTO dbo.TokenText (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text)
            SELECT ResourceTypeId,
                   ResourceSurrogateId,
                   SearchParamId,
                   Text
            FROM   @TokenTexts;
            SET @AffectedRows += @@rowcount;
            INSERT INTO dbo.StringSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax)
            SELECT ResourceTypeId,
                   ResourceSurrogateId,
                   SearchParamId,
                   Text,
                   TextOverflow,
                   IsMin,
                   IsMax
            FROM   @StringSearchParams;
            SET @AffectedRows += @@rowcount;
            INSERT INTO dbo.UriSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri)
            SELECT ResourceTypeId,
                   ResourceSurrogateId,
                   SearchParamId,
                   Uri
            FROM   @UriSearchParams;
            SET @AffectedRows += @@rowcount;
            INSERT INTO dbo.NumberSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue)
            SELECT ResourceTypeId,
                   ResourceSurrogateId,
                   SearchParamId,
                   SingleValue,
                   LowValue,
                   HighValue
            FROM   @NumberSearchParams;
            SET @AffectedRows += @@rowcount;
            INSERT INTO dbo.QuantitySearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue)
            SELECT ResourceTypeId,
                   ResourceSurrogateId,
                   SearchParamId,
                   SystemId,
                   QuantityCodeId,
                   SingleValue,
                   LowValue,
                   HighValue
            FROM   @QuantitySearchParams;
            SET @AffectedRows += @@rowcount;
            INSERT INTO dbo.DateTimeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax)
            SELECT ResourceTypeId,
                   ResourceSurrogateId,
                   SearchParamId,
                   StartDateTime,
                   EndDateTime,
                   IsLongerThanADay,
                   IsMin,
                   IsMax
            FROM   @DateTimeSearchParms;
            SET @AffectedRows += @@rowcount;
            INSERT INTO dbo.ReferenceTokenCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2)
            SELECT ResourceTypeId,
                   ResourceSurrogateId,
                   SearchParamId,
                   BaseUri1,
                   ReferenceResourceTypeId1,
                   ReferenceResourceId1,
                   ReferenceResourceVersion1,
                   SystemId2,
                   Code2,
                   CodeOverflow2
            FROM   @ReferenceTokenCompositeSearchParams;
            SET @AffectedRows += @@rowcount;
            INSERT INTO dbo.TokenTokenCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2)
            SELECT ResourceTypeId,
                   ResourceSurrogateId,
                   SearchParamId,
                   SystemId1,
                   Code1,
                   CodeOverflow1,
                   SystemId2,
                   Code2,
                   CodeOverflow2
            FROM   @TokenTokenCompositeSearchParams;
            SET @AffectedRows += @@rowcount;
            INSERT INTO dbo.TokenDateTimeCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2)
            SELECT ResourceTypeId,
                   ResourceSurrogateId,
                   SearchParamId,
                   SystemId1,
                   Code1,
                   CodeOverflow1,
                   StartDateTime2,
                   EndDateTime2,
                   IsLongerThanADay2
            FROM   @TokenDateTimeCompositeSearchParams;
            SET @AffectedRows += @@rowcount;
            INSERT INTO dbo.TokenQuantityCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2)
            SELECT ResourceTypeId,
                   ResourceSurrogateId,
                   SearchParamId,
                   SystemId1,
                   Code1,
                   CodeOverflow1,
                   SingleValue2,
                   SystemId2,
                   QuantityCodeId2,
                   LowValue2,
                   HighValue2
            FROM   @TokenQuantityCompositeSearchParams;
            SET @AffectedRows += @@rowcount;
            INSERT INTO dbo.TokenStringCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2)
            SELECT ResourceTypeId,
                   ResourceSurrogateId,
                   SearchParamId,
                   SystemId1,
                   Code1,
                   CodeOverflow1,
                   Text2,
                   TextOverflow2
            FROM   @TokenStringCompositeSearchParams;
            SET @AffectedRows += @@rowcount;
            INSERT INTO dbo.TokenNumberNumberCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange)
            SELECT ResourceTypeId,
                   ResourceSurrogateId,
                   SearchParamId,
                   SystemId1,
                   Code1,
                   CodeOverflow1,
                   SingleValue2,
                   LowValue2,
                   HighValue2,
                   SingleValue3,
                   LowValue3,
                   HighValue3,
                   HasRange
            FROM   @TokenNumberNumberCompositeSearchParams;
            SET @AffectedRows += @@rowcount;
        END
    ELSE
        BEGIN
            INSERT INTO dbo.ResourceWriteClaim (ResourceSurrogateId, ClaimTypeId, ClaimValue)
            SELECT ResourceSurrogateId,
                   ClaimTypeId,
                   ClaimValue
            FROM   (SELECT TOP (@DummyTop) *
                    FROM   @ResourceWriteClaims) AS A
            WHERE  EXISTS (SELECT *
                           FROM   @Existing AS B
                           WHERE  B.SurrogateId = A.ResourceSurrogateId)
                   AND NOT EXISTS (SELECT *
                                   FROM   dbo.ResourceWriteClaim AS C
                                   WHERE  C.ResourceSurrogateId = A.ResourceSurrogateId)
            OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1));
            SET @AffectedRows += @@rowcount;
            INSERT INTO dbo.ReferenceSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion)
            SELECT ResourceTypeId,
                   ResourceSurrogateId,
                   SearchParamId,
                   BaseUri,
                   ReferenceResourceTypeId,
                   ReferenceResourceId,
                   ReferenceResourceVersion
            FROM   (SELECT TOP (@DummyTop) *
                    FROM   @ReferenceSearchParams) AS A
            WHERE  EXISTS (SELECT *
                           FROM   @Existing AS B
                           WHERE  B.ResourceTypeId = A.ResourceTypeId
                                  AND B.SurrogateId = A.ResourceSurrogateId)
                   AND NOT EXISTS (SELECT *
                                   FROM   dbo.ReferenceSearchParam AS C
                                   WHERE  C.ResourceTypeId = A.ResourceTypeId
                                          AND C.ResourceSurrogateId = A.ResourceSurrogateId)
            OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1));
            SET @AffectedRows += @@rowcount;
            INSERT INTO dbo.TokenSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow)
            SELECT ResourceTypeId,
                   ResourceSurrogateId,
                   SearchParamId,
                   SystemId,
                   Code,
                   CodeOverflow
            FROM   (SELECT TOP (@DummyTop) *
                    FROM   @TokenSearchParams) AS A
            WHERE  EXISTS (SELECT *
                           FROM   @Existing AS B
                           WHERE  B.ResourceTypeId = A.ResourceTypeId
                                  AND B.SurrogateId = A.ResourceSurrogateId)
                   AND NOT EXISTS (SELECT *
                                   FROM   dbo.TokenSearchParam AS C
                                   WHERE  C.ResourceTypeId = A.ResourceTypeId
                                          AND C.ResourceSurrogateId = A.ResourceSurrogateId)
            OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1));
            SET @AffectedRows += @@rowcount;
            INSERT INTO dbo.TokenText (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text)
            SELECT ResourceTypeId,
                   ResourceSurrogateId,
                   SearchParamId,
                   Text
            FROM   (SELECT TOP (@DummyTop) *
                    FROM   @TokenTexts) AS A
            WHERE  EXISTS (SELECT *
                           FROM   @Existing AS B
                           WHERE  B.ResourceTypeId = A.ResourceTypeId
                                  AND B.SurrogateId = A.ResourceSurrogateId)
                   AND NOT EXISTS (SELECT *
                                   FROM   dbo.TokenText AS C
                                   WHERE  C.ResourceTypeId = A.ResourceTypeId
                                          AND C.ResourceSurrogateId = A.ResourceSurrogateId)
            OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1));
            SET @AffectedRows += @@rowcount;
            INSERT INTO dbo.StringSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax)
            SELECT ResourceTypeId,
                   ResourceSurrogateId,
                   SearchParamId,
                   Text,
                   TextOverflow,
                   IsMin,
                   IsMax
            FROM   (SELECT TOP (@DummyTop) *
                    FROM   @StringSearchParams) AS A
            WHERE  EXISTS (SELECT *
                           FROM   @Existing AS B
                           WHERE  B.ResourceTypeId = A.ResourceTypeId
                                  AND B.SurrogateId = A.ResourceSurrogateId)
                   AND NOT EXISTS (SELECT *
                                   FROM   dbo.StringSearchParam AS C
                                   WHERE  C.ResourceTypeId = A.ResourceTypeId
                                          AND C.ResourceSurrogateId = A.ResourceSurrogateId)
            OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1));
            SET @AffectedRows += @@rowcount;
            INSERT INTO dbo.UriSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri)
            SELECT ResourceTypeId,
                   ResourceSurrogateId,
                   SearchParamId,
                   Uri
            FROM   (SELECT TOP (@DummyTop) *
                    FROM   @UriSearchParams) AS A
            WHERE  EXISTS (SELECT *
                           FROM   @Existing AS B
                           WHERE  B.ResourceTypeId = A.ResourceTypeId
                                  AND B.SurrogateId = A.ResourceSurrogateId)
                   AND NOT EXISTS (SELECT *
                                   FROM   dbo.UriSearchParam AS C
                                   WHERE  C.ResourceTypeId = A.ResourceTypeId
                                          AND C.ResourceSurrogateId = A.ResourceSurrogateId)
            OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1));
            SET @AffectedRows += @@rowcount;
            INSERT INTO dbo.NumberSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue)
            SELECT ResourceTypeId,
                   ResourceSurrogateId,
                   SearchParamId,
                   SingleValue,
                   LowValue,
                   HighValue
            FROM   (SELECT TOP (@DummyTop) *
                    FROM   @NumberSearchParams) AS A
            WHERE  EXISTS (SELECT *
                           FROM   @Existing AS B
                           WHERE  B.ResourceTypeId = A.ResourceTypeId
                                  AND B.SurrogateId = A.ResourceSurrogateId)
                   AND NOT EXISTS (SELECT *
                                   FROM   dbo.NumberSearchParam AS C
                                   WHERE  C.ResourceTypeId = A.ResourceTypeId
                                          AND C.ResourceSurrogateId = A.ResourceSurrogateId)
            OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1));
            SET @AffectedRows += @@rowcount;
            INSERT INTO dbo.QuantitySearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue)
            SELECT ResourceTypeId,
                   ResourceSurrogateId,
                   SearchParamId,
                   SystemId,
                   QuantityCodeId,
                   SingleValue,
                   LowValue,
                   HighValue
            FROM   (SELECT TOP (@DummyTop) *
                    FROM   @QuantitySearchParams) AS A
            WHERE  EXISTS (SELECT *
                           FROM   @Existing AS B
                           WHERE  B.ResourceTypeId = A.ResourceTypeId
                                  AND B.SurrogateId = A.ResourceSurrogateId)
                   AND NOT EXISTS (SELECT *
                                   FROM   dbo.QuantitySearchParam AS C
                                   WHERE  C.ResourceTypeId = A.ResourceTypeId
                                          AND C.ResourceSurrogateId = A.ResourceSurrogateId)
            OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1));
            SET @AffectedRows += @@rowcount;
            INSERT INTO dbo.DateTimeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax)
            SELECT ResourceTypeId,
                   ResourceSurrogateId,
                   SearchParamId,
                   StartDateTime,
                   EndDateTime,
                   IsLongerThanADay,
                   IsMin,
                   IsMax
            FROM   (SELECT TOP (@DummyTop) *
                    FROM   @DateTimeSearchParms) AS A
            WHERE  EXISTS (SELECT *
                           FROM   @Existing AS B
                           WHERE  B.ResourceTypeId = A.ResourceTypeId
                                  AND B.SurrogateId = A.ResourceSurrogateId)
                   AND NOT EXISTS (SELECT *
                                   FROM   dbo.DateTimeSearchParam AS C
                                   WHERE  C.ResourceTypeId = A.ResourceTypeId
                                          AND C.ResourceSurrogateId = A.ResourceSurrogateId)
            OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1));
            SET @AffectedRows += @@rowcount;
            INSERT INTO dbo.ReferenceTokenCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2)
            SELECT ResourceTypeId,
                   ResourceSurrogateId,
                   SearchParamId,
                   BaseUri1,
                   ReferenceResourceTypeId1,
                   ReferenceResourceId1,
                   ReferenceResourceVersion1,
                   SystemId2,
                   Code2,
                   CodeOverflow2
            FROM   (SELECT TOP (@DummyTop) *
                    FROM   @ReferenceTokenCompositeSearchParams) AS A
            WHERE  EXISTS (SELECT *
                           FROM   @Existing AS B
                           WHERE  B.ResourceTypeId = A.ResourceTypeId
                                  AND B.SurrogateId = A.ResourceSurrogateId)
                   AND NOT EXISTS (SELECT *
                                   FROM   dbo.ReferenceTokenCompositeSearchParam AS C
                                   WHERE  C.ResourceTypeId = A.ResourceTypeId
                                          AND C.ResourceSurrogateId = A.ResourceSurrogateId)
            OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1));
            SET @AffectedRows += @@rowcount;
            INSERT INTO dbo.TokenTokenCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2)
            SELECT ResourceTypeId,
                   ResourceSurrogateId,
                   SearchParamId,
                   SystemId1,
                   Code1,
                   CodeOverflow1,
                   SystemId2,
                   Code2,
                   CodeOverflow2
            FROM   (SELECT TOP (@DummyTop) *
                    FROM   @TokenTokenCompositeSearchParams) AS A
            WHERE  EXISTS (SELECT *
                           FROM   @Existing AS B
                           WHERE  B.ResourceTypeId = A.ResourceTypeId
                                  AND B.SurrogateId = A.ResourceSurrogateId)
                   AND NOT EXISTS (SELECT *
                                   FROM   dbo.TokenTokenCompositeSearchParam AS C
                                   WHERE  C.ResourceTypeId = A.ResourceTypeId
                                          AND C.ResourceSurrogateId = A.ResourceSurrogateId)
            OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1));
            SET @AffectedRows += @@rowcount;
            INSERT INTO dbo.TokenDateTimeCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2)
            SELECT ResourceTypeId,
                   ResourceSurrogateId,
                   SearchParamId,
                   SystemId1,
                   Code1,
                   CodeOverflow1,
                   StartDateTime2,
                   EndDateTime2,
                   IsLongerThanADay2
            FROM   (SELECT TOP (@DummyTop) *
                    FROM   @TokenDateTimeCompositeSearchParams) AS A
            WHERE  EXISTS (SELECT *
                           FROM   @Existing AS B
                           WHERE  B.ResourceTypeId = A.ResourceTypeId
                                  AND B.SurrogateId = A.ResourceSurrogateId)
                   AND NOT EXISTS (SELECT *
                                   FROM   dbo.TokenDateTimeCompositeSearchParam AS C
                                   WHERE  C.ResourceTypeId = A.ResourceTypeId
                                          AND C.ResourceSurrogateId = A.ResourceSurrogateId)
            OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1));
            SET @AffectedRows += @@rowcount;
            INSERT INTO dbo.TokenQuantityCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2)
            SELECT ResourceTypeId,
                   ResourceSurrogateId,
                   SearchParamId,
                   SystemId1,
                   Code1,
                   CodeOverflow1,
                   SingleValue2,
                   SystemId2,
                   QuantityCodeId2,
                   LowValue2,
                   HighValue2
            FROM   (SELECT TOP (@DummyTop) *
                    FROM   @TokenQuantityCompositeSearchParams) AS A
            WHERE  EXISTS (SELECT *
                           FROM   @Existing AS B
                           WHERE  B.ResourceTypeId = A.ResourceTypeId
                                  AND B.SurrogateId = A.ResourceSurrogateId)
                   AND NOT EXISTS (SELECT *
                                   FROM   dbo.TokenQuantityCompositeSearchParam AS C
                                   WHERE  C.ResourceTypeId = A.ResourceTypeId
                                          AND C.ResourceSurrogateId = A.ResourceSurrogateId)
            OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1));
            SET @AffectedRows += @@rowcount;
            INSERT INTO dbo.TokenStringCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2)
            SELECT ResourceTypeId,
                   ResourceSurrogateId,
                   SearchParamId,
                   SystemId1,
                   Code1,
                   CodeOverflow1,
                   Text2,
                   TextOverflow2
            FROM   (SELECT TOP (@DummyTop) *
                    FROM   @TokenStringCompositeSearchParams) AS A
            WHERE  EXISTS (SELECT *
                           FROM   @Existing AS B
                           WHERE  B.ResourceTypeId = A.ResourceTypeId
                                  AND B.SurrogateId = A.ResourceSurrogateId)
                   AND NOT EXISTS (SELECT *
                                   FROM   dbo.TokenStringCompositeSearchParam AS C
                                   WHERE  C.ResourceTypeId = A.ResourceTypeId
                                          AND C.ResourceSurrogateId = A.ResourceSurrogateId)
            OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1));
            SET @AffectedRows += @@rowcount;
            INSERT INTO dbo.TokenNumberNumberCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange)
            SELECT ResourceTypeId,
                   ResourceSurrogateId,
                   SearchParamId,
                   SystemId1,
                   Code1,
                   CodeOverflow1,
                   SingleValue2,
                   LowValue2,
                   HighValue2,
                   SingleValue3,
                   LowValue3,
                   HighValue3,
                   HasRange
            FROM   (SELECT TOP (@DummyTop) *
                    FROM   @TokenNumberNumberCompositeSearchParams) AS A
            WHERE  EXISTS (SELECT *
                           FROM   @Existing AS B
                           WHERE  B.ResourceTypeId = A.ResourceTypeId
                                  AND B.SurrogateId = A.ResourceSurrogateId)
                   AND NOT EXISTS (SELECT *
                                   FROM   dbo.TokenNumberNumberCompositeSearchParam AS C
                                   WHERE  C.ResourceTypeId = A.ResourceTypeId
                                          AND C.ResourceSurrogateId = A.ResourceSurrogateId)
            OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1));
            SET @AffectedRows += @@rowcount;
        END
    IF @IsResourceChangeCaptureEnabled = 1
        EXECUTE dbo.CaptureResourceIdsForChanges @Resources;
    IF @TransactionId IS NOT NULL
        EXECUTE dbo.MergeResourcesCommitTransaction @TransactionId;
    IF @InitialTranCount = 0
       AND @@trancount > 0
        COMMIT TRANSACTION;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @AffectedRows;
END TRY
BEGIN CATCH
    IF @InitialTranCount = 0
       AND @@trancount > 0
        ROLLBACK;
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st;
    IF @RaiseExceptionOnConflict = 1 AND error_message() LIKE '%''dbo.Resource''%'
        BEGIN
            IF error_number() = 2601
                THROW 50409, 'Resource has been recently updated or added, please compare the resource content in code for any duplicate updates', 1;
            ELSE IF error_number() = 2627
                THROW 50424, 'Cannot persit resource due to a conflict with duplicated keys. Check the volume of resource being submited for ingestion', 1;
            ELSE
                THROW;
        END
    ELSE
        THROW;
END CATCH
GO
