/*************************************************************
    Semantic search feature
    Adds the EmbeddingModel registry and vector search tables.
**************************************************************/

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'EmbeddingModel')
BEGIN
    CREATE TABLE dbo.EmbeddingModel
    (
        EmbeddingModelId        smallint        IDENTITY(1,1)   NOT NULL,
        ModelName               varchar(128)    COLLATE Latin1_General_100_CS_AS NOT NULL,
        ModelVersion            varchar(64)     COLLATE Latin1_General_100_CS_AS NOT NULL,
        Dimension               int             NOT NULL,
        DistanceMetric          varchar(16)     COLLATE Latin1_General_100_CS_AS NOT NULL
            CONSTRAINT DF_EmbeddingModel_DistanceMetric DEFAULT 'cosine',
        CreatedAt               datetime2(7)    NOT NULL
            CONSTRAINT DF_EmbeddingModel_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PKC_EmbeddingModel PRIMARY KEY CLUSTERED (EmbeddingModelId),
        CONSTRAINT U_EmbeddingModel_Name_Version UNIQUE (ModelName, ModelVersion)
    )
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'VectorSearchParam')
BEGIN
    CREATE TABLE dbo.VectorSearchParam
    (
        ResourceTypeId          smallint        NOT NULL,
        ResourceSurrogateId     bigint          NOT NULL,
        SearchParamId           smallint        NOT NULL,
        ChunkOrdinal            smallint        NOT NULL
            CONSTRAINT DF_VectorSearchParam_ChunkOrdinal DEFAULT 0,
        EmbeddingModelId        smallint        NOT NULL,
        ChunkText               nvarchar(max)   NOT NULL,
        SourceTextHash          binary(32)      NOT NULL,
        SourceResourceTypeId    smallint        NULL,
        SourceResourceId        varchar(64)     COLLATE Latin1_General_100_CS_AS NULL,
        SourceResourceVersion   varchar(64)     COLLATE Latin1_General_100_CS_AS NULL,
        SourcePath              nvarchar(512)   NULL,
        Embedding               vector(1536)    NOT NULL
    )

    ALTER TABLE dbo.VectorSearchParam SET ( LOCK_ESCALATION = AUTO )

    ALTER TABLE dbo.VectorSearchParam ADD CONSTRAINT PKC_VectorSearchParam
    PRIMARY KEY CLUSTERED
    (
        ResourceTypeId,
        ResourceSurrogateId,
        SearchParamId,
        ChunkOrdinal
    )
    WITH (DATA_COMPRESSION = PAGE)
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.table_types WHERE name = 'VectorSearchParamList')
BEGIN
    CREATE TYPE dbo.VectorSearchParamList AS TABLE
    (
        ResourceTypeId           smallint      NOT NULL,
        ResourceSurrogateId      bigint        NOT NULL,
        SearchParamId            smallint      NOT NULL,
        ChunkOrdinal             smallint      NOT NULL,
        EmbeddingModelId         smallint      NOT NULL,
        ChunkText                nvarchar(max) NOT NULL,
        SourceTextHash           binary(32)    NOT NULL,
        SourceResourceTypeId     smallint      NOT NULL,
        SourceResourceId         varchar(64)   COLLATE Latin1_General_100_CS_AS NOT NULL,
        SourceResourceVersion    varchar(64)   COLLATE Latin1_General_100_CS_AS NULL,
        SourcePath               nvarchar(512) NOT NULL,
        Embedding                nvarchar(max) NOT NULL
    )
END
GO

ALTER PROCEDURE dbo.MergeResources
        @AffectedRows int = 0 OUT
     ,@RaiseExceptionOnConflict bit = 1
     ,@IsResourceChangeCaptureEnabled bit = 0
     ,@TransactionId bigint = NULL
     ,@SingleTransaction bit = 1
     ,@Resources dbo.ResourceList READONLY
     ,@ResourceWriteClaims dbo.ResourceWriteClaimList READONLY
     ,@ReferenceSearchParams dbo.ReferenceSearchParamList READONLY
     ,@TokenSearchParams dbo.TokenSearchParamList READONLY
     ,@TokenTexts dbo.TokenTextList READONLY
     ,@StringSearchParams dbo.StringSearchParamList READONLY
     ,@UriSearchParams dbo.UriSearchParamList READONLY
     ,@NumberSearchParams dbo.NumberSearchParamList READONLY
     ,@QuantitySearchParams dbo.QuantitySearchParamList READONLY
     ,@DateTimeSearchParms dbo.DateTimeSearchParamList READONLY
     ,@VectorSearchParams dbo.VectorSearchParamList READONLY
     ,@ReferenceTokenCompositeSearchParams dbo.ReferenceTokenCompositeSearchParamList READONLY
     ,@TokenTokenCompositeSearchParams dbo.TokenTokenCompositeSearchParamList READONLY
     ,@TokenDateTimeCompositeSearchParams dbo.TokenDateTimeCompositeSearchParamList READONLY
     ,@TokenQuantityCompositeSearchParams dbo.TokenQuantityCompositeSearchParamList READONLY
     ,@TokenStringCompositeSearchParams dbo.TokenStringCompositeSearchParamList READONLY
     ,@TokenNumberNumberCompositeSearchParams dbo.TokenNumberNumberCompositeSearchParamList READONLY
AS
set nocount on
DECLARE @st datetime = getUTCdate()
             ,@SP varchar(100) = object_name(@@procid)
             ,@DummyTop bigint = 9223372036854775807
             ,@InitialTranCount int = @@trancount
             ,@IsRetry bit = 0

DECLARE @Mode varchar(200) = isnull((SELECT 'RT=['+convert(varchar,min(ResourceTypeId))+','+convert(varchar,max(ResourceTypeId))+'] Sur=['+convert(varchar,min(ResourceSurrogateId))+','+convert(varchar,max(ResourceSurrogateId))+'] V='+convert(varchar,max(Version))+' Rows='+convert(varchar,count(*)) FROM @Resources),'Input=Empty')
SET @Mode += ' E='+convert(varchar,@RaiseExceptionOnConflict)+' CC='+convert(varchar,@IsResourceChangeCaptureEnabled)+' IT='+convert(varchar,@InitialTranCount)+' T='+isnull(convert(varchar,@TransactionId),'NULL')+' ST='+convert(varchar,@SingleTransaction)

SET @AffectedRows = 0

BEGIN TRY
    DECLARE @Existing AS TABLE (ResourceTypeId smallint NOT NULL, SurrogateId bigint NOT NULL PRIMARY KEY (ResourceTypeId, SurrogateId))

    DECLARE @ResourceInfos AS TABLE
        (
             ResourceTypeId       smallint       NOT NULL
            ,SurrogateId          bigint         NOT NULL
            ,Version              int            NOT NULL
            ,KeepHistory          bit            NOT NULL
            ,PreviousVersion      int            NULL
            ,PreviousSurrogateId  bigint         NULL

            PRIMARY KEY (ResourceTypeId, SurrogateId)
        )

    DECLARE @PreviousSurrogateIds AS TABLE (TypeId smallint NOT NULL, SurrogateId bigint NOT NULL PRIMARY KEY (TypeId, SurrogateId), KeepHistory bit)

    IF @InitialTranCount = 0
    BEGIN
        IF EXISTS (SELECT *
                                 FROM @Resources A JOIN dbo.Resource B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
                            )
        BEGIN
            BEGIN TRANSACTION

            INSERT INTO @Existing
                            (  ResourceTypeId,           SurrogateId )
                SELECT B.ResourceTypeId, B.ResourceSurrogateId
                    FROM (SELECT TOP (@DummyTop) * FROM @Resources) A
                             JOIN dbo.Resource B WITH (ROWLOCK, HOLDLOCK) ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    WHERE B.IsHistory = 0
                        AND B.ResourceId = A.ResourceId
                        AND B.Version = A.Version
                    OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))

            IF @@rowcount = (SELECT count(*) FROM @Resources) SET @IsRetry = 1

            IF @IsRetry = 0 COMMIT TRANSACTION
        END
    END

    SET @Mode += ' R='+convert(varchar,@IsRetry)

    IF @SingleTransaction = 1 AND @@trancount = 0 BEGIN TRANSACTION

    IF @IsRetry = 0
    BEGIN
        INSERT INTO @ResourceInfos
                        (  ResourceTypeId,           SurrogateId,   Version,   KeepHistory, PreviousVersion,   PreviousSurrogateId )
            SELECT A.ResourceTypeId, A.ResourceSurrogateId, A.Version, A.KeepHistory,       B.Version, B.ResourceSurrogateId
                FROM (SELECT TOP (@DummyTop) * FROM @Resources WHERE HasVersionToCompare = 1) A
                         LEFT OUTER JOIN dbo.Resource B
                             ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId AND B.IsHistory = 0
                OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))

        IF @RaiseExceptionOnConflict = 1 AND EXISTS (SELECT * FROM @ResourceInfos WHERE (PreviousVersion IS NOT NULL AND Version <= PreviousVersion) OR (PreviousSurrogateId IS NOT NULL AND SurrogateId <= PreviousSurrogateId))
            THROW 50409, 'Resource has been recently updated or added, please compare the resource content in code for any duplicate updates', 1

        INSERT INTO @PreviousSurrogateIds
            SELECT ResourceTypeId, PreviousSurrogateId, KeepHistory
                FROM @ResourceInfos
                WHERE PreviousSurrogateId IS NOT NULL

        IF @@rowcount > 0
        BEGIN
            UPDATE dbo.Resource
                SET IsHistory = 1
                WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId AND KeepHistory = 1)
            SET @AffectedRows += @@rowcount

            IF @IsResourceChangeCaptureEnabled = 1 AND NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = 'InvisibleHistory.IsEnabled' AND Number = 0)
                UPDATE dbo.Resource
                    SET IsHistory = 1
                         ,RawResource = 0xF
                         ,SearchParamHash = NULL
                         ,HistoryTransactionId = @TransactionId
                    WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId AND KeepHistory = 0)
            ELSE
                DELETE FROM dbo.Resource WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId AND KeepHistory = 0)
            SET @AffectedRows += @@rowcount

            DELETE FROM dbo.ResourceWriteClaim WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE SurrogateId = ResourceSurrogateId)
            SET @AffectedRows += @@rowcount
            DELETE FROM dbo.ReferenceSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
            SET @AffectedRows += @@rowcount
            DELETE FROM dbo.TokenSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
            SET @AffectedRows += @@rowcount
            DELETE FROM dbo.TokenText WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
            SET @AffectedRows += @@rowcount
            DELETE FROM dbo.StringSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
            SET @AffectedRows += @@rowcount
            DELETE FROM dbo.UriSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
            SET @AffectedRows += @@rowcount
            DELETE FROM dbo.NumberSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
            SET @AffectedRows += @@rowcount
            DELETE FROM dbo.QuantitySearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
            SET @AffectedRows += @@rowcount
            DELETE FROM dbo.DateTimeSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
            SET @AffectedRows += @@rowcount
            DELETE FROM dbo.VectorSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
            SET @AffectedRows += @@rowcount
            DELETE FROM dbo.ReferenceTokenCompositeSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
            SET @AffectedRows += @@rowcount
            DELETE FROM dbo.TokenTokenCompositeSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
            SET @AffectedRows += @@rowcount
            DELETE FROM dbo.TokenDateTimeCompositeSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
            SET @AffectedRows += @@rowcount
            DELETE FROM dbo.TokenQuantityCompositeSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
            SET @AffectedRows += @@rowcount
            DELETE FROM dbo.TokenStringCompositeSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
            SET @AffectedRows += @@rowcount
            DELETE FROM dbo.TokenNumberNumberCompositeSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
            SET @AffectedRows += @@rowcount
        END

        INSERT INTO dbo.Resource
                     ( ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash,  TransactionId )
            SELECT ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash, @TransactionId
                FROM @Resources
        SET @AffectedRows += @@rowcount

        INSERT INTO dbo.ResourceWriteClaim
                     ( ResourceSurrogateId, ClaimTypeId, ClaimValue )
            SELECT ResourceSurrogateId, ClaimTypeId, ClaimValue
                FROM @ResourceWriteClaims
        SET @AffectedRows += @@rowcount

        INSERT INTO dbo.ReferenceSearchParam
                     ( ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion )
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion
                FROM @ReferenceSearchParams
        SET @AffectedRows += @@rowcount

        INSERT INTO dbo.TokenSearchParam
                     ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow )
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow
                FROM @TokenSearchParams
        SET @AffectedRows += @@rowcount

        INSERT INTO dbo.TokenText
                     ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Text )
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Text
                FROM @TokenTexts
        SET @AffectedRows += @@rowcount

        INSERT INTO dbo.StringSearchParam
                     ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax )
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax
                FROM @StringSearchParams
        SET @AffectedRows += @@rowcount

        INSERT INTO dbo.UriSearchParam
                     ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri )
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri
                FROM @UriSearchParams
        SET @AffectedRows += @@rowcount

        INSERT INTO dbo.NumberSearchParam
                     ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue )
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue
                FROM @NumberSearchParams
        SET @AffectedRows += @@rowcount

        INSERT INTO dbo.QuantitySearchParam
                     ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue )
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue
                FROM @QuantitySearchParams
        SET @AffectedRows += @@rowcount

        INSERT INTO dbo.DateTimeSearchParam
                     ( ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax )
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax
                FROM @DateTimeSearchParms
        SET @AffectedRows += @@rowcount

        INSERT INTO dbo.VectorSearchParam
                     ( ResourceTypeId, ResourceSurrogateId, SearchParamId, ChunkOrdinal, EmbeddingModelId, ChunkText, SourceTextHash, SourceResourceTypeId, SourceResourceId, SourceResourceVersion, SourcePath, Embedding )
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, ChunkOrdinal, EmbeddingModelId, ChunkText, SourceTextHash, SourceResourceTypeId, SourceResourceId, SourceResourceVersion, SourcePath, CAST(Embedding AS vector(1536))
                FROM @VectorSearchParams
        SET @AffectedRows += @@rowcount

        INSERT INTO dbo.ReferenceTokenCompositeSearchParam
                     ( ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2 )
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2
                FROM @ReferenceTokenCompositeSearchParams
        SET @AffectedRows += @@rowcount

        INSERT INTO dbo.TokenTokenCompositeSearchParam
                     ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2 )
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2
                FROM @TokenTokenCompositeSearchParams
        SET @AffectedRows += @@rowcount

        INSERT INTO dbo.TokenDateTimeCompositeSearchParam
                     ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2 )
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2
                FROM @TokenDateTimeCompositeSearchParams
        SET @AffectedRows += @@rowcount

        INSERT INTO dbo.TokenQuantityCompositeSearchParam
                     ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2 )
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2
                FROM @TokenQuantityCompositeSearchParams
        SET @AffectedRows += @@rowcount

        INSERT INTO dbo.TokenStringCompositeSearchParam
                     ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2 )
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2
                FROM @TokenStringCompositeSearchParams
        SET @AffectedRows += @@rowcount

        INSERT INTO dbo.TokenNumberNumberCompositeSearchParam
                     ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange )
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange
                FROM @TokenNumberNumberCompositeSearchParams
        SET @AffectedRows += @@rowcount
    END
    ELSE
    BEGIN
        INSERT INTO dbo.ResourceWriteClaim
                     ( ResourceSurrogateId, ClaimTypeId, ClaimValue )
            SELECT ResourceSurrogateId, ClaimTypeId, ClaimValue
                FROM (SELECT TOP (@DummyTop) * FROM @ResourceWriteClaims) A
                WHERE EXISTS (SELECT * FROM @Existing B WHERE B.SurrogateId = A.ResourceSurrogateId)
                    AND NOT EXISTS (SELECT * FROM dbo.ResourceWriteClaim C WHERE C.ResourceSurrogateId = A.ResourceSurrogateId)
                OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
        SET @AffectedRows += @@rowcount

        INSERT INTO dbo.ReferenceSearchParam
                     ( ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion )
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion
                FROM (SELECT TOP (@DummyTop) * FROM @ReferenceSearchParams) A
                WHERE EXISTS (SELECT * FROM @Existing B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.SurrogateId = A.ResourceSurrogateId)
                    AND NOT EXISTS (SELECT * FROM dbo.ReferenceSearchParam C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
                OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
        SET @AffectedRows += @@rowcount

        INSERT INTO dbo.TokenSearchParam
                     ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow )
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow
                FROM (SELECT TOP (@DummyTop) * FROM @TokenSearchParams) A
                WHERE EXISTS (SELECT * FROM @Existing B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.SurrogateId = A.ResourceSurrogateId)
                    AND NOT EXISTS (SELECT * FROM dbo.TokenSearchParam C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
                OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
        SET @AffectedRows += @@rowcount

        INSERT INTO dbo.TokenText
                     ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Text )
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Text
                FROM (SELECT TOP (@DummyTop) * FROM @TokenTexts) A
                WHERE EXISTS (SELECT * FROM @Existing B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.SurrogateId = A.ResourceSurrogateId)
                    AND NOT EXISTS (SELECT * FROM dbo.TokenText C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
                OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
        SET @AffectedRows += @@rowcount

        INSERT INTO dbo.StringSearchParam
                     ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax )
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax
                FROM (SELECT TOP (@DummyTop) * FROM @StringSearchParams) A
                WHERE EXISTS (SELECT * FROM @Existing B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.SurrogateId = A.ResourceSurrogateId)
                    AND NOT EXISTS (SELECT * FROM dbo.StringSearchParam C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
                OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
        SET @AffectedRows += @@rowcount

        INSERT INTO dbo.UriSearchParam
                     ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri )
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri
                FROM (SELECT TOP (@DummyTop) * FROM @UriSearchParams) A
                WHERE EXISTS (SELECT * FROM @Existing B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.SurrogateId = A.ResourceSurrogateId)
                    AND NOT EXISTS (SELECT * FROM dbo.UriSearchParam C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
                OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
        SET @AffectedRows += @@rowcount

        INSERT INTO dbo.NumberSearchParam
                     ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue )
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue
                FROM (SELECT TOP (@DummyTop) * FROM @NumberSearchParams) A
                WHERE EXISTS (SELECT * FROM @Existing B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.SurrogateId = A.ResourceSurrogateId)
                    AND NOT EXISTS (SELECT * FROM dbo.NumberSearchParam C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
                OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
        SET @AffectedRows += @@rowcount

        INSERT INTO dbo.QuantitySearchParam
                     ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue )
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue
                FROM (SELECT TOP (@DummyTop) * FROM @QuantitySearchParams) A
                WHERE EXISTS (SELECT * FROM @Existing B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.SurrogateId = A.ResourceSurrogateId)
                    AND NOT EXISTS (SELECT * FROM dbo.QuantitySearchParam C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
                OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
        SET @AffectedRows += @@rowcount

        INSERT INTO dbo.DateTimeSearchParam
                     ( ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax )
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax
                FROM (SELECT TOP (@DummyTop) * FROM @DateTimeSearchParms) A
                WHERE EXISTS (SELECT * FROM @Existing B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.SurrogateId = A.ResourceSurrogateId)
                    AND NOT EXISTS (SELECT * FROM dbo.DateTimeSearchParam C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
                OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
        SET @AffectedRows += @@rowcount

        INSERT INTO dbo.VectorSearchParam
                     ( ResourceTypeId, ResourceSurrogateId, SearchParamId, ChunkOrdinal, EmbeddingModelId, ChunkText, SourceTextHash, SourceResourceTypeId, SourceResourceId, SourceResourceVersion, SourcePath, Embedding )
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, ChunkOrdinal, EmbeddingModelId, ChunkText, SourceTextHash, SourceResourceTypeId, SourceResourceId, SourceResourceVersion, SourcePath, CAST(Embedding AS vector(1536))
                FROM (SELECT TOP (@DummyTop) * FROM @VectorSearchParams) A
                WHERE EXISTS (SELECT * FROM @Existing B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.SurrogateId = A.ResourceSurrogateId)
                    AND NOT EXISTS (SELECT * FROM dbo.VectorSearchParam C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
                OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
        SET @AffectedRows += @@rowcount

        INSERT INTO dbo.ReferenceTokenCompositeSearchParam
                     ( ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2 )
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2
                FROM (SELECT TOP (@DummyTop) * FROM @ReferenceTokenCompositeSearchParams) A
                WHERE EXISTS (SELECT * FROM @Existing B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.SurrogateId = A.ResourceSurrogateId)
                    AND NOT EXISTS (SELECT * FROM dbo.ReferenceTokenCompositeSearchParam C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
                OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
        SET @AffectedRows += @@rowcount

        INSERT INTO dbo.TokenTokenCompositeSearchParam
                     ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2 )
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2
                FROM (SELECT TOP (@DummyTop) * FROM @TokenTokenCompositeSearchParams) A
                WHERE EXISTS (SELECT * FROM @Existing B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.SurrogateId = A.ResourceSurrogateId)
                    AND NOT EXISTS (SELECT * FROM dbo.TokenTokenCompositeSearchParam C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
                OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
        SET @AffectedRows += @@rowcount

        INSERT INTO dbo.TokenDateTimeCompositeSearchParam
                     ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2 )
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2
                FROM (SELECT TOP (@DummyTop) * FROM @TokenDateTimeCompositeSearchParams) A
                WHERE EXISTS (SELECT * FROM @Existing B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.SurrogateId = A.ResourceSurrogateId)
                    AND NOT EXISTS (SELECT * FROM dbo.TokenDateTimeCompositeSearchParam C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
                OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
        SET @AffectedRows += @@rowcount

        INSERT INTO dbo.TokenQuantityCompositeSearchParam
                     ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2 )
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2
                FROM (SELECT TOP (@DummyTop) * FROM @TokenQuantityCompositeSearchParams) A
                WHERE EXISTS (SELECT * FROM @Existing B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.SurrogateId = A.ResourceSurrogateId)
                    AND NOT EXISTS (SELECT * FROM dbo.TokenQuantityCompositeSearchParam C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
                OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
        SET @AffectedRows += @@rowcount

        INSERT INTO dbo.TokenStringCompositeSearchParam
                     ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2 )
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2
                FROM (SELECT TOP (@DummyTop) * FROM @TokenStringCompositeSearchParams) A
                WHERE EXISTS (SELECT * FROM @Existing B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.SurrogateId = A.ResourceSurrogateId)
                    AND NOT EXISTS (SELECT * FROM dbo.TokenStringCompositeSearchParam C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
                OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
        SET @AffectedRows += @@rowcount

        INSERT INTO dbo.TokenNumberNumberCompositeSearchParam
                     ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange )
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange
                FROM (SELECT TOP (@DummyTop) * FROM @TokenNumberNumberCompositeSearchParams) A
                WHERE EXISTS (SELECT * FROM @Existing B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.SurrogateId = A.ResourceSurrogateId)
                    AND NOT EXISTS (SELECT * FROM dbo.TokenNumberNumberCompositeSearchParam C WHERE C.ResourceTypeId = A.ResourceTypeId AND C.ResourceSurrogateId = A.ResourceSurrogateId)
                OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
        SET @AffectedRows += @@rowcount
    END

    IF @IsResourceChangeCaptureEnabled = 1
        EXECUTE dbo.CaptureResourceIdsForChanges @Resources

    IF @TransactionId IS NOT NULL
        EXECUTE dbo.MergeResourcesCommitTransaction @TransactionId

    IF @InitialTranCount = 0 AND @@trancount > 0 COMMIT TRANSACTION

    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@AffectedRows
END TRY
BEGIN CATCH
    IF @InitialTranCount = 0 AND @@trancount > 0 ROLLBACK TRANSACTION
    IF error_number() = 1750 THROW

    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;

    IF @RaiseExceptionOnConflict = 1 AND error_message() LIKE '%''dbo.Resource''%'
    BEGIN
        IF error_number() = 2601
            THROW 50409, 'Resource has been recently updated or added, please compare the resource content in code for any duplicate updates.', 1;
        ELSE IF error_number() = 2627
            THROW 50424, 'Cannot persit resource due to a conflict with duplicated keys. Check the volume of resource being submited for ingestion.', 1;
        ELSE
            THROW;
    END
    ELSE
        THROW;
END CATCH
GO

ALTER PROCEDURE dbo.MergeResourcesAndSearchParams
         @SearchParams dbo.SearchParamList READONLY
        ,@ReindexId bigint = NULL
        ,@IsResourceChangeCaptureEnabled bit = 0
        ,@TransactionId bigint = NULL
        ,@Resources dbo.ResourceList READONLY
        ,@ResourceWriteClaims dbo.ResourceWriteClaimList READONLY
        ,@ReferenceSearchParams dbo.ReferenceSearchParamList READONLY
        ,@TokenSearchParams dbo.TokenSearchParamList READONLY
        ,@TokenTexts dbo.TokenTextList READONLY
        ,@StringSearchParams dbo.StringSearchParamList READONLY
        ,@UriSearchParams dbo.UriSearchParamList READONLY
        ,@NumberSearchParams dbo.NumberSearchParamList READONLY
        ,@QuantitySearchParams dbo.QuantitySearchParamList READONLY
        ,@DateTimeSearchParms dbo.DateTimeSearchParamList READONLY
        ,@VectorSearchParams dbo.VectorSearchParamList READONLY
        ,@ReferenceTokenCompositeSearchParams dbo.ReferenceTokenCompositeSearchParamList READONLY
        ,@TokenTokenCompositeSearchParams dbo.TokenTokenCompositeSearchParamList READONLY
        ,@TokenDateTimeCompositeSearchParams dbo.TokenDateTimeCompositeSearchParamList READONLY
        ,@TokenQuantityCompositeSearchParams dbo.TokenQuantityCompositeSearchParamList READONLY
        ,@TokenStringCompositeSearchParams dbo.TokenStringCompositeSearchParamList READONLY
        ,@TokenNumberNumberCompositeSearchParams dbo.TokenNumberNumberCompositeSearchParamList READONLY
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
             ,@Mode varchar(200) = 'R='+convert(varchar,(SELECT count(*) FROM @Resources))+' SP='+convert(varchar,(SELECT count(*) FROM @SearchParams))
             ,@st datetime = getUTCdate()
             ,@Rows int = 0

BEGIN TRY
    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE

    BEGIN TRANSACTION

    EXECUTE dbo.MergeSearchParams @SearchParams, @ReindexId

    IF EXISTS (SELECT * FROM @Resources)
        EXECUTE dbo.MergeResources
                         @AffectedRows = @Rows OUTPUT
                        ,@RaiseExceptionOnConflict = 1
                        ,@IsResourceChangeCaptureEnabled = @IsResourceChangeCaptureEnabled
                        ,@TransactionId = @TransactionId
                        ,@SingleTransaction = 1
                        ,@Resources = @Resources
                        ,@ResourceWriteClaims = @ResourceWriteClaims
                        ,@ReferenceSearchParams = @ReferenceSearchParams
                        ,@TokenSearchParams = @TokenSearchParams
                        ,@TokenTexts = @TokenTexts
                        ,@StringSearchParams = @StringSearchParams
                        ,@UriSearchParams = @UriSearchParams
                        ,@NumberSearchParams = @NumberSearchParams
                        ,@QuantitySearchParams = @QuantitySearchParams
                        ,@DateTimeSearchParms = @DateTimeSearchParms
                        ,@VectorSearchParams = @VectorSearchParams
                        ,@ReferenceTokenCompositeSearchParams = @ReferenceTokenCompositeSearchParams
                        ,@TokenTokenCompositeSearchParams = @TokenTokenCompositeSearchParams
                        ,@TokenDateTimeCompositeSearchParams = @TokenDateTimeCompositeSearchParams
                        ,@TokenQuantityCompositeSearchParams = @TokenQuantityCompositeSearchParams
                        ,@TokenStringCompositeSearchParams = @TokenStringCompositeSearchParams
                        ,@TokenNumberNumberCompositeSearchParams = @TokenNumberNumberCompositeSearchParams;
    ELSE
        IF @TransactionId IS NOT NULL
            EXECUTE dbo.MergeResourcesCommitTransaction @TransactionId

    COMMIT TRANSACTION

    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Action='Merge',@Rows=@Rows
END TRY
BEGIN CATCH
    IF @@trancount > 0 ROLLBACK TRANSACTION;
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
    THROW
END CATCH
GO
