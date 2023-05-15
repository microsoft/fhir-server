declare @precision tinyint
set @precision = (SELECT precision FROM sys.columns 
                      WHERE object_id  = (select object_id from sys.tables where name = 'NumberSearchParam') 
                      AND name = 'SingleValue');
IF (@precision != 36)
BEGIN
    INSERT INTO dbo.Parameters (Id, Char) SELECT 'Creating temp tables', 'LogEvent'
    EXECUTE dbo.LogEvent @Process='Creating temp tables',@Status='Start'
    IF object_id('dbo.Tmp_NumberSearchParam') IS NULL
        CREATE TABLE dbo.Tmp_NumberSearchParam (
            ResourceTypeId      SMALLINT        NOT NULL,
            ResourceSurrogateId BIGINT          NOT NULL,
            SearchParamId       SMALLINT        NOT NULL,
            SingleValue         DECIMAL (36, 18) NULL,
            LowValue            DECIMAL (36, 18) NOT NULL,
            HighValue           DECIMAL (36, 18) NOT NULL,
            IsHistory           BIT             NOT NULL
        );
    IF object_id('DF_Tmp_NumberSearchParam_IsHistory') IS NULL
        ALTER TABLE dbo.Tmp_NumberSearchParam
            ADD CONSTRAINT DF_Tmp_NumberSearchParam_IsHistory DEFAULT 0 FOR IsHistory;

    ALTER TABLE dbo.Tmp_NumberSearchParam SET (LOCK_ESCALATION = AUTO);

    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('dbo.Tmp_NumberSearchParam') AND name = 'IXC_NumberSearchParam')
        CREATE CLUSTERED INDEX IXC_NumberSearchParam
            ON dbo.Tmp_NumberSearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId)
            ON PartitionScheme_ResourceTypeId (ResourceTypeId);


    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('dbo.Tmp_NumberSearchParam') AND name = 'IX_NumberSearchParam_SearchParamId_SingleValue')
        CREATE NONCLUSTERED INDEX IX_NumberSearchParam_SearchParamId_SingleValue
            ON dbo.Tmp_NumberSearchParam(ResourceTypeId, SearchParamId, SingleValue, ResourceSurrogateId) WHERE IsHistory = 0
                                                                                                            AND SingleValue IS NOT NULL
            ON PartitionScheme_ResourceTypeId (ResourceTypeId);
    

    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('dbo.Tmp_NumberSearchParam') AND name = 'IX_NumberSearchParam_SearchParamId_LowValue_HighValue')
        CREATE NONCLUSTERED INDEX IX_NumberSearchParam_SearchParamId_LowValue_HighValue
            ON dbo.Tmp_NumberSearchParam(ResourceTypeId, SearchParamId, LowValue, HighValue, ResourceSurrogateId) WHERE IsHistory = 0
            ON PartitionScheme_ResourceTypeId (ResourceTypeId);


    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('dbo.Tmp_NumberSearchParam') AND name = 'IX_NumberSearchParam_SearchParamId_HighValue_LowValue')
        CREATE NONCLUSTERED INDEX IX_NumberSearchParam_SearchParamId_HighValue_LowValue
            ON dbo.Tmp_NumberSearchParam(ResourceTypeId, SearchParamId, HighValue, LowValue, ResourceSurrogateId) WHERE IsHistory = 0
            ON PartitionScheme_ResourceTypeId (ResourceTypeId);

    IF object_id('dbo.Tmp_QuantitySearchParam') IS NULL
        CREATE TABLE dbo.Tmp_QuantitySearchParam (
            ResourceTypeId      SMALLINT        NOT NULL,
            ResourceSurrogateId BIGINT          NOT NULL,
            SearchParamId       SMALLINT        NOT NULL,
            SystemId            INT             NULL,
            QuantityCodeId      INT             NULL,
            SingleValue         DECIMAL (36, 18) NULL,
            LowValue            DECIMAL (36, 18) NOT NULL,
            HighValue           DECIMAL (36, 18) NOT NULL,
            IsHistory           BIT             NOT NULL
        );

    IF object_id('DF_Tmp_QuantitySearchParam_IsHistory') IS NULL
        ALTER TABLE dbo.Tmp_QuantitySearchParam
            ADD CONSTRAINT DF_Tmp_QuantitySearchParam_IsHistory DEFAULT 0 FOR IsHistory;

    ALTER TABLE dbo.Tmp_QuantitySearchParam SET (LOCK_ESCALATION = AUTO);

    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('dbo.Tmp_QuantitySearchParam') AND name = 'IXC_QuantitySearchParam')
        CREATE CLUSTERED INDEX IXC_QuantitySearchParam
            ON dbo.Tmp_QuantitySearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId)
            ON PartitionScheme_ResourceTypeId (ResourceTypeId);

    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('dbo.Tmp_QuantitySearchParam') AND name = 'IX_QuantitySearchParam_SearchParamId_QuantityCodeId_SingleValue')
        CREATE NONCLUSTERED INDEX IX_QuantitySearchParam_SearchParamId_QuantityCodeId_SingleValue
            ON dbo.Tmp_QuantitySearchParam(ResourceTypeId, SearchParamId, QuantityCodeId, SingleValue, ResourceSurrogateId)
            INCLUDE(SystemId) WHERE IsHistory = 0
                                    AND SingleValue IS NOT NULL
            ON PartitionScheme_ResourceTypeId (ResourceTypeId);

    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('dbo.Tmp_QuantitySearchParam') AND name = 'IX_QuantitySearchParam_SearchParamId_QuantityCodeId_LowValue_HighValue')
        CREATE NONCLUSTERED INDEX IX_QuantitySearchParam_SearchParamId_QuantityCodeId_LowValue_HighValue
            ON dbo.Tmp_QuantitySearchParam(ResourceTypeId, SearchParamId, QuantityCodeId, LowValue, HighValue, ResourceSurrogateId)
            INCLUDE(SystemId) WHERE IsHistory = 0
            ON PartitionScheme_ResourceTypeId (ResourceTypeId);

    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('dbo.Tmp_QuantitySearchParam') AND name = 'IX_QuantitySearchParam_SearchParamId_QuantityCodeId_HighValue_LowValue')
        CREATE NONCLUSTERED INDEX IX_QuantitySearchParam_SearchParamId_QuantityCodeId_HighValue_LowValue
            ON dbo.Tmp_QuantitySearchParam(ResourceTypeId, SearchParamId, QuantityCodeId, HighValue, LowValue, ResourceSurrogateId)
            INCLUDE(SystemId) WHERE IsHistory = 0
            ON PartitionScheme_ResourceTypeId (ResourceTypeId);

    IF object_id('dbo.Tmp_TokenNumberNumberCompositeSearchParam') IS NULL
        CREATE TABLE dbo.Tmp_TokenNumberNumberCompositeSearchParam (
            ResourceTypeId      SMALLINT        NOT NULL,
            ResourceSurrogateId BIGINT          NOT NULL,
            SearchParamId       SMALLINT        NOT NULL,
            SystemId1           INT             NULL,
            Code1               VARCHAR (256)   COLLATE Latin1_General_100_CS_AS NOT NULL,
            SingleValue2        DECIMAL (36, 18) NULL,
            LowValue2           DECIMAL (36, 18) NULL,
            HighValue2          DECIMAL (36, 18) NULL,
            SingleValue3        DECIMAL (36, 18) NULL,
            LowValue3           DECIMAL (36, 18) NULL,
            HighValue3          DECIMAL (36, 18) NULL,
            HasRange            BIT             NOT NULL,
            IsHistory           BIT             NOT NULL,
            CodeOverflow1       VARCHAR (MAX)   COLLATE Latin1_General_100_CS_AS NULL
        );

    IF object_id('DF_Tmp_TokenNumberNumberCompositeSearchParam_IsHistory') IS NULL
        ALTER TABLE dbo.Tmp_TokenNumberNumberCompositeSearchParam
            ADD CONSTRAINT DF_Tmp_TokenNumberNumberCompositeSearchParam_IsHistory DEFAULT 0 FOR IsHistory;

    IF object_id('CHK_Tmp_TokenNumberNumberCompositeSearchParam_CodeOverflow1') IS NULL
        ALTER TABLE dbo.Tmp_TokenNumberNumberCompositeSearchParam
            ADD CONSTRAINT CHK_Tmp_TokenNumberNumberCompositeSearchParam_CodeOverflow1 CHECK (LEN(Code1) = 256
                                                                                            OR CodeOverflow1 IS NULL);

    ALTER TABLE dbo.Tmp_TokenNumberNumberCompositeSearchParam SET (LOCK_ESCALATION = AUTO);

    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('dbo.Tmp_TokenNumberNumberCompositeSearchParam') AND name = 'IXC_TokenNumberNumberCompositeSearchParam')
        CREATE CLUSTERED INDEX IXC_TokenNumberNumberCompositeSearchParam
            ON dbo.Tmp_TokenNumberNumberCompositeSearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId) WITH (DATA_COMPRESSION = PAGE)
            ON PartitionScheme_ResourceTypeId (ResourceTypeId);

    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('dbo.Tmp_TokenNumberNumberCompositeSearchParam') AND name = 'IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_Text2')
        CREATE NONCLUSTERED INDEX IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_Text2
            ON dbo.Tmp_TokenNumberNumberCompositeSearchParam(ResourceTypeId, SearchParamId, Code1, SingleValue2, SingleValue3, ResourceSurrogateId)
            INCLUDE(SystemId1) WHERE IsHistory = 0
                                        AND HasRange = 0 WITH (DATA_COMPRESSION = PAGE)
            ON PartitionScheme_ResourceTypeId (ResourceTypeId);

    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('dbo.Tmp_TokenNumberNumberCompositeSearchParam') AND name = 'IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_LowValue2_HighValue2_LowValue3_HighValue3')
        CREATE NONCLUSTERED INDEX IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_LowValue2_HighValue2_LowValue3_HighValue3
            ON dbo.Tmp_TokenNumberNumberCompositeSearchParam(ResourceTypeId, SearchParamId, Code1, LowValue2, HighValue2, LowValue3, HighValue3, ResourceSurrogateId)
            INCLUDE(SystemId1) WHERE IsHistory = 0
                                        AND HasRange = 1 WITH (DATA_COMPRESSION = PAGE)
            ON PartitionScheme_ResourceTypeId (ResourceTypeId);

    IF object_id('dbo.Tmp_TokenQuantityCompositeSearchParam') IS NULL
        CREATE TABLE dbo.Tmp_TokenQuantityCompositeSearchParam (
            ResourceTypeId      SMALLINT        NOT NULL,
            ResourceSurrogateId BIGINT          NOT NULL,
            SearchParamId       SMALLINT        NOT NULL,
            SystemId1           INT             NULL,
            Code1               VARCHAR (256)   COLLATE Latin1_General_100_CS_AS NOT NULL,
            SystemId2           INT             NULL,
            QuantityCodeId2     INT             NULL,
            SingleValue2        DECIMAL (36, 18) NULL,
            LowValue2           DECIMAL (36, 18) NULL,
            HighValue2          DECIMAL (36, 18) NULL,
            IsHistory           BIT             NOT NULL,
            CodeOverflow1       VARCHAR (MAX)   COLLATE Latin1_General_100_CS_AS NULL
        );
    IF object_id('DF_Tmp_TokenQuantityCompositeSearchParam_IsHistory') IS NULL
        ALTER TABLE dbo.Tmp_TokenQuantityCompositeSearchParam
            ADD CONSTRAINT DF_Tmp_TokenQuantityCompositeSearchParam_IsHistory DEFAULT 0 FOR IsHistory;

    IF object_id('CHK_Tmp_TokenQuantityCompositeSearchParam_CodeOverflow1') IS NULL
        ALTER TABLE dbo.Tmp_TokenQuantityCompositeSearchParam
            ADD CONSTRAINT CHK_Tmp_TokenQuantityCompositeSearchParam_CodeOverflow1 CHECK (LEN(Code1) = 256
                                                                                        OR CodeOverflow1 IS NULL);

    ALTER TABLE dbo.Tmp_TokenQuantityCompositeSearchParam SET (LOCK_ESCALATION = AUTO);

    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('dbo.Tmp_TokenQuantityCompositeSearchParam') AND name = 'IXC_TokenQuantityCompositeSearchParam')
        CREATE CLUSTERED INDEX IXC_TokenQuantityCompositeSearchParam
            ON dbo.Tmp_TokenQuantityCompositeSearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId) WITH (DATA_COMPRESSION = PAGE)
            ON PartitionScheme_ResourceTypeId (ResourceTypeId);


    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('dbo.Tmp_TokenQuantityCompositeSearchParam') AND name = 'IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_SingleValue2')
        CREATE NONCLUSTERED INDEX IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_SingleValue2
            ON dbo.Tmp_TokenQuantityCompositeSearchParam(ResourceTypeId, SearchParamId, Code1, SingleValue2, ResourceSurrogateId)
            INCLUDE(QuantityCodeId2, SystemId1, SystemId2) WHERE IsHistory = 0
                                                                    AND SingleValue2 IS NOT NULL WITH (DATA_COMPRESSION = PAGE)
            ON PartitionScheme_ResourceTypeId (ResourceTypeId);


    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('dbo.Tmp_TokenQuantityCompositeSearchParam') AND name = 'IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_LowValue2_HighValue2')
        CREATE NONCLUSTERED INDEX IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_LowValue2_HighValue2
            ON dbo.Tmp_TokenQuantityCompositeSearchParam(ResourceTypeId, SearchParamId, Code1, LowValue2, HighValue2, ResourceSurrogateId)
            INCLUDE(QuantityCodeId2, SystemId1, SystemId2) WHERE IsHistory = 0
                                                                    AND LowValue2 IS NOT NULL WITH (DATA_COMPRESSION = PAGE)
            ON PartitionScheme_ResourceTypeId (ResourceTypeId);


    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('dbo.Tmp_TokenQuantityCompositeSearchParam') AND name = 'IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_HighValue2_LowValue2')
        CREATE NONCLUSTERED INDEX IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_HighValue2_LowValue2
            ON dbo.Tmp_TokenQuantityCompositeSearchParam(ResourceTypeId, SearchParamId, Code1, HighValue2, LowValue2, ResourceSurrogateId)
            INCLUDE(QuantityCodeId2, SystemId1, SystemId2) WHERE IsHistory = 0
                                                                    AND LowValue2 IS NOT NULL WITH (DATA_COMPRESSION = PAGE)
            ON PartitionScheme_ResourceTypeId (ResourceTypeId);

    IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'Tmp_NumberSearchParamList')
        CREATE TYPE dbo.Tmp_NumberSearchParamList AS TABLE (
            ResourceTypeId      SMALLINT        NOT NULL,
            ResourceSurrogateId BIGINT          NOT NULL,
            SearchParamId       SMALLINT        NOT NULL,
            SingleValue         DECIMAL (36, 18) NULL,
            LowValue            DECIMAL (36, 18) NULL,
            HighValue           DECIMAL (36, 18) NULL UNIQUE (ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue));

    IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'Tmp_QuantitySearchParamList')
        CREATE TYPE dbo.Tmp_QuantitySearchParamList AS TABLE (
            ResourceTypeId      SMALLINT        NOT NULL,
            ResourceSurrogateId BIGINT          NOT NULL,
            SearchParamId       SMALLINT        NOT NULL,
            SystemId            INT             NULL,
            QuantityCodeId      INT             NULL,
            SingleValue         DECIMAL (36, 18) NULL,
            LowValue            DECIMAL (36, 18) NULL,
            HighValue           DECIMAL (36, 18) NULL UNIQUE (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue));


    IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'Tmp_TokenNumberNumberCompositeSearchParamList')
        CREATE TYPE dbo.Tmp_TokenNumberNumberCompositeSearchParamList AS TABLE (
            ResourceTypeId      SMALLINT        NOT NULL,
            ResourceSurrogateId BIGINT          NOT NULL,
            SearchParamId       SMALLINT        NOT NULL,
            SystemId1           INT             NULL,
            Code1               VARCHAR (256)   COLLATE Latin1_General_100_CS_AS NOT NULL,
            CodeOverflow1       VARCHAR (MAX)   COLLATE Latin1_General_100_CS_AS NULL,
            SingleValue2        DECIMAL (36, 18) NULL,
            LowValue2           DECIMAL (36, 18) NULL,
            HighValue2          DECIMAL (36, 18) NULL,
            SingleValue3        DECIMAL (36, 18) NULL,
            LowValue3           DECIMAL (36, 18) NULL,
            HighValue3          DECIMAL (36, 18) NULL,
            HasRange            BIT             NOT NULL);


    IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'Tmp_TokenQuantityCompositeSearchParamList')
        CREATE TYPE dbo.Tmp_TokenQuantityCompositeSearchParamList AS TABLE (
            ResourceTypeId      SMALLINT        NOT NULL,
            ResourceSurrogateId BIGINT          NOT NULL,
            SearchParamId       SMALLINT        NOT NULL,
            SystemId1           INT             NULL,
            Code1               VARCHAR (256)   COLLATE Latin1_General_100_CS_AS NOT NULL,
            CodeOverflow1       VARCHAR (MAX)   COLLATE Latin1_General_100_CS_AS NULL,
            SystemId2           INT             NULL,
            QuantityCodeId2     INT             NULL,
            SingleValue2        DECIMAL (36, 18) NULL,
            LowValue2           DECIMAL (36, 18) NULL,
            HighValue2          DECIMAL (36, 18) NULL);

    EXECUTE dbo.LogEvent @Process='Creating temp tables',@Status='End'

    BEGIN TRY
        INSERT INTO dbo.Parameters (Id, Char) SELECT 'Alter Procedure', 'LogEvent'
        EXECUTE dbo.LogEvent @Process='Alter Procedure',@Status='Start'
        EXECUTE(
'ALTER PROCEDURE dbo.MergeResources
-- This stored procedure can be used for:
-- 1. Ordinary put with single version per resource in input
-- 2. Put with history preservation (multiple input versions per resource)
-- 3. Copy from one gen2 store to another with ResourceSurrogateId preserved.
    @AffectedRows int = 0 OUT
    ,@RaiseExceptionOnConflict bit = 1
    ,@IsResourceChangeCaptureEnabled bit = 0
    ,@Resources dbo.ResourceList READONLY
    ,@ResourceWriteClaims dbo.ResourceWriteClaimList READONLY
    ,@CompartmentAssignments dbo.CompartmentAssignmentList READONLY
    ,@ReferenceSearchParams dbo.ReferenceSearchParamList READONLY
    ,@TokenSearchParams dbo.TokenSearchParamList READONLY
    ,@TokenTexts dbo.TokenTextList READONLY
    ,@StringSearchParams dbo.StringSearchParamList READONLY
    ,@UriSearchParams dbo.UriSearchParamList READONLY
    ,@NumberSearchParams dbo.Tmp_NumberSearchParamList READONLY
    ,@QuantitySearchParams dbo.Tmp_QuantitySearchParamList READONLY
    ,@DateTimeSearchParms dbo.DateTimeSearchParamList READONLY
    ,@ReferenceTokenCompositeSearchParams dbo.ReferenceTokenCompositeSearchParamList READONLY
    ,@TokenTokenCompositeSearchParams dbo.TokenTokenCompositeSearchParamList READONLY
    ,@TokenDateTimeCompositeSearchParams dbo.TokenDateTimeCompositeSearchParamList READONLY
    ,@TokenQuantityCompositeSearchParams dbo.Tmp_TokenQuantityCompositeSearchParamList READONLY
    ,@TokenStringCompositeSearchParams dbo.TokenStringCompositeSearchParamList READONLY
    ,@TokenNumberNumberCompositeSearchParams dbo.Tmp_TokenNumberNumberCompositeSearchParamList READONLY
AS
set nocount on
DECLARE @st datetime = getUTCdate()
        ,@SP varchar(100) = ''MergeResources''
        ,@DummyTop bigint = 9223372036854775807
        ,@InitialTranCount int = @@trancount

DECLARE @Mode varchar(200) = isnull((SELECT ''RT=[''+convert(varchar,min(ResourceTypeId))+'',''+convert(varchar,max(ResourceTypeId))+''] MinSur=''+convert(varchar,min(ResourceSurrogateId))+'' Rows=''+convert(varchar,count(*)) FROM @Resources),''Input=Empty'')
SET @Mode += '' ITC=''+convert(varchar,@InitialTranCount)+'' E=''+convert(varchar,@RaiseExceptionOnConflict)+'' CC=''+convert(varchar,@IsResourceChangeCaptureEnabled)

SET @AffectedRows = 0

BEGIN TRY
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

    IF @InitialTranCount = 0 BEGIN TRANSACTION
  
    INSERT INTO @ResourceInfos
        (
            ResourceTypeId
            ,SurrogateId
            ,Version
            ,KeepHistory
            ,PreviousVersion
            ,PreviousSurrogateId
        )
    SELECT A.ResourceTypeId
            ,A.ResourceSurrogateId
            ,A.Version
            ,A.KeepHistory
            ,B.Version
            ,B.ResourceSurrogateId
        FROM (SELECT TOP (@DummyTop) * FROM @Resources WHERE HasVersionToCompare = 1) A
            LEFT OUTER JOIN dbo.Resource B -- WITH (UPDLOCK, HOLDLOCK) These locking hints cause deadlocks and are not needed. Racing might lead to tries to insert dups in unique index (with version key), but it will fail anyway, and in no case this will cause incorrect data saved.
                ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId AND B.IsHistory = 0
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))

    IF @RaiseExceptionOnConflict = 1 AND EXISTS (SELECT * FROM @ResourceInfos WHERE PreviousVersion IS NOT NULL AND Version <> PreviousVersion + 1)
    THROW 50409, ''Resource has been recently updated or added, please compare the resource content in code for any duplicate updates'', 1

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
    
    DELETE FROM dbo.Resource WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId AND KeepHistory = 0)
    SET @AffectedRows += @@rowcount

    DELETE FROM dbo.ResourceWriteClaim WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE SurrogateId = ResourceSurrogateId)
    SET @AffectedRows += @@rowcount
    DELETE FROM dbo.CompartmentAssignment WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
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
	DELETE FROM dbo.Tmp_NumberSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
	SET @AffectedRows += @@rowcount	
    DELETE FROM dbo.QuantitySearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
    SET @AffectedRows += @@rowcount
	DELETE FROM dbo.Tmp_QuantitySearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
	SET @AffectedRows += @@rowcount	
    DELETE FROM dbo.DateTimeSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
    SET @AffectedRows += @@rowcount
    DELETE FROM dbo.ReferenceTokenCompositeSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
    SET @AffectedRows += @@rowcount
    DELETE FROM dbo.TokenTokenCompositeSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
    SET @AffectedRows += @@rowcount
    DELETE FROM dbo.TokenDateTimeCompositeSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
    SET @AffectedRows += @@rowcount
    DELETE FROM dbo.TokenQuantityCompositeSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
    SET @AffectedRows += @@rowcount
	DELETE FROM dbo.Tmp_TokenQuantityCompositeSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
	SET @AffectedRows += @@rowcount	
    DELETE FROM dbo.TokenStringCompositeSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
    SET @AffectedRows += @@rowcount
    DELETE FROM dbo.TokenNumberNumberCompositeSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
    SET @AffectedRows += @@rowcount
	DELETE FROM dbo.Tmp_TokenNumberNumberCompositeSearchParam WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
	SET @AffectedRows += @@rowcount	

    --EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status=''Info'',@Start=@st,@Rows=@AffectedRows,@Text=''Old rows''
    END

    INSERT INTO dbo.Resource 
            ( ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash )
    SELECT ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash
        FROM @Resources
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.ResourceWriteClaim 
            ( ResourceSurrogateId, ClaimTypeId, ClaimValue )
    SELECT ResourceSurrogateId, ClaimTypeId, ClaimValue
        FROM @ResourceWriteClaims
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.CompartmentAssignment 
            ( ResourceTypeId, ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId )
    SELECT ResourceTypeId, ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId
        FROM @CompartmentAssignments
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
  
    INSERT INTO dbo.Tmp_NumberSearchParam 
		    ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue )
	SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue
	    FROM @NumberSearchParams
    SET @AffectedRows += @@rowcount  

    INSERT INTO dbo.QuantitySearchParam 
            ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue
        FROM @QuantitySearchParams
    SET @AffectedRows += @@rowcount
  
    INSERT INTO dbo.Tmp_QuantitySearchParam 
		    ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue )
	SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue
	    FROM @QuantitySearchParams
    SET @AffectedRows += @@rowcount  

    INSERT INTO dbo.DateTimeSearchParam 
            ( ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax
        FROM @DateTimeSearchParms
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
  
    INSERT INTO dbo.Tmp_TokenQuantityCompositeSearchParam 
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
  
    INSERT INTO dbo.Tmp_TokenNumberNumberCompositeSearchParam 
		    ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange )
	SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange
	    FROM @TokenNumberNumberCompositeSearchParams
    SET @AffectedRows += @@rowcount  

    IF @IsResourceChangeCaptureEnabled = 1 --If the resource change capture feature is enabled, to execute a stored procedure called CaptureResourceChanges to insert resource change data.
    EXECUTE dbo.CaptureResourceIdsForChanges @Resources

    IF @InitialTranCount = 0 COMMIT TRANSACTION

    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status=''End'',@Start=@st,@Rows=@AffectedRows
END TRY
BEGIN CATCH
    IF @InitialTranCount = 0 AND @@trancount > 0 ROLLBACK TRANSACTION
    IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.

    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status=''Error'',@Start=@st;

    IF @RaiseExceptionOnConflict = 1 AND error_number() = 2601 AND error_message() LIKE ''%''''dbo.Resource''''%version%''
    THROW 50409, ''Resource has been recently updated or added, please compare the resource content in code for any duplicate updates'', 1;
    ELSE
    THROW
END CATCH')
        INSERT INTO dbo.Parameters (Id, Char) SELECT 'Insert MaxSurrogateId', 'LogEvent'
        EXECUTE dbo.LogEvent @Process='Insert MaxSurrogateId',@Status='Start'
        INSERT INTO dbo.Parameters (Id, Bigint) SELECT 'PrecisionUpdate.MaxSurrogateId', max(ResourceSurrogateId) from resource
    END TRY
    BEGIN CATCH
        EXECUTE dbo.LogEvent @Process="Alter Procedure",@Status="Error"
        ROLLBACK TRANSACTION
    END CATCH

    DECLARE @ProcessName varchar(100) = 'CopySearchParamData'
            ,@cst datetime = getUTCdate()
            ,@Id varchar(100) = 'PrecisionUpdate.LastProcessed.TypeId.SurrogateId'
            ,@ResourceTypeId smallint
            ,@SurrogateId bigint
            ,@CopyUntilSurrogateId bigint
            ,@RowsToProcess int
            ,@ProcessedResources int = 0
            ,@CopiedResources int = 0
            ,@CopiedSearchParams int = 0
            ,@ReportDate datetime = getUTCdate()
    BEGIN TRY
        INSERT INTO dbo.Parameters (Id, Char) SELECT @ProcessName, 'LogEvent'

        EXECUTE dbo.LogEvent @Process=@ProcessName,@Status='Start'

        INSERT INTO dbo.Parameters (Id, Char) SELECT @Id, '0.0' WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = @Id)

        DECLARE @LastProcessed varchar(100) = (SELECT Char FROM dbo.Parameters WHERE Id = @Id)

        DECLARE @Types TABLE (ResourceTypeId smallint PRIMARY KEY, Name varchar(100))
        DECLARE @SurrogateIds TABLE (ResourceSurrogateId bigint)
  
        INSERT INTO @Types EXECUTE dbo.GetUsedResourceTypes
        EXECUTE dbo.LogEvent @Process=@ProcessName,@Status='Run',@Target='@Types',@Action='Insert',@Rows=@@rowcount

        SET @ResourceTypeId = substring(@LastProcessed, 1, charindex('.', @LastProcessed) - 1) -- (SELECT value FROM string_split(@LastProcessed, '.', 1) WHERE ordinal = 1)
        SET @SurrogateId = substring(@LastProcessed, charindex('.', @LastProcessed) + 1, 255) -- (SELECT value FROM string_split(@LastProcessed, '.', 1) WHERE ordinal = 2)

        DELETE FROM @Types WHERE ResourceTypeId < @ResourceTypeId
        EXECUTE dbo.LogEvent @Process=@ProcessName,@Status='Run',@Target='@Types',@Action='Delete',@Rows=@@rowcount

        WHILE EXISTS (SELECT * FROM @Types) -- Processing in ASC order
        BEGIN
            SET @ResourceTypeId = (SELECT TOP 1 ResourceTypeId FROM @Types ORDER BY ResourceTypeId)
            SET @CopyUntilSurrogateId = (SELECT Bigint FROM dbo.Parameters WHERE Id = 'PrecisionUpdate.MaxSurrogateId')

            SET @ProcessedResources = 0
            SET @CopiedSearchParams = 0
            SET @RowsToProcess = 1
            WHILE @RowsToProcess > 0
            BEGIN
                DELETE FROM @SurrogateIds

                INSERT INTO @SurrogateIds
                    SELECT TOP 10000
                            ResourceSurrogateId
                        FROM dbo.Resource
                        WHERE ResourceTypeId = @ResourceTypeId
                        AND ResourceSurrogateId > @SurrogateId
                        AND ResourceSurrogateId <= @CopyUntilSurrogateId
                        AND IsHistory = 0
                        ORDER BY
                            ResourceSurrogateId
                SET @RowsToProcess = @@rowcount
                SET @ProcessedResources += @RowsToProcess

                IF @RowsToProcess > 0
                SET @SurrogateId = (SELECT max(ResourceSurrogateId) FROM @SurrogateIds)

                SET @LastProcessed = convert(varchar,@ResourceTypeId)+'.'+convert(varchar,@SurrogateId)

     
                IF EXISTS (SELECT * FROM @SurrogateIds)
                BEGIN
                    INSERT INTO dbo.Tmp_NumberSearchParam
                             ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue )
                        SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue
                        FROM dbo.NumberSearchParam B  
                        WHERE ResourceTypeId = @ResourceTypeId 
                            AND EXISTS (SELECT * FROM @SurrogateIds C WHERE C.ResourceSurrogateId = B.ResourceSurrogateId)
                            AND NOT EXISTS (SELECT * 
                                            FROM Tmp_NumberSearchParam A 
                                            WHERE A.ResourceTypeId = @ResourceTypeId 
                                                AND A.ResourceSurrogateId = B.ResourceSurrogateId)

                    DECLARE @myRows bigint = @@rowcount
                    EXECUTE dbo.LogEvent @Process=@ProcessName,@Status='Run',@Target='Tmp_NumberSearchParam',@Action='Insert',@Rows=@myRows
                    SET @CopiedSearchParams += @myRows

                    INSERT INTO dbo.Tmp_QuantitySearchParam 
                             ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue )
                        SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue
                            FROM QuantitySearchParam B
                            WHERE ResourceTypeId = @ResourceTypeId 
                                AND EXISTS (SELECT * FROM @SurrogateIds C WHERE C.ResourceSurrogateId = B.ResourceSurrogateId)
                                AND NOT EXISTS (SELECT * 
                                                    FROM Tmp_QuantitySearchParam A 
                                                    WHERE A.ResourceTypeId = @ResourceTypeId 
                                                    AND A.ResourceSurrogateId = B.ResourceSurrogateId)
                    SET @myRows = @@rowcount
                    EXECUTE dbo.LogEvent @Process=@ProcessName,@Status='Run',@Target='Tmp_QuantitySearchParam',@Action='Insert',@Rows=@myRows
                    SET @CopiedSearchParams += @myRows

                    INSERT INTO dbo.Tmp_TokenQuantityCompositeSearchParam 
                             ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2 )
                        SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2
                            FROM TokenQuantityCompositeSearchParam B
                            WHERE ResourceTypeId = @ResourceTypeId 
                                AND EXISTS (SELECT * FROM @SurrogateIds C WHERE C.ResourceSurrogateId = B.ResourceSurrogateId)
                                AND NOT EXISTS (SELECT * 
                                                    FROM Tmp_TokenQuantityCompositeSearchParam A 
                                                    WHERE A.ResourceTypeId = @ResourceTypeId 
                                                        AND A.ResourceSurrogateId = B.ResourceSurrogateId)
                    SET @myRows = @@rowcount
                    EXECUTE dbo.LogEvent @Process=@ProcessName,@Status='Run',@Target='Tmp_TokenQuantityCompositeSearchParam',@Action='Insert',@Rows=@myRows
                    SET @CopiedSearchParams += @myRows

                    INSERT INTO dbo.Tmp_TokenNumberNumberCompositeSearchParam 
                             ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange )
                        SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange
                            FROM TokenNumberNumberCompositeSearchParam B
                            WHERE ResourceTypeId = @ResourceTypeId 
                            AND EXISTS (SELECT * FROM @SurrogateIds C WHERE C.ResourceSurrogateId = B.ResourceSurrogateId)
                            AND NOT EXISTS (SELECT * 
                                                FROM Tmp_TokenNumberNumberCompositeSearchParam A 
                                                WHERE A.ResourceTypeId = @ResourceTypeId 
                                                AND A.ResourceSurrogateId = B.ResourceSurrogateId)

                    SET @myRows = @@rowcount
                    EXECUTE dbo.LogEvent @Process=@ProcessName,@Status='Run',@Target='Tmp_TokenNumberNumberCompositeSearchParam',@Action='Insert',@Rows=@myRows
                    SET @CopiedSearchParams += @myRows
                END

                UPDATE dbo.Parameters SET Char = @LastProcessed WHERE Id = @Id

                IF datediff(second, @ReportDate, getUTCdate()) > 60
                    BEGIN
                        EXECUTE dbo.LogEvent @Process=@ProcessName,@Status='Run',@Target='Resource',@Action='Select',@Rows=@ProcessedResources,@Text=@LastProcessed
                        EXECUTE dbo.LogEvent @Process=@ProcessName,@Status='Run',@Target='*SearchParam',@Action='Copy',@Rows=@CopiedSearchParams,@Text=@LastProcessed
                        SET @ReportDate = getUTCdate()
                        SET @ProcessedResources = 0
                        SET @CopiedSearchParams = 0
                    END
            END

            DELETE FROM @Types WHERE ResourceTypeId = @ResourceTypeId

            SET @SurrogateId = 0
        END

        EXECUTE dbo.LogEvent @Process=@ProcessName,@Status='Run',@Target='Resource',@Action='Select',@Rows=@ProcessedResources,@Text=@LastProcessed
        EXECUTE dbo.LogEvent @Process=@ProcessName,@Status='Run',@Target='*SearchParam',@Action='Copy',@Rows=@CopiedSearchParams,@Text=@LastProcessed

        EXECUTE dbo.LogEvent @Process=@ProcessName,@Status='End',@Start=@cst
    END TRY
    BEGIN CATCH
        IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
        EXECUTE dbo.LogEvent @Process=@ProcessName,@Status='Error';
        THROW
    END CATCH

    DECLARE @SP varchar(100) = 'DeleteSearchParamData'
            ,@st datetime = getUTCdate()
            ,@SId varchar(100) = 'PrecisionUpdate.Sync.LastProcessed.TypeId'
            ,@DeletedSearchParams int = 0
    BEGIN TRY
        SET @SP = 'DeleteSearchParamData'
        SET @st = getUTCdate()
        SET @ProcessedResources  = 0
        SET @ReportDate = getUTCdate()

        INSERT INTO dbo.Parameters (Id, Char) SELECT  @SP, 'LogEvent'

        EXECUTE dbo.LogEvent @Process= @SP,@Status='Start'

        INSERT INTO dbo.Parameters (Id, Char) SELECT @SId, '0' WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = @SId)

        SET @LastProcessed  = (SELECT Char FROM dbo.Parameters WHERE Id = @SId)

        INSERT INTO @Types EXECUTE dbo.GetUsedResourceTypes
        EXECUTE dbo.LogEvent @Process= @SP,@Status='Run',@Target='@Types',@Action='Insert',@Rows=@@rowcount

        SET @ResourceTypeId = @LastProcessed

        DELETE FROM @Types WHERE ResourceTypeId < @ResourceTypeId
        EXECUTE dbo.LogEvent @Process= @SP,@Status='Run',@Target='@Types',@Action='Delete',@Rows=@@rowcount

        WHILE EXISTS (SELECT * FROM @Types) -- Processing in ASC order
        BEGIN
            SET @ResourceTypeId = (SELECT TOP 1 ResourceTypeId FROM @Types ORDER BY ResourceTypeId)
            DELETE Tmp_NumberSearchParam FROM Tmp_NumberSearchParam A
                WHERE ResourceTypeId = @ResourceTypeId 
                        AND NOT EXISTS (SELECT * FROM NumberSearchParam B WHERE B.ResourceTypeId = @ResourceTypeId  
                                        AND B.ResourceSurrogateId = A.ResourceSurrogateId)
            SET @DeletedSearchParams += @@rowcount

            DELETE Tmp_QuantitySearchParam FROM Tmp_QuantitySearchParam A
                    WHERE ResourceTypeId = @ResourceTypeId 
                        AND NOT EXISTS (SELECT * FROM QuantitySearchParam B WHERE B.ResourceTypeId = @ResourceTypeId  
                                    AND B.ResourceSurrogateId = A.ResourceSurrogateId)
            SET @DeletedSearchParams += @@rowcount
            DELETE Tmp_TokenQuantityCompositeSearchParam FROM Tmp_TokenQuantityCompositeSearchParam A
                WHERE ResourceTypeId = @ResourceTypeId 
                        AND NOT EXISTS (SELECT * FROM TokenQuantityCompositeSearchParam B WHERE B.ResourceTypeId = @ResourceTypeId  
                                        AND B.ResourceSurrogateId = A.ResourceSurrogateId)
            SET @DeletedSearchParams += @@rowcount

            DELETE Tmp_TokenNumberNumberCompositeSearchParam FROM Tmp_TokenNumberNumberCompositeSearchParam A
                    WHERE ResourceTypeId = @ResourceTypeId 
                        AND NOT EXISTS (SELECT * FROM TokenNumberNumberCompositeSearchParam B WHERE B.ResourceTypeId = @ResourceTypeId  
                                    AND B.ResourceSurrogateId = A.ResourceSurrogateId)
            SET @DeletedSearchParams += @@rowcount
 
            SET @LastProcessed = convert(varchar,@ResourceTypeId)

            UPDATE dbo.Parameters SET Char = @LastProcessed WHERE Id = @SId

            IF datediff(second, @ReportDate, getUTCdate()) > 60
                BEGIN
                    EXECUTE dbo.LogEvent @Process= @SP,@Status='Run',@Target='*SearchParam',@Action='Delete',@Rows=@DeletedSearchParams,@Text=@LastProcessed
                    SET @ReportDate = getUTCdate()
                    SET @DeletedSearchParams = 0
                END
            DELETE FROM @Types WHERE ResourceTypeId = @ResourceTypeId

        END
 
        EXECUTE dbo.LogEvent @Process=@SP,@Status='Run',@Target='*SearchParam',@Action='Delete',@Rows=@DeletedSearchParams,@Text=@LastProcessed

        EXECUTE dbo.LogEvent @Process=@SP,@Status='End',@Start=@st
    END TRY
    BEGIN CATCH
        IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
        EXECUTE dbo.LogEvent @Process=@SP,@Status='Error';
        THROW
    END CATCH

    EXECUTE dbo.LogEvent @Process=@SP,@Status='End'
    INSERT INTO dbo.Parameters (Id, Char) SELECT 'Verification', 'LogEvent'
    EXECUTE dbo.LogEvent @Process='Verification',@Status='Start'
    DECLARE @origNumberSearchParamRows bigint = 0
            ,@tmpNumberSearchParamRows bigint = 0
            ,@origQuantitySearchParamRows bigint = 0
            ,@tmpQuantitySearchParamRows bigint = 0
            ,@origTokenNumberNumberCompositeSearchParamRows bigint = 0
            ,@tmpTokenNumberNumberCompositeSearchParamRows bigint = 0
            ,@origTokenQuantityCompositeSearchParamRows bigint = 0
            ,@tmpTokenQuantityCompositeSearchParamRows bigint = 0

        SELECT @origNumberSearchParamRows = sum(row_count)
            FROM sys.dm_db_partition_stats
            WHERE object_Id = object_id('NumberSearchParam')
            AND index_id IN (0,1)

        SELECT @origQuantitySearchParamRows = sum(row_count)
            FROM sys.dm_db_partition_stats
            WHERE object_Id = object_id('QuantitySearchParam')
            AND index_id IN (0,1)

        SELECT @origTokenNumberNumberCompositeSearchParamRows = sum(row_count)
            FROM sys.dm_db_partition_stats
            WHERE object_Id = object_id('TokenNumberNumberCompositeSearchParam')
            AND index_id IN (0,1)

        SELECT @origTokenQuantityCompositeSearchParamRows = sum(row_count)
            FROM sys.dm_db_partition_stats
            WHERE object_Id = object_id('TokenQuantityCompositeSearchParam')
            AND index_id IN (0,1)

        SELECT @tmpNumberSearchParamRows = sum(row_count)
            FROM sys.dm_db_partition_stats
            WHERE object_Id = object_id('Tmp_NumberSearchParam')
            AND index_id IN (0,1)

        SELECT @tmpQuantitySearchParamRows = sum(row_count)
            FROM sys.dm_db_partition_stats
            WHERE object_Id = object_id('Tmp_QuantitySearchParam')
            AND index_id IN (0,1)

        SELECT @tmpTokenNumberNumberCompositeSearchParamRows = sum(row_count)
            FROM sys.dm_db_partition_stats
            WHERE object_Id = object_id('Tmp_TokenNumberNumberCompositeSearchParam')
            AND index_id IN (0,1)

        SELECT @tmpTokenQuantityCompositeSearchParamRows = sum(row_count)
            FROM sys.dm_db_partition_stats
            WHERE object_Id = object_id('Tmp_TokenQuantityCompositeSearchParam')
            AND index_id IN (0,1)

    DECLARE @message varchar(100) = '';
    IF (@origNumberSearchParamRows != @tmpNumberSearchParamRows)
        BEGIN
            set @message = CONCAT(@origNumberSearchParamRows, @tmpNumberSearchParamRows,'NumberSearchParam rows does not match')
            EXECUTE dbo.LogEvent @Process='Verification',@Status=@message;
            THROW 5000, @message, 25;
        END
    IF (@origQuantitySearchParamRows != @tmpQuantitySearchParamRows)
            BEGIN
                set @message = CONCAT(@origQuantitySearchParamRows, @tmpQuantitySearchParamRows,'QuantitySearchParam rows does not match')
                EXECUTE dbo.LogEvent @Process='Verification',@Status=@message;
                THROW 5000, @message, 25;
            END
    IF (@origTokenNumberNumberCompositeSearchParamRows != @tmpTokenNumberNumberCompositeSearchParamRows)
            BEGIN
                set @message = CONCAT(@origTokenNumberNumberCompositeSearchParamRows, @tmpTokenNumberNumberCompositeSearchParamRows,'TokenNumberNumberCompositeSearchParam rows does not match')
                EXECUTE dbo.LogEvent @Process='Verification',@Status=@message;
                THROW 5000, @message, 25;
            END
    IF (@origTokenQuantityCompositeSearchParamRows != @tmpTokenQuantityCompositeSearchParamRows)
            BEGIN
                set @message = CONCAT(@origTokenQuantityCompositeSearchParamRows, @tmpTokenQuantityCompositeSearchParamRows,'TokenQuantityCompositeSearchParam rows does not match')
                EXECUTE dbo.LogEvent @Process='Verification',@Status=@message;
                THROW 5000, @message, 25;
            END
    
    EXECUTE dbo.LogEvent @Process='Verification',@Status='End'

    IF (@precision != 36)
    BEGIN TRY
        INSERT INTO dbo.Parameters (Id, Char) SELECT 'Alter Transaction', 'LogEvent'
        EXECUTE dbo.LogEvent @Process='Alter Transaction',@Status='Start'
        BEGIN TRANSACTION
            DROP TYPE dbo.NumberSearchParamList
            DROP TYPE dbo.QuantitySearchParamList
            DROP TYPE dbo.TokenNumberNumberCompositeSearchParamList
            DROP TYPE dbo.TokenQuantityCompositeSearchParamList

            CREATE TYPE dbo.NumberSearchParamList AS TABLE (
                ResourceTypeId      SMALLINT        NOT NULL,
                ResourceSurrogateId BIGINT          NOT NULL,
                SearchParamId       SMALLINT        NOT NULL,
                SingleValue         DECIMAL (36, 18) NULL,
                LowValue            DECIMAL (36, 18) NULL,
                HighValue           DECIMAL (36, 18) NULL UNIQUE (ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue));

            CREATE TYPE dbo.QuantitySearchParamList AS TABLE (
                ResourceTypeId      SMALLINT        NOT NULL,
                ResourceSurrogateId BIGINT          NOT NULL,
                SearchParamId       SMALLINT        NOT NULL,
                SystemId            INT             NULL,
                QuantityCodeId      INT             NULL,
                SingleValue         DECIMAL (36, 18) NULL,
                LowValue            DECIMAL (36, 18) NULL,
                HighValue           DECIMAL (36, 18) NULL UNIQUE (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue));


            CREATE TYPE dbo.TokenNumberNumberCompositeSearchParamList AS TABLE (
                ResourceTypeId      SMALLINT        NOT NULL,
                ResourceSurrogateId BIGINT          NOT NULL,
                SearchParamId       SMALLINT        NOT NULL,
                SystemId1           INT             NULL,
                Code1               VARCHAR (256)   COLLATE Latin1_General_100_CS_AS NOT NULL,
                CodeOverflow1       VARCHAR (MAX)   COLLATE Latin1_General_100_CS_AS NULL,
                SingleValue2        DECIMAL (36, 18) NULL,
                LowValue2           DECIMAL (36, 18) NULL,
                HighValue2          DECIMAL (36, 18) NULL,
                SingleValue3        DECIMAL (36, 18) NULL,
                LowValue3           DECIMAL (36, 18) NULL,
                HighValue3          DECIMAL (36, 18) NULL,
                HasRange            BIT             NOT NULL);


            CREATE TYPE dbo.TokenQuantityCompositeSearchParamList AS TABLE (
                ResourceTypeId      SMALLINT        NOT NULL,
                ResourceSurrogateId BIGINT          NOT NULL,
                SearchParamId       SMALLINT        NOT NULL,
                SystemId1           INT             NULL,
                Code1               VARCHAR (256)   COLLATE Latin1_General_100_CS_AS NOT NULL,
                CodeOverflow1       VARCHAR (MAX)   COLLATE Latin1_General_100_CS_AS NULL,
                SystemId2           INT             NULL,
                QuantityCodeId2     INT             NULL,
                SingleValue2        DECIMAL (36, 18) NULL,
                LowValue2           DECIMAL (36, 18) NULL,
                HighValue2          DECIMAL (36, 18) NULL);

                EXECUTE(
'ALTER PROCEDURE dbo.MergeResources
-- This stored procedure can be used for:
-- 1. Ordinary put with single version per resource in input
-- 2. Put with history preservation (multiple input versions per resource)
-- 3. Copy from one gen2 store to another with ResourceSurrogateId preserved.
    @AffectedRows int = 0 OUT
    ,@RaiseExceptionOnConflict bit = 1
    ,@IsResourceChangeCaptureEnabled bit = 0
    ,@Resources dbo.ResourceList READONLY
    ,@ResourceWriteClaims dbo.ResourceWriteClaimList READONLY
    ,@CompartmentAssignments dbo.CompartmentAssignmentList READONLY
    ,@ReferenceSearchParams dbo.ReferenceSearchParamList READONLY
    ,@TokenSearchParams dbo.TokenSearchParamList READONLY
    ,@TokenTexts dbo.TokenTextList READONLY
    ,@StringSearchParams dbo.StringSearchParamList READONLY
    ,@UriSearchParams dbo.UriSearchParamList READONLY
    ,@NumberSearchParams dbo.NumberSearchParamList READONLY
    ,@QuantitySearchParams dbo.QuantitySearchParamList READONLY
    ,@DateTimeSearchParms dbo.DateTimeSearchParamList READONLY
    ,@ReferenceTokenCompositeSearchParams dbo.ReferenceTokenCompositeSearchParamList READONLY
    ,@TokenTokenCompositeSearchParams dbo.TokenTokenCompositeSearchParamList READONLY
    ,@TokenDateTimeCompositeSearchParams dbo.TokenDateTimeCompositeSearchParamList READONLY
    ,@TokenQuantityCompositeSearchParams dbo.TokenQuantityCompositeSearchParamList READONLY
    ,@TokenStringCompositeSearchParams dbo.TokenStringCompositeSearchParamList READONLY
    ,@TokenNumberNumberCompositeSearchParams dbo.TokenNumberNumberCompositeSearchParamList READONLY
AS
set nocount on
DECLARE @st datetime = getUTCdate()
        ,@SP varchar(100) = ''MergeResources''
        ,@DummyTop bigint = 9223372036854775807
        ,@InitialTranCount int = @@trancount

DECLARE @Mode varchar(200) = isnull((SELECT ''RT=[''+convert(varchar,min(ResourceTypeId))+'',''+convert(varchar,max(ResourceTypeId))+''] MinSur=''+convert(varchar,min(ResourceSurrogateId))+'' Rows=''+convert(varchar,count(*)) FROM @Resources),''Input=Empty'')
SET @Mode += '' ITC=''+convert(varchar,@InitialTranCount)+'' E=''+convert(varchar,@RaiseExceptionOnConflict)+'' CC=''+convert(varchar,@IsResourceChangeCaptureEnabled)

SET @AffectedRows = 0

BEGIN TRY
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

    IF @InitialTranCount = 0 BEGIN TRANSACTION
  
    INSERT INTO @ResourceInfos
        (
            ResourceTypeId
            ,SurrogateId
            ,Version
            ,KeepHistory
            ,PreviousVersion
            ,PreviousSurrogateId
        )
    SELECT A.ResourceTypeId
            ,A.ResourceSurrogateId
            ,A.Version
            ,A.KeepHistory
            ,B.Version
            ,B.ResourceSurrogateId
        FROM (SELECT TOP (@DummyTop) * FROM @Resources WHERE HasVersionToCompare = 1) A
            LEFT OUTER JOIN dbo.Resource B -- WITH (UPDLOCK, HOLDLOCK) These locking hints cause deadlocks and are not needed. Racing might lead to tries to insert dups in unique index (with version key), but it will fail anyway, and in no case this will cause incorrect data saved.
                ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId AND B.IsHistory = 0
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))

    IF @RaiseExceptionOnConflict = 1 AND EXISTS (SELECT * FROM @ResourceInfos WHERE PreviousVersion IS NOT NULL AND Version <> PreviousVersion + 1)
    THROW 50409, ''Resource has been recently updated or added, please compare the resource content in code for any duplicate updates'', 1

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
    
    DELETE FROM dbo.Resource WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId AND KeepHistory = 0)
    SET @AffectedRows += @@rowcount

    DELETE FROM dbo.ResourceWriteClaim WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE SurrogateId = ResourceSurrogateId)
    SET @AffectedRows += @@rowcount
    DELETE FROM dbo.CompartmentAssignment WHERE EXISTS (SELECT * FROM @PreviousSurrogateIds WHERE TypeId = ResourceTypeId AND SurrogateId = ResourceSurrogateId)
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

    --EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status=''Info'',@Start=@st,@Rows=@AffectedRows,@Text=''Old rows''
    END

    INSERT INTO dbo.Resource 
            ( ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash )
    SELECT ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash
        FROM @Resources
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.ResourceWriteClaim 
            ( ResourceSurrogateId, ClaimTypeId, ClaimValue )
    SELECT ResourceSurrogateId, ClaimTypeId, ClaimValue
        FROM @ResourceWriteClaims
    SET @AffectedRows += @@rowcount

    INSERT INTO dbo.CompartmentAssignment 
            ( ResourceTypeId, ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId )
    SELECT ResourceTypeId, ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId
        FROM @CompartmentAssignments
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

    IF @IsResourceChangeCaptureEnabled = 1 --If the resource change capture feature is enabled, to execute a stored procedure called CaptureResourceChanges to insert resource change data.
    EXECUTE dbo.CaptureResourceIdsForChanges @Resources

    IF @InitialTranCount = 0 COMMIT TRANSACTION

    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status=''End'',@Start=@st,@Rows=@AffectedRows
END TRY
BEGIN CATCH
    IF @InitialTranCount = 0 AND @@trancount > 0 ROLLBACK TRANSACTION
    IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.

    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status=''Error'',@Start=@st;

    IF @RaiseExceptionOnConflict = 1 AND error_number() = 2601 AND error_message() LIKE ''%''''dbo.Resource''''%version%''
    THROW 50409, ''Resource has been recently updated or added, please compare the resource content in code for any duplicate updates'', 1;
    ELSE
    THROW
END CATCH'
            )
            INSERT INTO dbo.Parameters (Id, Char) SELECT 'Drop Temp Types', 'LogEvent'
            EXECUTE dbo.LogEvent @Process='Drop Temp Types',@Status='Start'

            DROP TYPE dbo.Tmp_NumberSearchParamList
            DROP TYPE dbo.Tmp_QuantitySearchParamList
            DROP TYPE dbo.Tmp_TokenNumberNumberCompositeSearchParamList
            DROP TYPE dbo.Tmp_TokenQuantityCompositeSearchParamList

            EXECUTE dbo.LogEvent @Process='Drop Temp Types',@Status='End'

            INSERT INTO dbo.Parameters (Id, Char) SELECT 'Drop Orig Tables', 'LogEvent'
            EXECUTE dbo.LogEvent @Process='Drop Orig Tables',@Status='Start'
            DROP TABLE dbo.NumberSearchParam;
            DROP TABLE dbo.QuantitySearchParam;
            DROP TABLE dbo.TokenNumberNumberCompositeSearchParam;
            DROP TABLE dbo.TokenQuantityCompositeSearchParam;

            INSERT INTO dbo.Parameters (Id, Char) SELECT 'Rename Temp Tables', 'LogEvent'
            EXECUTE dbo.LogEvent @Process='Rename Temp Tables',@Status='Start'
            EXEC sp_rename 'Tmp_NumberSearchParam', 'NumberSearchParam';
            EXEC sp_rename 'Tmp_QuantitySearchParam', 'QuantitySearchParam';
            EXEC sp_rename 'Tmp_TokenNumberNumberCompositeSearchParam', 'TokenNumberNumberCompositeSearchParam';
            EXEC sp_rename 'Tmp_TokenQuantityCompositeSearchParam', 'TokenQuantityCompositeSearchParam';
            EXEC sp_rename 'DF_Tmp_NumberSearchParam_IsHistory', 'DF_NumberSearchParam_IsHistory';
            EXEC sp_rename 'DF_Tmp_QuantitySearchParam_IsHistory', 'DF_QuantitySearchParam_IsHistory';
            EXEC sp_rename 'DF_Tmp_TokenNumberNumberCompositeSearchParam_IsHistory', 'DF_TokenNumberNumberCompositeSearchParam_IsHistory';
            EXEC sp_rename 'CHK_Tmp_TokenNumberNumberCompositeSearchParam_CodeOverflow1', 'CHK_TokenNumberNumberCompositeSearchParam_CodeOverflow1';
            EXEC sp_rename 'DF_Tmp_TokenQuantityCompositeSearchParam_IsHistory', 'DF_TokenQuantityCompositeSearchParam_IsHistory';
            EXEC sp_rename 'CHK_Tmp_TokenQuantityCompositeSearchParam_CodeOverflow1', 'CHK_TokenQuantityCompositeSearchParam_CodeOverflow1';
        COMMIT TRANSACTION
    END TRY
    BEGIN CATCH
        ROLLBACK TRANSACTION
        EXECUTE dbo.LogEvent @Process=@SP,@Status="Error",@Start=@st;
        THROW 
    END CATCH
END







