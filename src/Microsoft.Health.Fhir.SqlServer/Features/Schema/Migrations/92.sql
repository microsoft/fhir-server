
/*************************************************************************************************
    Auto-Generated from Sql build task. Do not manually edit it. 
**************************************************************************************************/
SET XACT_ABORT ON
BEGIN TRAN
IF EXISTS (SELECT *
           FROM   sys.tables
           WHERE  name = 'ClaimType')
    BEGIN
        ROLLBACK;
        RETURN;
    END

CREATE PARTITION FUNCTION PartitionFunction_ResourceTypeId(SMALLINT)
    AS RANGE RIGHT
    FOR VALUES (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95, 96, 97, 98, 99, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117, 118, 119, 120, 121, 122, 123, 124, 125, 126, 127, 128, 129, 130, 131, 132, 133, 134, 135, 136, 137, 138, 139, 140, 141, 142, 143, 144, 145, 146, 147, 148, 149, 150);

CREATE PARTITION SCHEME PartitionScheme_ResourceTypeId
    AS PARTITION PartitionFunction_ResourceTypeId
    ALL TO ([PRIMARY]);


GO
CREATE PARTITION FUNCTION PartitionFunction_ResourceChangeData_Timestamp(DATETIME2 (7))
    AS RANGE RIGHT
    FOR VALUES (N'1970-01-01T00:00:00.0000000');

CREATE PARTITION SCHEME PartitionScheme_ResourceChangeData_Timestamp
    AS PARTITION PartitionFunction_ResourceChangeData_Timestamp
    ALL TO ([PRIMARY]);

DECLARE @numberOfHistoryPartitions AS INT = 48;

DECLARE @numberOfFuturePartitions AS INT = 720;

DECLARE @rightPartitionBoundary AS DATETIME2 (7);

DECLARE @currentDateTime AS DATETIME2 (7) = sysutcdatetime();

WHILE @numberOfHistoryPartitions >= -@numberOfFuturePartitions
    BEGIN
        SET @rightPartitionBoundary = DATEADD(hour, DATEDIFF(hour, 0, @currentDateTime) - @numberOfHistoryPartitions, 0);
        ALTER PARTITION SCHEME PartitionScheme_ResourceChangeData_Timestamp NEXT USED [Primary];
        ALTER PARTITION FUNCTION PartitionFunction_ResourceChangeData_Timestamp( )
            SPLIT RANGE (@rightPartitionBoundary);
        SET @numberOfHistoryPartitions -= 1;
    END

CREATE SEQUENCE dbo.ResourceSurrogateIdUniquifierSequence
    AS INT
    START WITH 0
    INCREMENT BY 1
    MINVALUE 0
    MAXVALUE 79999
    CYCLE
    CACHE 1000000;

CREATE TYPE dbo.BigintList AS TABLE (
    Id BIGINT NOT NULL PRIMARY KEY);

CREATE TYPE dbo.DateTimeSearchParamList AS TABLE (
    ResourceTypeId      SMALLINT           NOT NULL,
    ResourceSurrogateId BIGINT             NOT NULL,
    SearchParamId       SMALLINT           NOT NULL,
    StartDateTime       DATETIMEOFFSET (7) NOT NULL,
    EndDateTime         DATETIMEOFFSET (7) NOT NULL,
    IsLongerThanADay    BIT                NOT NULL,
    IsMin               BIT                NOT NULL,
    IsMax               BIT                NOT NULL UNIQUE (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax));

CREATE TYPE dbo.NumberSearchParamList AS TABLE (
    ResourceTypeId      SMALLINT         NOT NULL,
    ResourceSurrogateId BIGINT           NOT NULL,
    SearchParamId       SMALLINT         NOT NULL,
    SingleValue         DECIMAL (36, 18) NULL,
    LowValue            DECIMAL (36, 18) NULL,
    HighValue           DECIMAL (36, 18) NULL UNIQUE (ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue));

CREATE TYPE dbo.QuantitySearchParamList AS TABLE (
    ResourceTypeId      SMALLINT         NOT NULL,
    ResourceSurrogateId BIGINT           NOT NULL,
    SearchParamId       SMALLINT         NOT NULL,
    SystemId            INT              NULL,
    QuantityCodeId      INT              NULL,
    SingleValue         DECIMAL (36, 18) NULL,
    LowValue            DECIMAL (36, 18) NULL,
    HighValue           DECIMAL (36, 18) NULL UNIQUE (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue));

CREATE TYPE dbo.ReferenceSearchParamList AS TABLE (
    ResourceTypeId           SMALLINT      NOT NULL,
    ResourceSurrogateId      BIGINT        NOT NULL,
    SearchParamId            SMALLINT      NOT NULL,
    BaseUri                  VARCHAR (128) COLLATE Latin1_General_100_CS_AS NULL,
    ReferenceResourceTypeId  SMALLINT      NULL,
    ReferenceResourceId      VARCHAR (64)  COLLATE Latin1_General_100_CS_AS NOT NULL,
    ReferenceResourceVersion INT           NULL UNIQUE (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId));

CREATE TYPE dbo.ReferenceTokenCompositeSearchParamList AS TABLE (
    ResourceTypeId            SMALLINT      NOT NULL,
    ResourceSurrogateId       BIGINT        NOT NULL,
    SearchParamId             SMALLINT      NOT NULL,
    BaseUri1                  VARCHAR (128) COLLATE Latin1_General_100_CS_AS NULL,
    ReferenceResourceTypeId1  SMALLINT      NULL,
    ReferenceResourceId1      VARCHAR (64)  COLLATE Latin1_General_100_CS_AS NOT NULL,
    ReferenceResourceVersion1 INT           NULL,
    SystemId2                 INT           NULL,
    Code2                     VARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow2             VARCHAR (MAX) COLLATE Latin1_General_100_CS_AS NULL);

CREATE TYPE dbo.ResourceDateKeyList AS TABLE (
    ResourceTypeId      SMALLINT     NOT NULL,
    ResourceId          VARCHAR (64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    ResourceSurrogateId BIGINT       NOT NULL PRIMARY KEY (ResourceTypeId, ResourceId, ResourceSurrogateId));

CREATE TYPE dbo.ResourceKeyList AS TABLE (
    ResourceTypeId SMALLINT     NOT NULL,
    ResourceId     VARCHAR (64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Version        INT          NULL UNIQUE (ResourceTypeId, ResourceId, Version));

CREATE TYPE dbo.ResourceList AS TABLE (
    ResourceTypeId       SMALLINT        NOT NULL,
    ResourceSurrogateId  BIGINT          NOT NULL,
    ResourceId           VARCHAR (64)    COLLATE Latin1_General_100_CS_AS NOT NULL,
    Version              INT             NOT NULL,
    HasVersionToCompare  BIT             NOT NULL,
    IsDeleted            BIT             NOT NULL,
    IsHistory            BIT             NOT NULL,
    KeepHistory          BIT             NOT NULL,
    RawResource          VARBINARY (MAX) NOT NULL,
    IsRawResourceMetaSet BIT             NOT NULL,
    RequestMethod        VARCHAR (10)    NULL,
    SearchParamHash      VARCHAR (64)    NULL PRIMARY KEY (ResourceTypeId, ResourceSurrogateId),
    UNIQUE (ResourceTypeId, ResourceId, Version));

CREATE TYPE dbo.ResourceWriteClaimList AS TABLE (
    ResourceSurrogateId BIGINT         NOT NULL,
    ClaimTypeId         TINYINT        NOT NULL,
    ClaimValue          NVARCHAR (128) NOT NULL);

CREATE TYPE dbo.StringList AS TABLE (
    String VARCHAR (MAX));

CREATE TYPE dbo.StringSearchParamList AS TABLE (
    ResourceTypeId      SMALLINT       NOT NULL,
    ResourceSurrogateId BIGINT         NOT NULL,
    SearchParamId       SMALLINT       NOT NULL,
    Text                NVARCHAR (256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL,
    TextOverflow        NVARCHAR (MAX) COLLATE Latin1_General_100_CI_AI_SC NULL,
    IsMin               BIT            NOT NULL,
    IsMax               BIT            NOT NULL);

CREATE TYPE dbo.TokenDateTimeCompositeSearchParamList AS TABLE (
    ResourceTypeId      SMALLINT           NOT NULL,
    ResourceSurrogateId BIGINT             NOT NULL,
    SearchParamId       SMALLINT           NOT NULL,
    SystemId1           INT                NULL,
    Code1               VARCHAR (256)      COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow1       VARCHAR (MAX)      COLLATE Latin1_General_100_CS_AS NULL,
    StartDateTime2      DATETIMEOFFSET (7) NOT NULL,
    EndDateTime2        DATETIMEOFFSET (7) NOT NULL,
    IsLongerThanADay2   BIT                NOT NULL);

CREATE TYPE dbo.TokenNumberNumberCompositeSearchParamList AS TABLE (
    ResourceTypeId      SMALLINT         NOT NULL,
    ResourceSurrogateId BIGINT           NOT NULL,
    SearchParamId       SMALLINT         NOT NULL,
    SystemId1           INT              NULL,
    Code1               VARCHAR (256)    COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow1       VARCHAR (MAX)    COLLATE Latin1_General_100_CS_AS NULL,
    SingleValue2        DECIMAL (36, 18) NULL,
    LowValue2           DECIMAL (36, 18) NULL,
    HighValue2          DECIMAL (36, 18) NULL,
    SingleValue3        DECIMAL (36, 18) NULL,
    LowValue3           DECIMAL (36, 18) NULL,
    HighValue3          DECIMAL (36, 18) NULL,
    HasRange            BIT              NOT NULL);

CREATE TYPE dbo.TokenQuantityCompositeSearchParamList AS TABLE (
    ResourceTypeId      SMALLINT         NOT NULL,
    ResourceSurrogateId BIGINT           NOT NULL,
    SearchParamId       SMALLINT         NOT NULL,
    SystemId1           INT              NULL,
    Code1               VARCHAR (256)    COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow1       VARCHAR (MAX)    COLLATE Latin1_General_100_CS_AS NULL,
    SystemId2           INT              NULL,
    QuantityCodeId2     INT              NULL,
    SingleValue2        DECIMAL (36, 18) NULL,
    LowValue2           DECIMAL (36, 18) NULL,
    HighValue2          DECIMAL (36, 18) NULL);

CREATE TYPE dbo.TokenSearchParamList AS TABLE (
    ResourceTypeId      SMALLINT      NOT NULL,
    ResourceSurrogateId BIGINT        NOT NULL,
    SearchParamId       SMALLINT      NOT NULL,
    SystemId            INT           NULL,
    Code                VARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow        VARCHAR (MAX) COLLATE Latin1_General_100_CS_AS NULL);

CREATE TYPE dbo.TokenStringCompositeSearchParamList AS TABLE (
    ResourceTypeId      SMALLINT       NOT NULL,
    ResourceSurrogateId BIGINT         NOT NULL,
    SearchParamId       SMALLINT       NOT NULL,
    SystemId1           INT            NULL,
    Code1               VARCHAR (256)  COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow1       VARCHAR (MAX)  COLLATE Latin1_General_100_CS_AS NULL,
    Text2               NVARCHAR (256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL,
    TextOverflow2       NVARCHAR (MAX) COLLATE Latin1_General_100_CI_AI_SC NULL);

CREATE TYPE dbo.TokenTextList AS TABLE (
    ResourceTypeId      SMALLINT       NOT NULL,
    ResourceSurrogateId BIGINT         NOT NULL,
    SearchParamId       SMALLINT       NOT NULL,
    Text                NVARCHAR (400) COLLATE Latin1_General_CI_AI NOT NULL);

CREATE TYPE dbo.TokenTokenCompositeSearchParamList AS TABLE (
    ResourceTypeId      SMALLINT      NOT NULL,
    ResourceSurrogateId BIGINT        NOT NULL,
    SearchParamId       SMALLINT      NOT NULL,
    SystemId1           INT           NULL,
    Code1               VARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow1       VARCHAR (MAX) COLLATE Latin1_General_100_CS_AS NULL,
    SystemId2           INT           NULL,
    Code2               VARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow2       VARCHAR (MAX) COLLATE Latin1_General_100_CS_AS NULL);

CREATE TYPE dbo.SearchParamTableType_2 AS TABLE (
    Uri                  VARCHAR (128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Status               VARCHAR (20)  NOT NULL,
    IsPartiallySupported BIT           NOT NULL);

CREATE TYPE dbo.BulkReindexResourceTableType_1 AS TABLE (
    Offset          INT          NOT NULL,
    ResourceTypeId  SMALLINT     NOT NULL,
    ResourceId      VARCHAR (64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    ETag            INT          NULL,
    SearchParamHash VARCHAR (64) NOT NULL);

CREATE TYPE dbo.UriSearchParamList AS TABLE (
    ResourceTypeId      SMALLINT      NOT NULL,
    ResourceSurrogateId BIGINT        NOT NULL,
    SearchParamId       SMALLINT      NOT NULL,
    Uri                 VARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY (ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri));

CREATE TABLE dbo.ClaimType (
    ClaimTypeId TINYINT       IDENTITY (1, 1) NOT NULL,
    Name        VARCHAR (128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CONSTRAINT UQ_ClaimType_ClaimTypeId UNIQUE (ClaimTypeId),
    CONSTRAINT PKC_ClaimType PRIMARY KEY CLUSTERED (Name) WITH (DATA_COMPRESSION = PAGE)
);

CREATE TABLE dbo.CompartmentAssignment (
    ResourceTypeId      SMALLINT     NOT NULL,
    ResourceSurrogateId BIGINT       NOT NULL,
    CompartmentTypeId   TINYINT      NOT NULL,
    ReferenceResourceId VARCHAR (64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    IsHistory           BIT          NOT NULL,
    CONSTRAINT PKC_CompartmentAssignment PRIMARY KEY CLUSTERED (ResourceTypeId, ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
);


GO
ALTER TABLE dbo.CompartmentAssignment
    ADD CONSTRAINT DF_CompartmentAssignment_IsHistory DEFAULT 0 FOR IsHistory;


GO
ALTER TABLE dbo.CompartmentAssignment SET (LOCK_ESCALATION = AUTO);


GO
CREATE NONCLUSTERED INDEX IX_CompartmentAssignment_CompartmentTypeId_ReferenceResourceId
    ON dbo.CompartmentAssignment(ResourceTypeId, CompartmentTypeId, ReferenceResourceId, ResourceSurrogateId) WHERE IsHistory = 0 WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE TABLE dbo.CompartmentType (
    CompartmentTypeId TINYINT       IDENTITY (1, 1) NOT NULL,
    Name              VARCHAR (128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CONSTRAINT UQ_CompartmentType_CompartmentTypeId UNIQUE (CompartmentTypeId),
    CONSTRAINT PKC_CompartmentType PRIMARY KEY CLUSTERED (Name) WITH (DATA_COMPRESSION = PAGE)
);

CREATE TABLE dbo.DateTimeSearchParam (
    ResourceTypeId      SMALLINT      NOT NULL,
    ResourceSurrogateId BIGINT        NOT NULL,
    SearchParamId       SMALLINT      NOT NULL,
    StartDateTime       DATETIME2 (7) NOT NULL,
    EndDateTime         DATETIME2 (7) NOT NULL,
    IsLongerThanADay    BIT           NOT NULL,
    IsMin               BIT           CONSTRAINT date_IsMin_Constraint DEFAULT 0 NOT NULL,
    IsMax               BIT           CONSTRAINT date_IsMax_Constraint DEFAULT 0 NOT NULL
);

ALTER TABLE dbo.DateTimeSearchParam SET (LOCK_ESCALATION = AUTO);

CREATE CLUSTERED INDEX IXC_DateTimeSearchParam
    ON dbo.DateTimeSearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE INDEX IX_SearchParamId_StartDateTime_EndDateTime_INCLUDE_IsLongerThanADay_IsMin_IsMax
    ON dbo.DateTimeSearchParam(SearchParamId, StartDateTime, EndDateTime)
    INCLUDE(IsLongerThanADay, IsMin, IsMax)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE INDEX IX_SearchParamId_EndDateTime_StartDateTime_INCLUDE_IsLongerThanADay_IsMin_IsMax
    ON dbo.DateTimeSearchParam(SearchParamId, EndDateTime, StartDateTime)
    INCLUDE(IsLongerThanADay, IsMin, IsMax)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE INDEX IX_SearchParamId_StartDateTime_EndDateTime_INCLUDE_IsMin_IsMax_WHERE_IsLongerThanADay_1
    ON dbo.DateTimeSearchParam(SearchParamId, StartDateTime, EndDateTime)
    INCLUDE(IsMin, IsMax) WHERE IsLongerThanADay = 1
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE INDEX IX_SearchParamId_EndDateTime_StartDateTime_INCLUDE_IsMin_IsMax_WHERE_IsLongerThanADay_1
    ON dbo.DateTimeSearchParam(SearchParamId, EndDateTime, StartDateTime)
    INCLUDE(IsMin, IsMax) WHERE IsLongerThanADay = 1
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

IF NOT EXISTS (SELECT 1
               FROM   sys.tables
               WHERE  name = 'EventAgentCheckpoint')
    BEGIN
        CREATE TABLE dbo.EventAgentCheckpoint (
            CheckpointId            VARCHAR (64)       NOT NULL,
            LastProcessedDateTime   DATETIMEOFFSET (7),
            LastProcessedIdentifier VARCHAR (64)      ,
            UpdatedOn               DATETIME2 (7)      DEFAULT sysutcdatetime() NOT NULL,
            CONSTRAINT PK_EventAgentCheckpoint PRIMARY KEY CLUSTERED (CheckpointId)
        ) ON [PRIMARY];
    END

CREATE PARTITION FUNCTION EventLogPartitionFunction(TINYINT)
    AS RANGE RIGHT
    FOR VALUES (0, 1, 2, 3, 4, 5, 6, 7);


GO
CREATE PARTITION SCHEME EventLogPartitionScheme
    AS PARTITION EventLogPartitionFunction
    ALL TO ([PRIMARY]);


GO
CREATE TABLE dbo.EventLog (
    PartitionId  AS              isnull(CONVERT (TINYINT, EventId % 8), 0) PERSISTED,
    EventId      BIGINT          IDENTITY (1, 1) NOT NULL,
    EventDate    DATETIME        NOT NULL,
    Process      VARCHAR (100)   NOT NULL,
    Status       VARCHAR (10)    NOT NULL,
    Mode         VARCHAR (200)   NULL,
    Action       VARCHAR (20)    NULL,
    Target       VARCHAR (100)   NULL,
    Rows         BIGINT          NULL,
    Milliseconds INT             NULL,
    EventText    NVARCHAR (3500) NULL,
    SPID         SMALLINT        NOT NULL,
    HostName     VARCHAR (64)    NOT NULL CONSTRAINT PKC_EventLog_EventDate_EventId_PartitionId PRIMARY KEY CLUSTERED (EventDate, EventId, PartitionId) ON EventLogPartitionScheme (PartitionId)
);

CREATE TABLE dbo.IndexProperties (
    TableName     VARCHAR (100) NOT NULL,
    IndexName     VARCHAR (200) NOT NULL,
    PropertyName  VARCHAR (100) NOT NULL,
    PropertyValue VARCHAR (100) NOT NULL,
    CreateDate    DATETIME      CONSTRAINT DF_IndexProperties_CreateDate DEFAULT getUTCdate() NOT NULL CONSTRAINT PKC_IndexProperties_TableName_IndexName_PropertyName PRIMARY KEY CLUSTERED (TableName, IndexName, PropertyName)
);

CREATE PARTITION FUNCTION TinyintPartitionFunction(TINYINT)
    AS RANGE RIGHT
    FOR VALUES (0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95, 96, 97, 98, 99, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117, 118, 119, 120, 121, 122, 123, 124, 125, 126, 127, 128, 129, 130, 131, 132, 133, 134, 135, 136, 137, 138, 139, 140, 141, 142, 143, 144, 145, 146, 147, 148, 149, 150, 151, 152, 153, 154, 155, 156, 157, 158, 159, 160, 161, 162, 163, 164, 165, 166, 167, 168, 169, 170, 171, 172, 173, 174, 175, 176, 177, 178, 179, 180, 181, 182, 183, 184, 185, 186, 187, 188, 189, 190, 191, 192, 193, 194, 195, 196, 197, 198, 199, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 214, 215, 216, 217, 218, 219, 220, 221, 222, 223, 224, 225, 226, 227, 228, 229, 230, 231, 232, 233, 234, 235, 236, 237, 238, 239, 240, 241, 242, 243, 244, 245, 246, 247, 248, 249, 250, 251, 252, 253, 254, 255);


GO
CREATE PARTITION SCHEME TinyintPartitionScheme
    AS PARTITION TinyintPartitionFunction
    ALL TO ([PRIMARY]);


GO
CREATE TABLE dbo.JobQueue (
    QueueType       TINYINT        NOT NULL,
    GroupId         BIGINT         NOT NULL,
    JobId           BIGINT         NOT NULL,
    PartitionId     AS             CONVERT (TINYINT, JobId % 16) PERSISTED,
    Definition      VARCHAR (MAX)  NOT NULL,
    DefinitionHash  VARBINARY (20) NOT NULL,
    Version         BIGINT         CONSTRAINT DF_JobQueue_Version DEFAULT datediff_big(millisecond, '0001-01-01', getUTCdate()) NOT NULL,
    Status          TINYINT        CONSTRAINT DF_JobQueue_Status DEFAULT 0 NOT NULL,
    Priority        TINYINT        CONSTRAINT DF_JobQueue_Priority DEFAULT 100 NOT NULL,
    Data            BIGINT         NULL,
    Result          VARCHAR (MAX)  NULL,
    CreateDate      DATETIME       CONSTRAINT DF_JobQueue_CreateDate DEFAULT getUTCdate() NOT NULL,
    StartDate       DATETIME       NULL,
    EndDate         DATETIME       NULL,
    HeartbeatDate   DATETIME       CONSTRAINT DF_JobQueue_HeartbeatDate DEFAULT getUTCdate() NOT NULL,
    Worker          VARCHAR (100)  NULL,
    Info            VARCHAR (1000) NULL,
    CancelRequested BIT            CONSTRAINT DF_JobQueue_CancelRequested DEFAULT 0 NOT NULL CONSTRAINT PKC_JobQueue_QueueType_PartitionId_JobId PRIMARY KEY CLUSTERED (QueueType, PartitionId, JobId) ON TinyintPartitionScheme (QueueType),
    CONSTRAINT U_JobQueue_QueueType_JobId UNIQUE (QueueType, JobId)
);


GO
CREATE INDEX IX_QueueType_PartitionId_Status_Priority
    ON dbo.JobQueue(PartitionId, Status, Priority)
    ON TinyintPartitionScheme (QueueType);


GO
CREATE INDEX IX_QueueType_GroupId
    ON dbo.JobQueue(QueueType, GroupId)
    ON TinyintPartitionScheme (QueueType);


GO
CREATE INDEX IX_QueueType_DefinitionHash
    ON dbo.JobQueue(QueueType, DefinitionHash)
    ON TinyintPartitionScheme (QueueType);

CREATE TABLE dbo.NumberSearchParam (
    ResourceTypeId      SMALLINT         NOT NULL,
    ResourceSurrogateId BIGINT           NOT NULL,
    SearchParamId       SMALLINT         NOT NULL,
    SingleValue         DECIMAL (36, 18) NULL,
    LowValue            DECIMAL (36, 18) NOT NULL,
    HighValue           DECIMAL (36, 18) NOT NULL
);

ALTER TABLE dbo.NumberSearchParam SET (LOCK_ESCALATION = AUTO);

CREATE CLUSTERED INDEX IXC_NumberSearchParam
    ON dbo.NumberSearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE INDEX IX_SearchParamId_SingleValue_WHERE_SingleValue_NOT_NULL
    ON dbo.NumberSearchParam(SearchParamId, SingleValue) WHERE SingleValue IS NOT NULL
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE INDEX IX_SearchParamId_LowValue_HighValue
    ON dbo.NumberSearchParam(SearchParamId, LowValue, HighValue)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE INDEX IX_SearchParamId_HighValue_LowValue
    ON dbo.NumberSearchParam(SearchParamId, HighValue, LowValue)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE TABLE dbo.Parameters (
    Id          VARCHAR (100)   NOT NULL,
    Date        DATETIME        NULL,
    Number      FLOAT           NULL,
    Bigint      BIGINT          NULL,
    Char        VARCHAR (4000)  NULL,
    Binary      VARBINARY (MAX) NULL,
    UpdatedDate DATETIME        NULL,
    UpdatedBy   NVARCHAR (255)  NULL CONSTRAINT PKC_Parameters_Id PRIMARY KEY CLUSTERED (Id) WITH (IGNORE_DUP_KEY = ON)
);


GO
CREATE TABLE dbo.ParametersHistory (
    ChangeId    INT             IDENTITY (1, 1) NOT NULL,
    Id          VARCHAR (100)   NOT NULL,
    Date        DATETIME        NULL,
    Number      FLOAT           NULL,
    Bigint      BIGINT          NULL,
    Char        VARCHAR (4000)  NULL,
    Binary      VARBINARY (MAX) NULL,
    UpdatedDate DATETIME        NULL,
    UpdatedBy   NVARCHAR (255)  NULL
);

CREATE TABLE dbo.QuantityCode (
    QuantityCodeId INT            IDENTITY (1, 1) NOT NULL,
    Value          NVARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CONSTRAINT UQ_QuantityCode_QuantityCodeId UNIQUE (QuantityCodeId),
    CONSTRAINT PKC_QuantityCode PRIMARY KEY CLUSTERED (Value) WITH (DATA_COMPRESSION = PAGE)
);

CREATE TABLE dbo.QuantitySearchParam (
    ResourceTypeId      SMALLINT         NOT NULL,
    ResourceSurrogateId BIGINT           NOT NULL,
    SearchParamId       SMALLINT         NOT NULL,
    SystemId            INT              NULL,
    QuantityCodeId      INT              NULL,
    SingleValue         DECIMAL (36, 18) NULL,
    LowValue            DECIMAL (36, 18) NOT NULL,
    HighValue           DECIMAL (36, 18) NOT NULL
);

ALTER TABLE dbo.QuantitySearchParam SET (LOCK_ESCALATION = AUTO);

CREATE CLUSTERED INDEX IXC_QuantitySearchParam
    ON dbo.QuantitySearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE INDEX IX_SearchParamId_QuantityCodeId_SingleValue_INCLUDE_SystemId_WHERE_SingleValue_NOT_NULL
    ON dbo.QuantitySearchParam(SearchParamId, QuantityCodeId, SingleValue)
    INCLUDE(SystemId) WHERE SingleValue IS NOT NULL
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE INDEX IX_SearchParamId_QuantityCodeId_LowValue_HighValue_INCLUDE_SystemId
    ON dbo.QuantitySearchParam(SearchParamId, QuantityCodeId, LowValue, HighValue)
    INCLUDE(SystemId)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE INDEX IX_SearchParamId_QuantityCodeId_HighValue_LowValue_INCLUDE_SystemId
    ON dbo.QuantitySearchParam(SearchParamId, QuantityCodeId, HighValue, LowValue)
    INCLUDE(SystemId)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE TABLE dbo.ReferenceSearchParam (
    ResourceTypeId           SMALLINT      NOT NULL,
    ResourceSurrogateId      BIGINT        NOT NULL,
    SearchParamId            SMALLINT      NOT NULL,
    BaseUri                  VARCHAR (128) COLLATE Latin1_General_100_CS_AS NULL,
    ReferenceResourceTypeId  SMALLINT      NULL,
    ReferenceResourceId      VARCHAR (64)  COLLATE Latin1_General_100_CS_AS NOT NULL,
    ReferenceResourceVersion INT           NULL
);

ALTER TABLE dbo.ReferenceSearchParam SET (LOCK_ESCALATION = AUTO);

CREATE CLUSTERED INDEX IXC_ReferenceSearchParam
    ON dbo.ReferenceSearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE UNIQUE INDEX IXU_ReferenceResourceId_ReferenceResourceTypeId_SearchParamId_BaseUri_ResourceSurrogateId_ResourceTypeId
    ON dbo.ReferenceSearchParam(ReferenceResourceId, ReferenceResourceTypeId, SearchParamId, BaseUri, ResourceSurrogateId, ResourceTypeId) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE TABLE dbo.ReferenceTokenCompositeSearchParam (
    ResourceTypeId            SMALLINT      NOT NULL,
    ResourceSurrogateId       BIGINT        NOT NULL,
    SearchParamId             SMALLINT      NOT NULL,
    BaseUri1                  VARCHAR (128) COLLATE Latin1_General_100_CS_AS NULL,
    ReferenceResourceTypeId1  SMALLINT      NULL,
    ReferenceResourceId1      VARCHAR (64)  COLLATE Latin1_General_100_CS_AS NOT NULL,
    ReferenceResourceVersion1 INT           NULL,
    SystemId2                 INT           NULL,
    Code2                     VARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow2             VARCHAR (MAX) COLLATE Latin1_General_100_CS_AS NULL
);

ALTER TABLE dbo.ReferenceTokenCompositeSearchParam SET (LOCK_ESCALATION = AUTO);

CREATE CLUSTERED INDEX IXC_ReferenceTokenCompositeSearchParam
    ON dbo.ReferenceTokenCompositeSearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE INDEX IX_SearchParamId_ReferenceResourceId1_Code2_INCLUDE_ReferenceResourceTypeId1_BaseUri1_SystemId2
    ON dbo.ReferenceTokenCompositeSearchParam(SearchParamId, ReferenceResourceId1, Code2)
    INCLUDE(ReferenceResourceTypeId1, BaseUri1, SystemId2) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE TABLE dbo.ReindexJob (
    Id                VARCHAR (64)  COLLATE Latin1_General_100_CS_AS NOT NULL,
    Status            VARCHAR (10)  NOT NULL,
    HeartbeatDateTime DATETIME2 (7) NULL,
    RawJobRecord      VARCHAR (MAX) NOT NULL,
    JobVersion        ROWVERSION    NOT NULL,
    CONSTRAINT PKC_ReindexJob PRIMARY KEY CLUSTERED (Id)
);

CREATE TABLE dbo.CurrentResource (
    ResourceTypeId       SMALLINT        NOT NULL,
    ResourceId           VARCHAR (64)    COLLATE Latin1_General_100_CS_AS NOT NULL,
    Version              INT             NOT NULL,
    IsHistory            BIT             NOT NULL,
    ResourceSurrogateId  BIGINT          NOT NULL,
    IsDeleted            BIT             NOT NULL,
    RequestMethod        VARCHAR (10)    NULL,
    RawResource          VARBINARY (MAX) NOT NULL,
    IsRawResourceMetaSet BIT             NOT NULL,
    SearchParamHash      VARCHAR (64)    NULL,
    TransactionId        BIGINT          NULL,
    HistoryTransactionId BIGINT          NULL
);


GO
DROP TABLE dbo.CurrentResource;


GO
CREATE TABLE dbo.Resource (
    ResourceTypeId       SMALLINT        NOT NULL,
    ResourceId           VARCHAR (64)    COLLATE Latin1_General_100_CS_AS NOT NULL,
    Version              INT             NOT NULL,
    IsHistory            BIT             NOT NULL,
    ResourceSurrogateId  BIGINT          NOT NULL,
    IsDeleted            BIT             NOT NULL,
    RequestMethod        VARCHAR (10)    NULL,
    RawResource          VARBINARY (MAX) NOT NULL,
    IsRawResourceMetaSet BIT             DEFAULT 0 NOT NULL,
    SearchParamHash      VARCHAR (64)    NULL,
    TransactionId        BIGINT          NULL,
    HistoryTransactionId BIGINT          NULL CONSTRAINT PKC_Resource PRIMARY KEY CLUSTERED (ResourceTypeId, ResourceSurrogateId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId),
    CONSTRAINT CH_Resource_RawResource_Length CHECK (RawResource > 0x0)
);

ALTER TABLE dbo.Resource SET (LOCK_ESCALATION = AUTO);

CREATE INDEX IX_ResourceTypeId_TransactionId
    ON dbo.Resource(ResourceTypeId, TransactionId) WHERE TransactionId IS NOT NULL
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE INDEX IX_ResourceTypeId_HistoryTransactionId
    ON dbo.Resource(ResourceTypeId, HistoryTransactionId) WHERE HistoryTransactionId IS NOT NULL
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_ResourceTypeId_ResourceId_Version
    ON dbo.Resource(ResourceTypeId, ResourceId, Version)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_ResourceTypeId_ResourceId
    ON dbo.Resource(ResourceTypeId, ResourceId)
    INCLUDE(Version, IsDeleted) WHERE IsHistory = 0
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_ResourceTypeId_ResourceSurrgateId
    ON dbo.Resource(ResourceTypeId, ResourceSurrogateId) WHERE IsHistory = 0
                                                               AND IsDeleted = 0
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE TABLE dbo.ResourceChangeData (
    Id                   BIGINT        IDENTITY (1, 1) NOT NULL,
    Timestamp            DATETIME2 (7) CONSTRAINT DF_ResourceChangeData_Timestamp DEFAULT sysutcdatetime() NOT NULL,
    ResourceId           VARCHAR (64)  NOT NULL,
    ResourceTypeId       SMALLINT      NOT NULL,
    ResourceVersion      INT           NOT NULL,
    ResourceChangeTypeId TINYINT       NOT NULL
) ON PartitionScheme_ResourceChangeData_Timestamp (Timestamp);

CREATE CLUSTERED INDEX IXC_ResourceChangeData
    ON dbo.ResourceChangeData(Id ASC) WITH (ONLINE = ON)
    ON PartitionScheme_ResourceChangeData_Timestamp (Timestamp);

CREATE TABLE dbo.ResourceChangeDataStaging (
    Id                   BIGINT        IDENTITY (1, 1) NOT NULL,
    Timestamp            DATETIME2 (7) CONSTRAINT DF_ResourceChangeDataStaging_Timestamp DEFAULT sysutcdatetime() NOT NULL,
    ResourceId           VARCHAR (64)  NOT NULL,
    ResourceTypeId       SMALLINT      NOT NULL,
    ResourceVersion      INT           NOT NULL,
    ResourceChangeTypeId TINYINT       NOT NULL
) ON [PRIMARY];

CREATE CLUSTERED INDEX IXC_ResourceChangeDataStaging
    ON dbo.ResourceChangeDataStaging(Id ASC, Timestamp ASC) WITH (ONLINE = ON)
    ON [PRIMARY];

ALTER TABLE dbo.ResourceChangeDataStaging WITH CHECK
    ADD CONSTRAINT CHK_ResourceChangeDataStaging_partition CHECK (Timestamp < CONVERT (DATETIME2 (7), N'9999-12-31 23:59:59.9999999'));

ALTER TABLE dbo.ResourceChangeDataStaging CHECK CONSTRAINT CHK_ResourceChangeDataStaging_partition;

CREATE TABLE dbo.ResourceChangeType (
    ResourceChangeTypeId TINYINT       NOT NULL,
    Name                 NVARCHAR (50) NOT NULL,
    CONSTRAINT PK_ResourceChangeType PRIMARY KEY CLUSTERED (ResourceChangeTypeId),
    CONSTRAINT UQ_ResourceChangeType_Name UNIQUE NONCLUSTERED (Name)
) ON [PRIMARY];


GO
INSERT  dbo.ResourceChangeType (ResourceChangeTypeId, Name)
VALUES                        (0, N'Creation');

INSERT  dbo.ResourceChangeType (ResourceChangeTypeId, Name)
VALUES                        (1, N'Update');

INSERT  dbo.ResourceChangeType (ResourceChangeTypeId, Name)
VALUES                        (2, N'Deletion');

CREATE TABLE dbo.ResourceType (
    ResourceTypeId SMALLINT      IDENTITY (1, 1) NOT NULL,
    Name           NVARCHAR (50) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CONSTRAINT UQ_ResourceType_ResourceTypeId UNIQUE (ResourceTypeId),
    CONSTRAINT PKC_ResourceType PRIMARY KEY CLUSTERED (Name) WITH (DATA_COMPRESSION = PAGE)
);

CREATE TABLE dbo.ResourceWriteClaim (
    ResourceSurrogateId BIGINT         NOT NULL,
    ClaimTypeId         TINYINT        NOT NULL,
    ClaimValue          NVARCHAR (128) NOT NULL
)
WITH (DATA_COMPRESSION = PAGE);

CREATE CLUSTERED INDEX IXC_ResourceWriteClaim
    ON dbo.ResourceWriteClaim(ResourceSurrogateId, ClaimTypeId);

CREATE TABLE dbo.SchemaMigrationProgress (
    Timestamp DATETIME2 (3)  DEFAULT CURRENT_TIMESTAMP,
    Message   NVARCHAR (MAX)
);

CREATE TABLE dbo.SearchParam (
    SearchParamId        SMALLINT           IDENTITY (1, 1) NOT NULL,
    Uri                  VARCHAR (128)      COLLATE Latin1_General_100_CS_AS NOT NULL,
    Status               VARCHAR (20)       NULL,
    LastUpdated          DATETIMEOFFSET (7) NULL,
    IsPartiallySupported BIT                NULL,
    CONSTRAINT UQ_SearchParam_SearchParamId UNIQUE (SearchParamId),
    CONSTRAINT PKC_SearchParam PRIMARY KEY CLUSTERED (Uri) WITH (DATA_COMPRESSION = PAGE)
);

CREATE TABLE dbo.StringSearchParam (
    ResourceTypeId      SMALLINT       NOT NULL,
    ResourceSurrogateId BIGINT         NOT NULL,
    SearchParamId       SMALLINT       NOT NULL,
    Text                NVARCHAR (256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL,
    TextOverflow        NVARCHAR (MAX) COLLATE Latin1_General_100_CI_AI_SC NULL,
    IsMin               BIT            CONSTRAINT string_IsMin_Constraint DEFAULT 0 NOT NULL,
    IsMax               BIT            CONSTRAINT string_IsMax_Constraint DEFAULT 0 NOT NULL
);

ALTER TABLE dbo.StringSearchParam SET (LOCK_ESCALATION = AUTO);

CREATE CLUSTERED INDEX IXC_StringSearchParam
    ON dbo.StringSearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE INDEX IX_SearchParamId_Text_INCLUDE_TextOverflow_IsMin_IsMax
    ON dbo.StringSearchParam(SearchParamId, Text)
    INCLUDE(TextOverflow, IsMin, IsMax) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE INDEX IX_SearchParamId_Text_INCLUDE_IsMin_IsMax_WHERE_TextOverflow_NOT_NULL
    ON dbo.StringSearchParam(SearchParamId, Text)
    INCLUDE(IsMin, IsMax) WHERE TextOverflow IS NOT NULL WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE TABLE dbo.System (
    SystemId INT            IDENTITY (1, 1) NOT NULL,
    Value    NVARCHAR (256) NOT NULL,
    CONSTRAINT UQ_System_SystemId UNIQUE (SystemId),
    CONSTRAINT PKC_System PRIMARY KEY CLUSTERED (Value) WITH (DATA_COMPRESSION = PAGE)
);

CREATE TABLE [dbo].[TaskInfo] (
    [TaskId]            VARCHAR (64)  NOT NULL,
    [QueueId]           VARCHAR (64)  NOT NULL,
    [Status]            SMALLINT      NOT NULL,
    [TaskTypeId]        SMALLINT      NOT NULL,
    [RunId]             VARCHAR (50)  NULL,
    [IsCanceled]        BIT           NOT NULL,
    [RetryCount]        SMALLINT      NOT NULL,
    [MaxRetryCount]     SMALLINT      NOT NULL,
    [HeartbeatDateTime] DATETIME2 (7) NULL,
    [InputData]         VARCHAR (MAX) NOT NULL,
    [TaskContext]       VARCHAR (MAX) NULL,
    [Result]            VARCHAR (MAX) NULL,
    [CreateDateTime]    DATETIME2 (7) CONSTRAINT DF_TaskInfo_CreateDate DEFAULT SYSUTCDATETIME() NOT NULL,
    [StartDateTime]     DATETIME2 (7) NULL,
    [EndDateTime]       DATETIME2 (7) NULL,
    [Worker]            VARCHAR (100) NULL,
    [RestartInfo]       VARCHAR (MAX) NULL,
    [ParentTaskId]      VARCHAR (64)  NULL,
    CONSTRAINT PKC_TaskInfo PRIMARY KEY CLUSTERED (TaskId) WITH (DATA_COMPRESSION = PAGE)
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY];


GO
CREATE NONCLUSTERED INDEX IX_QueueId_Status
    ON dbo.TaskInfo(QueueId, Status);


GO
CREATE NONCLUSTERED INDEX IX_QueueId_ParentTaskId
    ON dbo.TaskInfo(QueueId, ParentTaskId);

CREATE TABLE dbo.TokenDateTimeCompositeSearchParam (
    ResourceTypeId      SMALLINT      NOT NULL,
    ResourceSurrogateId BIGINT        NOT NULL,
    SearchParamId       SMALLINT      NOT NULL,
    SystemId1           INT           NULL,
    Code1               VARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    StartDateTime2      DATETIME2 (7) NOT NULL,
    EndDateTime2        DATETIME2 (7) NOT NULL,
    IsLongerThanADay2   BIT           NOT NULL,
    CodeOverflow1       VARCHAR (MAX) COLLATE Latin1_General_100_CS_AS NULL
);

ALTER TABLE dbo.TokenDateTimeCompositeSearchParam SET (LOCK_ESCALATION = AUTO);

CREATE CLUSTERED INDEX IXC_TokenDateTimeCompositeSearchParam
    ON dbo.TokenDateTimeCompositeSearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE INDEX IX_SearchParamId_Code1_StartDateTime2_EndDateTime2_INCLUDE_SystemId1_IsLongerThanADay2
    ON dbo.TokenDateTimeCompositeSearchParam(SearchParamId, Code1, StartDateTime2, EndDateTime2)
    INCLUDE(SystemId1, IsLongerThanADay2) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE INDEX IX_SearchParamId_Code1_EndDateTime2_StartDateTime2_INCLUDE_SystemId1_IsLongerThanADay2
    ON dbo.TokenDateTimeCompositeSearchParam(SearchParamId, Code1, EndDateTime2, StartDateTime2)
    INCLUDE(SystemId1, IsLongerThanADay2) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE INDEX IX_SearchParamId_Code1_StartDateTime2_EndDateTime2_INCLUDE_SystemId1_WHERE_IsLongerThanADay2_1
    ON dbo.TokenDateTimeCompositeSearchParam(SearchParamId, Code1, StartDateTime2, EndDateTime2)
    INCLUDE(SystemId1) WHERE IsLongerThanADay2 = 1 WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE INDEX IX_SearchParamId_Code1_EndDateTime2_StartDateTime2_INCLUDE_SystemId1_WHERE_IsLongerThanADay2_1
    ON dbo.TokenDateTimeCompositeSearchParam(SearchParamId, Code1, EndDateTime2, StartDateTime2)
    INCLUDE(SystemId1) WHERE IsLongerThanADay2 = 1 WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE TABLE dbo.TokenNumberNumberCompositeSearchParam (
    ResourceTypeId      SMALLINT         NOT NULL,
    ResourceSurrogateId BIGINT           NOT NULL,
    SearchParamId       SMALLINT         NOT NULL,
    SystemId1           INT              NULL,
    Code1               VARCHAR (256)    COLLATE Latin1_General_100_CS_AS NOT NULL,
    SingleValue2        DECIMAL (36, 18) NULL,
    LowValue2           DECIMAL (36, 18) NULL,
    HighValue2          DECIMAL (36, 18) NULL,
    SingleValue3        DECIMAL (36, 18) NULL,
    LowValue3           DECIMAL (36, 18) NULL,
    HighValue3          DECIMAL (36, 18) NULL,
    HasRange            BIT              NOT NULL,
    CodeOverflow1       VARCHAR (MAX)    COLLATE Latin1_General_100_CS_AS NULL
);

ALTER TABLE dbo.TokenNumberNumberCompositeSearchParam SET (LOCK_ESCALATION = AUTO);

CREATE CLUSTERED INDEX IXC_TokenNumberNumberCompositeSearchParam
    ON dbo.TokenNumberNumberCompositeSearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE INDEX IX_SearchParamId_Code1_SingleValue2_SingleValue3_INCLUDE_SystemId1_WHERE_HasRange_0
    ON dbo.TokenNumberNumberCompositeSearchParam(SearchParamId, Code1, SingleValue2, SingleValue3)
    INCLUDE(SystemId1) WHERE HasRange = 0 WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE INDEX IX_SearchParamId_Code1_LowValue2_HighValue2_LowValue3_HighValue3_INCLUDE_SystemId1_WHERE_HasRange_1
    ON dbo.TokenNumberNumberCompositeSearchParam(SearchParamId, Code1, LowValue2, HighValue2, LowValue3, HighValue3)
    INCLUDE(SystemId1) WHERE HasRange = 1 WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE TABLE dbo.TokenQuantityCompositeSearchParam (
    ResourceTypeId      SMALLINT         NOT NULL,
    ResourceSurrogateId BIGINT           NOT NULL,
    SearchParamId       SMALLINT         NOT NULL,
    SystemId1           INT              NULL,
    Code1               VARCHAR (256)    COLLATE Latin1_General_100_CS_AS NOT NULL,
    SystemId2           INT              NULL,
    QuantityCodeId2     INT              NULL,
    SingleValue2        DECIMAL (36, 18) NULL,
    LowValue2           DECIMAL (36, 18) NULL,
    HighValue2          DECIMAL (36, 18) NULL,
    CodeOverflow1       VARCHAR (MAX)    COLLATE Latin1_General_100_CS_AS NULL
);

ALTER TABLE dbo.TokenQuantityCompositeSearchParam SET (LOCK_ESCALATION = AUTO);

CREATE CLUSTERED INDEX IXC_TokenQuantityCompositeSearchParam
    ON dbo.TokenQuantityCompositeSearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE INDEX IX_SearchParamId_Code1_SingleValue2_INCLUDE_QuantityCodeId2_SystemId1_SystemId2_WHERE_SingleValue2_NOT_NULL
    ON dbo.TokenQuantityCompositeSearchParam(SearchParamId, Code1, SingleValue2)
    INCLUDE(QuantityCodeId2, SystemId1, SystemId2) WHERE SingleValue2 IS NOT NULL WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE INDEX IX_SearchParamId_Code1_LowValue2_HighValue2_INCLUDE_QuantityCodeId2_SystemId1_SystemId2_WHERE_LowValue2_NOT_NULL
    ON dbo.TokenQuantityCompositeSearchParam(SearchParamId, Code1, LowValue2, HighValue2)
    INCLUDE(QuantityCodeId2, SystemId1, SystemId2) WHERE LowValue2 IS NOT NULL WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE INDEX IX_SearchParamId_Code1_HighValue2_LowValue2_INCLUDE_QuantityCodeId2_SystemId1_SystemId2_WHERE_LowValue2_NOT_NULL
    ON dbo.TokenQuantityCompositeSearchParam(SearchParamId, Code1, HighValue2, LowValue2)
    INCLUDE(QuantityCodeId2, SystemId1, SystemId2) WHERE LowValue2 IS NOT NULL WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE TABLE dbo.TokenSearchParam (
    ResourceTypeId      SMALLINT      NOT NULL,
    ResourceSurrogateId BIGINT        NOT NULL,
    SearchParamId       SMALLINT      NOT NULL,
    SystemId            INT           NULL,
    Code                VARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow        VARCHAR (MAX) COLLATE Latin1_General_100_CS_AS NULL
);

ALTER TABLE dbo.TokenSearchParam SET (LOCK_ESCALATION = AUTO);

CREATE CLUSTERED INDEX IXC_TokenSearchParam
    ON dbo.TokenSearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE INDEX IX_SearchParamId_Code_INCLUDE_SystemId
    ON dbo.TokenSearchParam(SearchParamId, Code)
    INCLUDE(SystemId) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE TABLE dbo.TokenStringCompositeSearchParam (
    ResourceTypeId      SMALLINT       NOT NULL,
    ResourceSurrogateId BIGINT         NOT NULL,
    SearchParamId       SMALLINT       NOT NULL,
    SystemId1           INT            NULL,
    Code1               VARCHAR (256)  COLLATE Latin1_General_100_CS_AS NOT NULL,
    Text2               NVARCHAR (256) COLLATE Latin1_General_CI_AI NOT NULL,
    TextOverflow2       NVARCHAR (MAX) COLLATE Latin1_General_CI_AI NULL,
    CodeOverflow1       VARCHAR (MAX)  COLLATE Latin1_General_100_CS_AS NULL
);

ALTER TABLE dbo.TokenStringCompositeSearchParam SET (LOCK_ESCALATION = AUTO);

CREATE CLUSTERED INDEX IXC_TokenStringCompositeSearchParam
    ON dbo.TokenStringCompositeSearchParam(ResourceSurrogateId, SearchParamId) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE INDEX IX_SearchParamId_Code1_Text2_INCLUDE_SystemId1_TextOverflow2
    ON dbo.TokenStringCompositeSearchParam(SearchParamId, Code1, Text2)
    INCLUDE(SystemId1, TextOverflow2) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE INDEX IX_SearchParamId_Code1_Text2_INCLUDE_SystemId1_WHERE_TextOverflow2_NOT_NULL
    ON dbo.TokenStringCompositeSearchParam(SearchParamId, Code1, Text2)
    INCLUDE(SystemId1) WHERE TextOverflow2 IS NOT NULL WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE TABLE dbo.TokenText (
    ResourceTypeId      SMALLINT       NOT NULL,
    ResourceSurrogateId BIGINT         NOT NULL,
    SearchParamId       SMALLINT       NOT NULL,
    Text                NVARCHAR (400) COLLATE Latin1_General_CI_AI NOT NULL,
    IsHistory           BIT            NOT NULL
);

ALTER TABLE dbo.TokenText
    ADD CONSTRAINT DF_TokenText_IsHistory DEFAULT 0 FOR IsHistory;

ALTER TABLE dbo.TokenText SET (LOCK_ESCALATION = AUTO);

CREATE CLUSTERED INDEX IXC_TokenText
    ON dbo.TokenText(ResourceTypeId, ResourceSurrogateId, SearchParamId) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE NONCLUSTERED INDEX IX_TokenText_SearchParamId_Text
    ON dbo.TokenText(ResourceTypeId, SearchParamId, Text, ResourceSurrogateId) WHERE IsHistory = 0 WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE TABLE dbo.TokenTokenCompositeSearchParam (
    ResourceTypeId      SMALLINT      NOT NULL,
    ResourceSurrogateId BIGINT        NOT NULL,
    SearchParamId       SMALLINT      NOT NULL,
    SystemId1           INT           NULL,
    Code1               VARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    SystemId2           INT           NULL,
    Code2               VARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow1       VARCHAR (MAX) COLLATE Latin1_General_100_CS_AS NULL,
    CodeOverflow2       VARCHAR (MAX) COLLATE Latin1_General_100_CS_AS NULL
);

ALTER TABLE dbo.TokenTokenCompositeSearchParam SET (LOCK_ESCALATION = AUTO);

CREATE CLUSTERED INDEX IXC_TokenTokenCompositeSearchParam
    ON dbo.TokenTokenCompositeSearchParam(ResourceSurrogateId, SearchParamId) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE INDEX IX_SearchParamId_Code1_Code2_INCLUDE_SystemId1_SystemId2
    ON dbo.TokenTokenCompositeSearchParam(SearchParamId, Code1, Code2)
    INCLUDE(SystemId1, SystemId2) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE TABLE dbo.Transactions (
    SurrogateIdRangeFirstValue  BIGINT         NOT NULL,
    SurrogateIdRangeLastValue   BIGINT         NOT NULL,
    Definition                  VARCHAR (2000) NULL,
    IsCompleted                 BIT            CONSTRAINT DF_Transactions_IsCompleted DEFAULT 0 NOT NULL,
    IsSuccess                   BIT            CONSTRAINT DF_Transactions_IsSuccess DEFAULT 0 NOT NULL,
    IsVisible                   BIT            CONSTRAINT DF_Transactions_IsVisible DEFAULT 0 NOT NULL,
    IsHistoryMoved              BIT            CONSTRAINT DF_Transactions_IsHistoryMoved DEFAULT 0 NOT NULL,
    CreateDate                  DATETIME       CONSTRAINT DF_Transactions_CreateDate DEFAULT getUTCdate() NOT NULL,
    EndDate                     DATETIME       NULL,
    VisibleDate                 DATETIME       NULL,
    HistoryMovedDate            DATETIME       NULL,
    HeartbeatDate               DATETIME       CONSTRAINT DF_Transactions_HeartbeatDate DEFAULT getUTCdate() NOT NULL,
    FailureReason               VARCHAR (MAX)  NULL,
    IsControlledByClient        BIT            CONSTRAINT DF_Transactions_IsControlledByClient DEFAULT 1 NOT NULL,
    InvisibleHistoryRemovedDate DATETIME       NULL CONSTRAINT PKC_Transactions_SurrogateIdRangeFirstValue PRIMARY KEY CLUSTERED (SurrogateIdRangeFirstValue)
);

CREATE INDEX IX_IsVisible
    ON dbo.Transactions(IsVisible);

CREATE TABLE dbo.UriSearchParam (
    ResourceTypeId      SMALLINT      NOT NULL,
    ResourceSurrogateId BIGINT        NOT NULL,
    SearchParamId       SMALLINT      NOT NULL,
    Uri                 VARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL
);

ALTER TABLE dbo.UriSearchParam SET (LOCK_ESCALATION = AUTO);

CREATE CLUSTERED INDEX IXC_UriSearchParam
    ON dbo.UriSearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE INDEX IX_SearchParamId_Uri
    ON dbo.UriSearchParam(SearchParamId, Uri) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE TABLE dbo.WatchdogLeases (
    Watchdog              VARCHAR (100) NOT NULL,
    LeaseHolder           VARCHAR (100) CONSTRAINT DF_WatchdogLeases_LeaseHolder DEFAULT '' NOT NULL,
    LeaseEndTime          DATETIME      CONSTRAINT DF_WatchdogLeases_LeaseEndTime DEFAULT 0 NOT NULL,
    RemainingLeaseTimeSec AS            datediff(second, getUTCdate(), LeaseEndTime),
    LeaseRequestor        VARCHAR (100) CONSTRAINT DF_WatchdogLeases_LeaseRequestor DEFAULT '' NOT NULL,
    LeaseRequestTime      DATETIME      CONSTRAINT DF_WatchdogLeases_LeaseRequestTime DEFAULT 0 NOT NULL CONSTRAINT PKC_WatchdogLeases_Watchdog PRIMARY KEY CLUSTERED (Watchdog)
);

COMMIT
GO
CREATE PROCEDURE dbo.AcquireReindexJobs
@jobHeartbeatTimeoutThresholdInSeconds BIGINT, @maximumNumberOfConcurrentJobsAllowed INT
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;
DECLARE @expirationDateTime AS DATETIME2 (7);
SELECT @expirationDateTime = DATEADD(second, -@jobHeartbeatTimeoutThresholdInSeconds, SYSUTCDATETIME());
DECLARE @numberOfRunningJobs AS INT;
SELECT @numberOfRunningJobs = COUNT(*)
FROM   dbo.ReindexJob WITH (TABLOCKX)
WHERE  Status = 'Running'
       AND HeartbeatDateTime > @expirationDateTime;
DECLARE @limit AS INT = @maximumNumberOfConcurrentJobsAllowed - @numberOfRunningJobs;
IF (@limit > 0)
    BEGIN
        DECLARE @availableJobs TABLE (
            Id         VARCHAR (64) COLLATE Latin1_General_100_CS_AS NOT NULL,
            JobVersion BINARY (8)   NOT NULL);
        INSERT INTO @availableJobs
        SELECT   TOP (@limit) Id,
                              JobVersion
        FROM     dbo.ReindexJob
        WHERE    (Status = 'Queued'
                  OR (Status = 'Running'
                      AND HeartbeatDateTime <= @expirationDateTime))
        ORDER BY HeartbeatDateTime;
        DECLARE @heartbeatDateTime AS DATETIME2 (7) = SYSUTCDATETIME();
        UPDATE dbo.ReindexJob
        SET    Status            = 'Running',
               HeartbeatDateTime = @heartbeatDateTime,
               RawJobRecord      = JSON_MODIFY(RawJobRecord, '$.status', 'Running')
        OUTPUT inserted.RawJobRecord, inserted.JobVersion
        FROM   dbo.ReindexJob AS job
               INNER JOIN
               @availableJobs AS availableJob
               ON job.Id = availableJob.Id
                  AND job.JobVersion = availableJob.JobVersion;
    END
COMMIT TRANSACTION;

GO
CREATE PROCEDURE dbo.AcquireWatchdogLease
@Watchdog VARCHAR (100), @Worker VARCHAR (100), @AllowRebalance BIT=1, @ForceAcquire BIT=0, @LeasePeriodSec FLOAT, @WorkerIsRunning BIT=0, @LeaseEndTime DATETIME OUTPUT, @IsAcquired BIT OUTPUT, @CurrentLeaseHolder VARCHAR (100)=NULL OUTPUT
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @SP AS VARCHAR (100) = 'AcquireWatchdogLease', @Mode AS VARCHAR (100), @msg AS VARCHAR (1000), @MyLeasesNumber AS INT, @OtherValidRequestsOrLeasesNumber AS INT, @MyValidRequestsOrLeasesNumber AS INT, @DesiredLeasesNumber AS INT, @NotLeasedWatchdogNumber AS INT, @WatchdogNumber AS INT, @Now AS DATETIME, @MyLastChangeTime AS DATETIME, @PreviousLeaseHolder AS VARCHAR (100), @Rows AS INT = 0, @NumberOfWorkers AS INT, @st AS DATETIME = getUTCdate(), @RowsInt AS INT, @Pattern AS VARCHAR (100);
BEGIN TRY
    SET @Mode = 'R=' + isnull(@Watchdog, 'NULL') + ' W=' + isnull(@Worker, 'NULL') + ' F=' + isnull(CONVERT (VARCHAR, @ForceAcquire), 'NULL') + ' LP=' + isnull(CONVERT (VARCHAR, @LeasePeriodSec), 'NULL');
    SET @CurrentLeaseHolder = '';
    SET @IsAcquired = 0;
    SET @Now = getUTCdate();
    SET @LeaseEndTime = @Now;
    SET @Pattern = NULLIF ((SELECT Char
                            FROM   dbo.Parameters
                            WHERE  Id = 'WatchdogLeaseHolderIncludePatternFor' + @Watchdog), '');
    IF @Pattern IS NULL
        SET @Pattern = NULLIF ((SELECT Char
                                FROM   dbo.Parameters
                                WHERE  Id = 'WatchdogLeaseHolderIncludePattern'), '');
    IF @Pattern IS NOT NULL
       AND @Worker NOT LIKE @Pattern
        BEGIN
            SET @msg = 'Worker does not match include pattern=' + @Pattern;
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @Rows, @Text = @msg;
            SET @CurrentLeaseHolder = isnull((SELECT LeaseHolder
                                              FROM   dbo.WatchdogLeases
                                              WHERE  Watchdog = @Watchdog), '');
            RETURN;
        END
    SET @Pattern = NULLIF ((SELECT Char
                            FROM   dbo.Parameters
                            WHERE  Id = 'WatchdogLeaseHolderExcludePatternFor' + @Watchdog), '');
    IF @Pattern IS NULL
        SET @Pattern = NULLIF ((SELECT Char
                                FROM   dbo.Parameters
                                WHERE  Id = 'WatchdogLeaseHolderExcludePattern'), '');
    IF @Pattern IS NOT NULL
       AND @Worker LIKE @Pattern
        BEGIN
            SET @msg = 'Worker matches exclude pattern=' + @Pattern;
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @Rows, @Text = @msg;
            SET @CurrentLeaseHolder = isnull((SELECT LeaseHolder
                                              FROM   dbo.WatchdogLeases
                                              WHERE  Watchdog = @Watchdog), '');
            RETURN;
        END
    DECLARE @Watchdogs TABLE (
        Watchdog VARCHAR (100) PRIMARY KEY);
    INSERT INTO @Watchdogs
    SELECT Watchdog
    FROM   dbo.WatchdogLeases WITH (NOLOCK)
    WHERE  RemainingLeaseTimeSec * (-1) > 10 * @LeasePeriodSec
           OR @ForceAcquire = 1
              AND Watchdog = @Watchdog
              AND LeaseHolder <> @Worker;
    IF @@rowcount > 0
        BEGIN
            DELETE dbo.WatchdogLeases
            WHERE  Watchdog IN (SELECT Watchdog
                                FROM   @Watchdogs);
            SET @Rows += @@rowcount;
            IF @Rows > 0
                BEGIN
                    SET @msg = '';
                    SELECT @msg = CONVERT (VARCHAR (1000), @msg + CASE WHEN @msg = '' THEN '' ELSE ',' END + Watchdog)
                    FROM   @Watchdogs;
                    SET @msg = CONVERT (VARCHAR (1000), 'Remove old/forced leases:' + @msg);
                    EXECUTE dbo.LogEvent @Process = 'AcquireWatchdogLease', @Status = 'Info', @Mode = @Mode, @Target = 'WatchdogLeases', @Action = 'Delete', @Rows = @Rows, @Text = @msg;
                END
        END
    SET @NumberOfWorkers = 1 + (SELECT count(*)
                                FROM   (SELECT LeaseHolder
                                        FROM   dbo.WatchdogLeases WITH (NOLOCK)
                                        WHERE  LeaseHolder <> @Worker
                                        UNION
                                        SELECT LeaseRequestor
                                        FROM   dbo.WatchdogLeases WITH (NOLOCK)
                                        WHERE  LeaseRequestor <> @Worker
                                               AND LeaseRequestor <> '') AS A);
    SET @Mode = CONVERT (VARCHAR (100), @Mode + ' N=' + CONVERT (VARCHAR (10), @NumberOfWorkers));
    IF NOT EXISTS (SELECT *
                   FROM   dbo.WatchdogLeases WITH (NOLOCK)
                   WHERE  Watchdog = @Watchdog)
        INSERT INTO dbo.WatchdogLeases (Watchdog, LeaseEndTime, LeaseRequestTime)
        SELECT @Watchdog,
               dateadd(day, -10, @Now),
               dateadd(day, -10, @Now)
        WHERE  NOT EXISTS (SELECT *
                           FROM   dbo.WatchdogLeases WITH (TABLOCKX)
                           WHERE  Watchdog = @Watchdog);
    SET @LeaseEndTime = dateadd(second, @LeasePeriodSec, @Now);
    SET @WatchdogNumber = (SELECT count(*)
                           FROM   dbo.WatchdogLeases WITH (NOLOCK));
    SET @NotLeasedWatchdogNumber = (SELECT count(*)
                                    FROM   dbo.WatchdogLeases WITH (NOLOCK)
                                    WHERE  LeaseHolder = ''
                                           OR LeaseEndTime < @Now);
    SET @MyLeasesNumber = (SELECT count(*)
                           FROM   dbo.WatchdogLeases WITH (NOLOCK)
                           WHERE  LeaseHolder = @Worker
                                  AND LeaseEndTime > @Now);
    SET @OtherValidRequestsOrLeasesNumber = (SELECT count(*)
                                             FROM   dbo.WatchdogLeases WITH (NOLOCK)
                                             WHERE  LeaseHolder <> @Worker
                                                    AND LeaseEndTime > @Now
                                                    OR LeaseRequestor <> @Worker
                                                       AND datediff(second, LeaseRequestTime, @Now) < @LeasePeriodSec);
    SET @MyValidRequestsOrLeasesNumber = (SELECT count(*)
                                          FROM   dbo.WatchdogLeases WITH (NOLOCK)
                                          WHERE  LeaseHolder = @Worker
                                                 AND LeaseEndTime > @Now
                                                 OR LeaseRequestor = @Worker
                                                    AND datediff(second, LeaseRequestTime, @Now) < @LeasePeriodSec);
    SET @DesiredLeasesNumber = ceiling(1.0 * @WatchdogNumber / @NumberOfWorkers);
    IF @DesiredLeasesNumber = 0
        SET @DesiredLeasesNumber = 1;
    IF @DesiredLeasesNumber = 1
       AND @OtherValidRequestsOrLeasesNumber = 1
       AND @WatchdogNumber = 1
        SET @DesiredLeasesNumber = 0;
    IF @MyValidRequestsOrLeasesNumber = floor(1.0 * @WatchdogNumber / @NumberOfWorkers)
       AND @OtherValidRequestsOrLeasesNumber + @MyValidRequestsOrLeasesNumber = @WatchdogNumber
        SET @DesiredLeasesNumber = @DesiredLeasesNumber - 1;
    UPDATE dbo.WatchdogLeases
    SET    LeaseHolder          = @Worker,
           LeaseEndTime         = @LeaseEndTime,
           LeaseRequestor       = '',
           @PreviousLeaseHolder = LeaseHolder
    WHERE  Watchdog = @Watchdog
           AND NOT (LeaseRequestor <> @Worker
                    AND datediff(second, LeaseRequestTime, @Now) < @LeasePeriodSec)
           AND (LeaseHolder = @Worker
                AND (LeaseEndTime > @Now
                     OR @WorkerIsRunning = 1)
                OR LeaseEndTime < @Now
                   AND (@DesiredLeasesNumber > @MyLeasesNumber
                        OR @OtherValidRequestsOrLeasesNumber < @WatchdogNumber));
    IF @@rowcount > 0
        BEGIN
            SET @IsAcquired = 1;
            SET @msg = 'Lease holder changed from [' + isnull(@PreviousLeaseHolder, '') + '] to [' + @Worker + ']';
            IF @PreviousLeaseHolder <> @Worker
                EXECUTE dbo.LogEvent @Process = 'AcquireWatchdogLease', @Status = 'Info', @Mode = @Mode, @Text = @msg;
        END
    ELSE
        IF @AllowRebalance = 1
            BEGIN
                SET @CurrentLeaseHolder = (SELECT LeaseHolder
                                           FROM   dbo.WatchdogLeases
                                           WHERE  Watchdog = @Watchdog);
                UPDATE dbo.WatchdogLeases
                SET    LeaseRequestTime = @Now
                WHERE  Watchdog = @Watchdog
                       AND LeaseRequestor = @Worker
                       AND datediff(second, LeaseRequestTime, @Now) < @LeasePeriodSec;
                IF @DesiredLeasesNumber > @MyValidRequestsOrLeasesNumber
                    BEGIN
                        UPDATE A
                        SET    LeaseRequestor   = @Worker,
                               LeaseRequestTime = @Now
                        FROM   dbo.WatchdogLeases AS A
                        WHERE  Watchdog = @Watchdog
                               AND NOT (LeaseRequestor <> @Worker
                                        AND datediff(second, LeaseRequestTime, @Now) < @LeasePeriodSec)
                               AND @NotLeasedWatchdogNumber = 0
                               AND (SELECT count(*)
                                    FROM   dbo.WatchdogLeases AS B
                                    WHERE  B.LeaseHolder = A.LeaseHolder
                                           AND datediff(second, B.LeaseEndTime, @Now) < @LeasePeriodSec) > @DesiredLeasesNumber;
                        SET @RowsInt = @@rowcount;
                        SET @msg = '@DesiredLeasesNumber=[' + CONVERT (VARCHAR (10), @DesiredLeasesNumber) + '] > @MyValidRequestsOrLeasesNumber=[' + CONVERT (VARCHAR (10), @MyValidRequestsOrLeasesNumber) + ']';
                        EXECUTE dbo.LogEvent @Process = 'AcquireWatchdogLease', @Status = 'Info', @Mode = @Mode, @Rows = @RowsInt, @Text = @msg;
                    END
            END
    SET @Mode = CONVERT (VARCHAR (100), @Mode + ' A=' + CONVERT (VARCHAR (1), @IsAcquired));
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @Rows;
END TRY
BEGIN CATCH
    IF @@trancount > 0
        ROLLBACK;
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = 'AcquireWatchdogLease', @Status = 'Error', @Mode = @Mode;
    THROW;
END CATCH

GO
CREATE OR ALTER PROCEDURE dbo.AddPartitionOnResourceChanges
@partitionBoundary DATETIME2 (7) OUTPUT
AS
BEGIN
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    DECLARE @rightPartitionBoundary AS DATETIME2 (7) = CAST ((SELECT   TOP (1) value
                                                              FROM     sys.partition_range_values AS prv
                                                                       INNER JOIN
                                                                       sys.partition_functions AS pf
                                                                       ON pf.function_id = prv.function_id
                                                              WHERE    pf.name = N'PartitionFunction_ResourceChangeData_Timestamp'
                                                              ORDER BY prv.boundary_id DESC) AS DATETIME2 (7));
    DECLARE @timestamp AS DATETIME2 (7) = DATEADD(hour, DATEDIFF(hour, 0, sysutcdatetime()), 0);
    IF (@rightPartitionBoundary < @timestamp)
        BEGIN
            SET @rightPartitionBoundary = @timestamp;
        END
    SET @rightPartitionBoundary = DATEADD(hour, 1, @rightPartitionBoundary);
    ALTER PARTITION SCHEME PartitionScheme_ResourceChangeData_Timestamp NEXT USED [Primary];
    ALTER PARTITION FUNCTION PartitionFunction_ResourceChangeData_Timestamp( )
        SPLIT RANGE (@rightPartitionBoundary);
    SET @partitionBoundary = @rightPartitionBoundary;
    COMMIT TRANSACTION;
END

GO
CREATE PROCEDURE dbo.ArchiveJobs
@QueueType TINYINT
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'ArchiveJobs', @Mode AS VARCHAR (100) = '', @st AS DATETIME = getUTCdate(), @Rows AS INT = 0, @PartitionId AS TINYINT, @MaxPartitions AS TINYINT = 16, @LookedAtPartitions AS TINYINT = 0, @InflightRows AS INT = 0, @Lock AS VARCHAR (100) = 'DequeueJob_' + CONVERT (VARCHAR, @QueueType);
BEGIN TRY
    SET @PartitionId = @MaxPartitions * rand();
    BEGIN TRANSACTION;
    EXECUTE sp_getapplock @Lock, 'Exclusive';
    WHILE @LookedAtPartitions <= @MaxPartitions
        BEGIN
            SET @InflightRows += (SELECT count(*)
                                  FROM   dbo.JobQueue
                                  WHERE  PartitionId = @PartitionId
                                         AND QueueType = @QueueType
                                         AND Status IN (0, 1));
            SET @PartitionId = CASE WHEN @PartitionId = 15 THEN 0 ELSE @PartitionId + 1 END;
            SET @LookedAtPartitions = @LookedAtPartitions + 1;
        END
    IF @InflightRows = 0
        BEGIN
            SET @LookedAtPartitions = 0;
            WHILE @LookedAtPartitions <= @MaxPartitions
                BEGIN
                    UPDATE dbo.JobQueue
                    SET    Status = 5
                    WHERE  PartitionId = @PartitionId
                           AND QueueType = @QueueType
                           AND Status IN (2, 3, 4);
                    SET @Rows += @@rowcount;
                    SET @PartitionId = CASE WHEN @PartitionId = 15 THEN 0 ELSE @PartitionId + 1 END;
                    SET @LookedAtPartitions = @LookedAtPartitions + 1;
                END
        END
    COMMIT TRANSACTION;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @Rows;
END TRY
BEGIN CATCH
    IF @@trancount > 0
        ROLLBACK;
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH

GO
CREATE PROCEDURE dbo.CaptureResourceChanges
@isDeleted BIT, @version INT, @resourceId VARCHAR (64), @resourceTypeId SMALLINT
AS
BEGIN
    DECLARE @changeType AS SMALLINT;
    IF (@isDeleted = 1)
        BEGIN
            SET @changeType = 2;
        END
    ELSE
        BEGIN
            IF (@version = 1)
                BEGIN
                    SET @changeType = 0;
                END
            ELSE
                BEGIN
                    SET @changeType = 1;
                END
        END
    INSERT  INTO dbo.ResourceChangeData (ResourceId, ResourceTypeId, ResourceVersion, ResourceChangeTypeId)
    VALUES                             (@resourceId, @resourceTypeId, @version, @changeType);
END

GO
CREATE PROCEDURE dbo.CaptureResourceIdsForChanges
@Resources dbo.ResourceList READONLY
AS
SET NOCOUNT ON;
INSERT INTO dbo.ResourceChangeData (ResourceId, ResourceTypeId, ResourceVersion, ResourceChangeTypeId)
SELECT ResourceId,
       ResourceTypeId,
       Version,
       CASE WHEN IsDeleted = 1 THEN 2 WHEN Version > 1 THEN 1 ELSE 0 END
FROM   @Resources
WHERE  IsHistory = 0;

GO
CREATE PROCEDURE dbo.CheckActiveReindexJobs
AS
SET NOCOUNT ON;
SELECT Id
FROM   dbo.ReindexJob
WHERE  Status = 'Running'
       OR Status = 'Queued'
       OR Status = 'Paused';

GO
CREATE PROCEDURE dbo.CleanupEventLog
WITH EXECUTE AS 'dbo'
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'CleanupEventLog', @Mode AS VARCHAR (100) = '', @MaxDeleteRows AS INT, @MaxAllowedRows AS BIGINT, @RetentionPeriodSecond AS INT, @DeletedRows AS INT, @TotalDeletedRows AS INT = 0, @TotalRows AS INT, @Now AS DATETIME = getUTCdate();
EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Start';
BEGIN TRY
    SET @MaxDeleteRows = (SELECT Number
                          FROM   dbo.Parameters
                          WHERE  Id = 'CleanupEventLog.DeleteBatchSize');
    IF @MaxDeleteRows IS NULL
        RAISERROR ('Cannot get Parameter.CleanupEventLog.DeleteBatchSize', 18, 127);
    SET @MaxAllowedRows = (SELECT Number
                           FROM   dbo.Parameters
                           WHERE  Id = 'CleanupEventLog.AllowedRows');
    IF @MaxAllowedRows IS NULL
        RAISERROR ('Cannot get Parameter.CleanupEventLog.AllowedRows', 18, 127);
    SET @RetentionPeriodSecond = (SELECT Number * 24 * 60 * 60
                                  FROM   dbo.Parameters
                                  WHERE  Id = 'CleanupEventLog.RetentionPeriodDay');
    IF @RetentionPeriodSecond IS NULL
        RAISERROR ('Cannot get Parameter.CleanupEventLog.RetentionPeriodDay', 18, 127);
    SET @TotalRows = (SELECT sum(row_count)
                      FROM   sys.dm_db_partition_stats
                      WHERE  object_id = object_id('EventLog')
                             AND index_id IN (0, 1));
    SET @DeletedRows = 1;
    WHILE @DeletedRows > 0
          AND EXISTS (SELECT *
                      FROM   dbo.Parameters
                      WHERE  Id = 'CleanupEventLog.IsEnabled'
                             AND Number = 1)
        BEGIN
            SET @DeletedRows = 0;
            IF @TotalRows - @TotalDeletedRows > @MaxAllowedRows
                BEGIN
                    DELETE TOP (@MaxDeleteRows)
                           dbo.EventLog WITH (PAGLOCK)
                    WHERE  EventDate <= dateadd(second, -@RetentionPeriodSecond, @Now);
                    SET @DeletedRows = @@rowcount;
                    SET @TotalDeletedRows += @DeletedRows;
                    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Run', @Target = 'EventLog', @Action = 'Delete', @Rows = @DeletedRows, @Text = @TotalDeletedRows;
                END
        END
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @Now;
END TRY
BEGIN CATCH
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH

GO
CREATE OR ALTER PROCEDURE dbo.ConfigurePartitionOnResourceChanges
@numberOfFuturePartitionsToAdd INT
AS
BEGIN
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    DECLARE @partitionBoundary AS DATETIME2 (7) = DATEADD(hour, DATEDIFF(hour, 0, sysutcdatetime()), 0);
    DECLARE @startingRightPartitionBoundary AS DATETIME2 (7) = CAST ((SELECT   TOP (1) value
                                                                      FROM     sys.partition_range_values AS prv
                                                                               INNER JOIN
                                                                               sys.partition_functions AS pf
                                                                               ON pf.function_id = prv.function_id
                                                                      WHERE    pf.name = N'PartitionFunction_ResourceChangeData_Timestamp'
                                                                      ORDER BY prv.boundary_id DESC) AS DATETIME2 (7));
    DECLARE @numberOfPartitionsToAdd AS INT = @numberOfFuturePartitionsToAdd + 1;
    WHILE @numberOfPartitionsToAdd > 0
        BEGIN
            IF (@startingRightPartitionBoundary < @partitionBoundary)
                BEGIN
                    ALTER PARTITION SCHEME PartitionScheme_ResourceChangeData_Timestamp NEXT USED [PRIMARY];
                    ALTER PARTITION FUNCTION PartitionFunction_ResourceChangeData_Timestamp( )
                        SPLIT RANGE (@partitionBoundary);
                END
            SET @partitionBoundary = DATEADD(hour, 1, @partitionBoundary);
            SET @numberOfPartitionsToAdd -= 1;
        END
    COMMIT TRANSACTION;
END

GO
CREATE PROCEDURE dbo.CreateReindexJob
@id VARCHAR (64), @status VARCHAR (10), @rawJobRecord VARCHAR (MAX)
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @heartbeatDateTime AS DATETIME2 (7) = SYSUTCDATETIME();
INSERT  INTO dbo.ReindexJob (Id, Status, HeartbeatDateTime, RawJobRecord)
VALUES                     (@id, @status, @heartbeatDateTime, @rawJobRecord);
SELECT CAST (MIN_ACTIVE_ROWVERSION() AS INT);
COMMIT TRANSACTION;

GO
CREATE PROCEDURE dbo.CreateResourceSearchParamStats
@Table VARCHAR (100), @Column VARCHAR (100), @ResourceTypeId SMALLINT, @SearchParamId SMALLINT
WITH EXECUTE AS 'dbo'
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = object_name(@@procid), @Mode AS VARCHAR (200) = 'T=' + isnull(@Table, 'NULL') + ' C=' + isnull(@Column, 'NULL') + ' RT=' + isnull(CONVERT (VARCHAR, @ResourceTypeId), 'NULL') + ' SP=' + isnull(CONVERT (VARCHAR, @SearchParamId), 'NULL'), @st AS DATETIME = getUTCdate();
BEGIN TRY
    IF @Table IS NULL
       OR @Column IS NULL
       OR @ResourceTypeId IS NULL
       OR @SearchParamId IS NULL
        RAISERROR ('@TableName IS NULL OR @KeyColumn IS NULL OR @ResourceTypeId IS NULL OR @SearchParamId IS NULL', 18, 127);
    EXECUTE ('CREATE STATISTICS ST_' + @Column + '_WHERE_ResourceTypeId_' + @ResourceTypeId + '_SearchParamId_' + @SearchParamId + ' ON dbo.' + @Table + ' (' + @Column + ') WHERE ResourceTypeId = ' + @ResourceTypeId + ' AND SearchParamId = ' + @SearchParamId);
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Text = 'Stats created';
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    IF error_number() = 1927
        BEGIN
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st;
            RETURN;
        END
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st;
    THROW;
END CATCH

GO
CREATE PROCEDURE dbo.Defrag
@TableName VARCHAR (100), @IndexName VARCHAR (200), @PartitionNumber INT, @IsPartitioned BIT
WITH EXECUTE AS 'dbo'
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = object_name(@@procid), @Mode AS VARCHAR (200) = @TableName + '.' + @IndexName + '.' + CONVERT (VARCHAR, @PartitionNumber) + '.' + CONVERT (VARCHAR, @IsPartitioned), @st AS DATETIME = getUTCdate(), @SQL AS VARCHAR (3500), @msg AS VARCHAR (1000), @SizeBefore AS FLOAT, @SizeAfter AS FLOAT, @IndexId AS INT, @Operation AS VARCHAR (50) = CASE WHEN EXISTS (SELECT *
                                                                                                                                                                                                                                                                                                                                                                                               FROM   dbo.Parameters
                                                                                                                                                                                                                                                                                                                                                                                               WHERE  Id = 'Defrag.IndexRebuild.IsEnabled'
                                                                                                                                                                                                                                                                                                                                                                                                      AND Number = 1) THEN 'REBUILD' ELSE 'REORGANIZE' END;
SET @Mode = @Mode + ' ' + @Operation;
BEGIN TRY
    SET @IndexId = (SELECT index_id
                    FROM   sys.indexes
                    WHERE  object_id = object_id(@TableName)
                           AND name = @IndexName);
    SET @Sql = 'ALTER INDEX ' + quotename(@IndexName) + ' ON dbo.' + quotename(@TableName) + ' ' + @Operation + CASE WHEN @IsPartitioned = 1 THEN ' PARTITION = ' + CONVERT (VARCHAR, @PartitionNumber) ELSE '' END + CASE WHEN @Operation = 'REBUILD' THEN ' WITH (ONLINE = ON' + CASE WHEN EXISTS (SELECT *
                                                                                                                                                                                                                                                                                                     FROM   sys.partitions
                                                                                                                                                                                                                                                                                                     WHERE  object_id = object_id(@TableName)
                                                                                                                                                                                                                                                                                                            AND index_id = @IndexId
                                                                                                                                                                                                                                                                                                            AND data_compression_desc = 'PAGE') THEN ', DATA_COMPRESSION = PAGE' ELSE '' END + ')' ELSE '' END;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Start', @Text = @Sql;
    SET @SizeBefore = (SELECT sum(reserved_page_count)
                       FROM   sys.dm_db_partition_stats
                       WHERE  object_id = object_id(@TableName)
                              AND index_id = @IndexId
                              AND partition_number = @PartitionNumber) * 8.0 / 1024 / 1024;
    SET @msg = 'Size[GB] before=' + CONVERT (VARCHAR, @SizeBefore);
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Run', @Text = @msg;
    BEGIN TRY
        EXECUTE (@Sql);
        SET @SizeAfter = (SELECT sum(reserved_page_count)
                          FROM   sys.dm_db_partition_stats
                          WHERE  object_id = object_id(@TableName)
                                 AND index_id = @IndexId
                                 AND partition_number = @PartitionNumber) * 8.0 / 1024 / 1024;
        SET @msg = 'Size[GB] before=' + CONVERT (VARCHAR, @SizeBefore) + ', after=' + CONVERT (VARCHAR, @SizeAfter) + ', reduced by=' + CONVERT (VARCHAR, @SizeBefore - @SizeAfter);
        EXECUTE dbo.LogEvent @Process = @SP, @Status = 'End', @Mode = @Mode, @Action = @Operation, @Start = @st, @Text = @msg;
    END TRY
    BEGIN CATCH
        EXECUTE dbo.LogEvent @Process = @SP, @Status = 'Error', @Mode = @Mode, @Action = @Operation, @Start = @st;
        THROW;
    END CATCH
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH

GO
CREATE PROCEDURE dbo.DefragChangeDatabaseSettings
@IsOn BIT
WITH EXECUTE AS 'dbo'
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'DefragChangeDatabaseSettings', @Mode AS VARCHAR (200) = 'On=' + CONVERT (VARCHAR, @IsOn), @st AS DATETIME = getUTCdate(), @SQL AS VARCHAR (3500);
BEGIN TRY
    EXECUTE dbo.LogEvent @Process = @SP, @Status = 'Start', @Mode = @Mode;
    SET @SQL = 'ALTER DATABASE CURRENT SET AUTO_UPDATE_STATISTICS ' + CASE WHEN @IsOn = 1 THEN 'ON' ELSE 'OFF' END;
    EXECUTE (@SQL);
    EXECUTE dbo.LogEvent @Process = @SP, @Status = 'Run', @Mode = @Mode, @Text = @SQL;
    SET @SQL = 'ALTER DATABASE CURRENT SET AUTO_CREATE_STATISTICS ' + CASE WHEN @IsOn = 1 THEN 'ON' ELSE 'OFF' END;
    EXECUTE (@SQL);
    EXECUTE dbo.LogEvent @Process = @SP, @Status = 'End', @Mode = @Mode, @Start = @st, @Text = @SQL;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH

GO
CREATE PROCEDURE dbo.DefragGetFragmentation
@TableName VARCHAR (200), @IndexName VARCHAR (200)=NULL, @PartitionNumber INT=NULL
WITH EXECUTE AS 'dbo'
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = object_name(@@procid), @st AS DATETIME = getUTCdate(), @msg AS VARCHAR (1000), @Rows AS INT, @MinFragPct AS INT = isnull((SELECT Number
                                                                                                                                                         FROM   dbo.Parameters
                                                                                                                                                         WHERE  Id = 'Defrag.MinFragPct'), 10), @MinSizeGB AS FLOAT = isnull((SELECT Number
                                                                                                                                                                                                                              FROM   dbo.Parameters
                                                                                                                                                                                                                              WHERE  Id = 'Defrag.MinSizeGB'), 0.1), @PreviousGroupId AS BIGINT, @IndexId AS INT;
DECLARE @Mode AS VARCHAR (200) = 'T=' + @TableName + ' I=' + isnull(@IndexName, 'NULL') + ' P=' + isnull(CONVERT (VARCHAR, @PartitionNumber), 'NULL') + ' MF=' + CONVERT (VARCHAR, @MinFragPct) + ' MS=' + CONVERT (VARCHAR, @MinSizeGB);
BEGIN TRY
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Start';
    IF object_id(@TableName) IS NULL
        RAISERROR ('Table does not exist', 18, 127);
    SET @IndexId = (SELECT index_id
                    FROM   sys.indexes
                    WHERE  object_id = object_id(@TableName)
                           AND name = @IndexName);
    IF @IndexName IS NOT NULL
       AND @IndexId IS NULL
        RAISERROR ('Index does not exist', 18, 127);
    SET @PreviousGroupId = (SELECT   TOP 1 GroupId
                            FROM     dbo.JobQueue
                            WHERE    QueueType = 3
                                     AND Status = 5
                                     AND Definition = @TableName
                            ORDER BY GroupId DESC);
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Run', @Target = '@PreviousGroupId', @Text = @PreviousGroupId;
    SELECT TableName,
           IndexName,
           partition_number,
           frag_in_percent
    FROM   (SELECT @TableName AS TableName,
                   I.name AS IndexName,
                   partition_number,
                   avg_fragmentation_in_percent AS frag_in_percent,
                   isnull(CONVERT (FLOAT, Result), 0) AS prev_frag_in_percent
            FROM   (SELECT object_id,
                           index_id,
                           partition_number,
                           avg_fragmentation_in_percent
                    FROM   sys.dm_db_index_physical_stats(db_id(), object_id(@TableName), @IndexId, @PartitionNumber, 'LIMITED') AS A
                    WHERE  index_id > 0
                           AND (@PartitionNumber IS NOT NULL
                                OR avg_fragmentation_in_percent >= @MinFragPct
                                   AND A.page_count > @MinSizeGB * 1024 * 1024 / 8)) AS A
                   INNER JOIN
                   sys.indexes AS I
                   ON I.object_id = A.object_id
                      AND I.index_id = A.index_id
                   LEFT OUTER JOIN
                   dbo.JobQueue
                   ON QueueType = 3
                      AND Status = 5
                      AND GroupId = @PreviousGroupId
                      AND Definition = I.name + ';' + CONVERT (VARCHAR, partition_number)) AS A
    WHERE  @PartitionNumber IS NOT NULL
           OR frag_in_percent >= prev_frag_in_percent + @MinFragPct;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @@rowcount;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH

GO
CREATE PROCEDURE dbo.DeleteHistory
@DeleteResources BIT=0, @Reset BIT=0, @DisableLogEvent BIT=0
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'DeleteHistory', @Mode AS VARCHAR (100) = 'D=' + isnull(CONVERT (VARCHAR, @DeleteResources), 'NULL') + ' R=' + isnull(CONVERT (VARCHAR, @Reset), 'NULL'), @st AS DATETIME = getUTCdate(), @Id AS VARCHAR (100) = 'DeleteHistory.LastProcessed.TypeId.SurrogateId', @ResourceTypeId AS SMALLINT, @SurrogateId AS BIGINT, @RowsToProcess AS INT, @ProcessedResources AS INT = 0, @DeletedResources AS INT = 0, @DeletedSearchParams AS INT = 0, @ReportDate AS DATETIME = getUTCdate();
BEGIN TRY
    IF @DisableLogEvent = 0
        INSERT INTO dbo.Parameters (Id, Char)
        SELECT @SP,
               'LogEvent';
    ELSE
        DELETE dbo.Parameters
        WHERE  Id = @SP;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Start';
    INSERT INTO dbo.Parameters (Id, Char)
    SELECT @Id,
           '0.0'
    WHERE  NOT EXISTS (SELECT *
                       FROM   dbo.Parameters
                       WHERE  Id = @Id);
    DECLARE @LastProcessed AS VARCHAR (100) = CASE WHEN @Reset = 0 THEN (SELECT Char
                                                                         FROM   dbo.Parameters
                                                                         WHERE  Id = @Id) ELSE '0.0' END;
    DECLARE @Types TABLE (
        ResourceTypeId SMALLINT      PRIMARY KEY,
        Name           VARCHAR (100));
    DECLARE @SurrogateIds TABLE (
        ResourceSurrogateId BIGINT PRIMARY KEY,
        IsHistory           BIT   );
    INSERT INTO @Types
    EXECUTE dbo.GetUsedResourceTypes ;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Run', @Target = '@Types', @Action = 'Insert', @Rows = @@rowcount;
    SET @ResourceTypeId = substring(@LastProcessed, 1, charindex('.', @LastProcessed) - 1);
    SET @SurrogateId = substring(@LastProcessed, charindex('.', @LastProcessed) + 1, 255);
    DELETE @Types
    WHERE  ResourceTypeId < @ResourceTypeId;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Run', @Target = '@Types', @Action = 'Delete', @Rows = @@rowcount;
    WHILE EXISTS (SELECT *
                  FROM   @Types)
        BEGIN
            SET @ResourceTypeId = (SELECT   TOP 1 ResourceTypeId
                                   FROM     @Types
                                   ORDER BY ResourceTypeId);
            SET @ProcessedResources = 0;
            SET @DeletedResources = 0;
            SET @DeletedSearchParams = 0;
            SET @RowsToProcess = 1;
            WHILE @RowsToProcess > 0
                BEGIN
                    DELETE @SurrogateIds;
                    INSERT INTO @SurrogateIds
                    SELECT   TOP 10000 ResourceSurrogateId,
                                       IsHistory
                    FROM     dbo.Resource
                    WHERE    ResourceTypeId = @ResourceTypeId
                             AND ResourceSurrogateId > @SurrogateId
                    ORDER BY ResourceSurrogateId;
                    SET @RowsToProcess = @@rowcount;
                    SET @ProcessedResources += @RowsToProcess;
                    IF @RowsToProcess > 0
                        SET @SurrogateId = (SELECT max(ResourceSurrogateId)
                                            FROM   @SurrogateIds);
                    SET @LastProcessed = CONVERT (VARCHAR, @ResourceTypeId) + '.' + CONVERT (VARCHAR, @SurrogateId);
                    DELETE @SurrogateIds
                    WHERE  IsHistory = 0;
                    IF EXISTS (SELECT *
                               FROM   @SurrogateIds)
                        BEGIN
                            DELETE dbo.ResourceWriteClaim
                            WHERE  ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                           FROM   @SurrogateIds);
                            SET @DeletedSearchParams += @@rowcount;
                            DELETE dbo.CompartmentAssignment
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @DeletedSearchParams += @@rowcount;
                            DELETE dbo.ReferenceSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @DeletedSearchParams += @@rowcount;
                            DELETE dbo.TokenSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @DeletedSearchParams += @@rowcount;
                            DELETE dbo.TokenText
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @DeletedSearchParams += @@rowcount;
                            DELETE dbo.StringSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @DeletedSearchParams += @@rowcount;
                            DELETE dbo.UriSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @DeletedSearchParams += @@rowcount;
                            DELETE dbo.NumberSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @DeletedSearchParams += @@rowcount;
                            DELETE dbo.QuantitySearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @DeletedSearchParams += @@rowcount;
                            DELETE dbo.DateTimeSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @DeletedSearchParams += @@rowcount;
                            DELETE dbo.ReferenceTokenCompositeSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @DeletedSearchParams += @@rowcount;
                            DELETE dbo.TokenTokenCompositeSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @DeletedSearchParams += @@rowcount;
                            DELETE dbo.TokenDateTimeCompositeSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @DeletedSearchParams += @@rowcount;
                            DELETE dbo.TokenQuantityCompositeSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @DeletedSearchParams += @@rowcount;
                            DELETE dbo.TokenStringCompositeSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @DeletedSearchParams += @@rowcount;
                            DELETE dbo.TokenNumberNumberCompositeSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @DeletedSearchParams += @@rowcount;
                            IF @DeleteResources = 1
                                BEGIN
                                    DELETE dbo.Resource
                                    WHERE  ResourceTypeId = @ResourceTypeId
                                           AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                                       FROM   @SurrogateIds);
                                    SET @DeletedResources += @@rowcount;
                                END
                        END
                    UPDATE dbo.Parameters
                    SET    Char = @LastProcessed
                    WHERE  Id = @Id;
                    IF datediff(second, @ReportDate, getUTCdate()) > 60
                        BEGIN
                            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Run', @Target = 'Resource', @Action = 'Select', @Rows = @ProcessedResources, @Text = @LastProcessed;
                            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Run', @Target = '*SearchParam', @Action = 'Delete', @Rows = @DeletedSearchParams, @Text = @LastProcessed;
                            IF @DeleteResources = 1
                                EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Run', @Target = 'Resource', @Action = 'Delete', @Rows = @DeletedResources, @Text = @LastProcessed;
                            SET @ReportDate = getUTCdate();
                            SET @ProcessedResources = 0;
                            SET @DeletedSearchParams = 0;
                            SET @DeletedResources = 0;
                        END
                END
            DELETE @Types
            WHERE  ResourceTypeId = @ResourceTypeId;
            SET @SurrogateId = 0;
        END
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Run', @Target = 'Resource', @Action = 'Select', @Rows = @ProcessedResources, @Text = @LastProcessed;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Run', @Target = '*SearchParam', @Action = 'Delete', @Rows = @DeletedSearchParams, @Text = @LastProcessed;
    IF @DeleteResources = 1
        EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Run', @Target = 'Resource', @Action = 'Delete', @Rows = @DeletedResources, @Text = @LastProcessed;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH

GO
CREATE PROCEDURE dbo.DequeueJob
@QueueType TINYINT, @Worker VARCHAR (100), @HeartbeatTimeoutSec INT, @InputJobId BIGINT=NULL, @CheckTimeoutJobs BIT=0
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'DequeueJob', @Mode AS VARCHAR (100) = 'Q=' + isnull(CONVERT (VARCHAR, @QueueType), 'NULL') + ' H=' + isnull(CONVERT (VARCHAR, @HeartbeatTimeoutSec), 'NULL') + ' W=' + isnull(@Worker, 'NULL') + ' IJ=' + isnull(CONVERT (VARCHAR, @InputJobId), 'NULL') + ' T=' + isnull(CONVERT (VARCHAR, @CheckTimeoutJobs), 'NULL'), @Rows AS INT = 0, @st AS DATETIME = getUTCdate(), @JobId AS BIGINT, @msg AS VARCHAR (100), @Lock AS VARCHAR (100), @PartitionId AS TINYINT, @MaxPartitions AS TINYINT = 16, @LookedAtPartitions AS TINYINT = 0;
BEGIN TRY
    IF EXISTS (SELECT *
               FROM   dbo.Parameters
               WHERE  Id = 'DequeueJobStop'
                      AND Number = 1)
        BEGIN
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = 0, @Text = 'Skipped';
            RETURN;
        END
    IF @InputJobId IS NULL
        SET @PartitionId = @MaxPartitions * rand();
    ELSE
        SET @PartitionId = @InputJobId % 16;
    SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
    WHILE @InputJobId IS NULL
          AND @JobId IS NULL
          AND @LookedAtPartitions < @MaxPartitions
          AND @CheckTimeoutJobs = 0
        BEGIN
            SET @Lock = 'DequeueJob_' + CONVERT (VARCHAR, @QueueType) + '_' + CONVERT (VARCHAR, @PartitionId);
            BEGIN TRANSACTION;
            EXECUTE sp_getapplock @Lock, 'Exclusive';
            UPDATE T
            SET    StartDate     = getUTCdate(),
                   HeartbeatDate = getUTCdate(),
                   Worker        = @Worker,
                   Status        = 1,
                   Version       = datediff_big(millisecond, '0001-01-01', getUTCdate()),
                   @JobId        = T.JobId
            FROM   dbo.JobQueue AS T WITH (PAGLOCK)
                   INNER JOIN
                   (SELECT   TOP 1 JobId
                    FROM     dbo.JobQueue WITH (INDEX (IX_QueueType_PartitionId_Status_Priority))
                    WHERE    QueueType = @QueueType
                             AND PartitionId = @PartitionId
                             AND Status = 0
                    ORDER BY Priority, JobId) AS S
                   ON QueueType = @QueueType
                      AND PartitionId = @PartitionId
                      AND T.JobId = S.JobId;
            SET @Rows += @@rowcount;
            COMMIT TRANSACTION;
            IF @JobId IS NULL
                BEGIN
                    SET @PartitionId = CASE WHEN @PartitionId = 15 THEN 0 ELSE @PartitionId + 1 END;
                    SET @LookedAtPartitions = @LookedAtPartitions + 1;
                END
        END
    SET @LookedAtPartitions = 0;
    WHILE @InputJobId IS NULL
          AND @JobId IS NULL
          AND @LookedAtPartitions < @MaxPartitions
        BEGIN
            SET @Lock = 'DequeueStoreCopyWorkUnit_' + CONVERT (VARCHAR, @PartitionId);
            BEGIN TRANSACTION;
            EXECUTE sp_getapplock @Lock, 'Exclusive';
            UPDATE T
            SET    StartDate     = getUTCdate(),
                   HeartbeatDate = getUTCdate(),
                   Worker        = @Worker,
                   Status        = CASE WHEN CancelRequested = 0 THEN 1 ELSE 4 END,
                   Version       = datediff_big(millisecond, '0001-01-01', getUTCdate()),
                   @JobId        = CASE WHEN CancelRequested = 0 THEN T.JobId END,
                   Info          = CONVERT (VARCHAR (1000), isnull(Info, '') + ' Prev: Worker=' + Worker + ' Start=' + CONVERT (VARCHAR, StartDate, 121))
            FROM   dbo.JobQueue AS T WITH (PAGLOCK)
                   INNER JOIN
                   (SELECT   TOP 1 JobId
                    FROM     dbo.JobQueue WITH (INDEX (IX_QueueType_PartitionId_Status_Priority))
                    WHERE    QueueType = @QueueType
                             AND PartitionId = @PartitionId
                             AND Status = 1
                             AND datediff(second, HeartbeatDate, getUTCdate()) > @HeartbeatTimeoutSec
                    ORDER BY Priority, JobId) AS S
                   ON QueueType = @QueueType
                      AND PartitionId = @PartitionId
                      AND T.JobId = S.JobId;
            SET @Rows += @@rowcount;
            COMMIT TRANSACTION;
            IF @JobId IS NULL
                BEGIN
                    SET @PartitionId = CASE WHEN @PartitionId = 15 THEN 0 ELSE @PartitionId + 1 END;
                    SET @LookedAtPartitions = @LookedAtPartitions + 1;
                END
        END
    IF @InputJobId IS NOT NULL
        BEGIN
            UPDATE dbo.JobQueue WITH (PAGLOCK)
            SET    StartDate     = getUTCdate(),
                   HeartbeatDate = getUTCdate(),
                   Worker        = @Worker,
                   Status        = 1,
                   Version       = datediff_big(millisecond, '0001-01-01', getUTCdate()),
                   @JobId        = JobId
            WHERE  QueueType = @QueueType
                   AND PartitionId = @PartitionId
                   AND Status = 0
                   AND JobId = @InputJobId;
            SET @Rows += @@rowcount;
            IF @JobId IS NULL
                BEGIN
                    UPDATE dbo.JobQueue WITH (PAGLOCK)
                    SET    StartDate     = getUTCdate(),
                           HeartbeatDate = getUTCdate(),
                           Worker        = @Worker,
                           Status        = 1,
                           Version       = datediff_big(millisecond, '0001-01-01', getUTCdate()),
                           @JobId        = JobId
                    WHERE  QueueType = @QueueType
                           AND PartitionId = @PartitionId
                           AND Status = 1
                           AND JobId = @InputJobId
                           AND datediff(second, HeartbeatDate, getUTCdate()) > @HeartbeatTimeoutSec;
                    SET @Rows += @@rowcount;
                END
        END
    IF @JobId IS NOT NULL
        EXECUTE dbo.GetJobs @QueueType = @QueueType, @JobId = @JobId;
    SET @msg = 'J=' + isnull(CONVERT (VARCHAR, @JobId), 'NULL') + ' P=' + CONVERT (VARCHAR, @PartitionId);
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @Rows, @Text = @msg;
END TRY
BEGIN CATCH
    IF @@trancount > 0
        ROLLBACK;
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH

GO
CREATE PROCEDURE dbo.DisableIndex
@tableName NVARCHAR (128), @indexName NVARCHAR (128)
WITH EXECUTE AS 'dbo'
AS
DECLARE @errorTxt AS VARCHAR (1000), @sql AS NVARCHAR (1000), @isDisabled AS BIT;
IF object_id(@tableName) IS NULL
    BEGIN
        SET @errorTxt = @tableName + ' does not exist or you don''t have permissions.';
        RAISERROR (@errorTxt, 18, 127);
    END
SET @isDisabled = (SELECT is_disabled
                   FROM   sys.indexes
                   WHERE  object_id = object_id(@tableName)
                          AND name = @indexName);
IF @isDisabled IS NULL
    BEGIN
        SET @errorTxt = @indexName + ' does not exist or you don''t have permissions.';
        RAISERROR (@errorTxt, 18, 127);
    END
IF @isDisabled = 0
    BEGIN
        SET @sql = N'ALTER INDEX ' + QUOTENAME(@indexName) + N' on ' + @tableName + ' Disable';
        EXECUTE sp_executesql @sql;
    END

GO
CREATE PROCEDURE dbo.DisableIndexes
WITH EXECUTE AS 'dbo'
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'DisableIndexes', @Mode AS VARCHAR (200) = '', @st AS DATETIME = getUTCdate(), @Tbl AS VARCHAR (100), @Ind AS VARCHAR (200), @Txt AS VARCHAR (4000);
BEGIN TRY
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Start';
    DECLARE @Tables TABLE (
        Tbl       VARCHAR (100) PRIMARY KEY,
        Supported BIT          );
    INSERT INTO @Tables
    EXECUTE dbo.GetPartitionedTables @IncludeNotDisabled = 1, @IncludeNotSupported = 0;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = '@Tables', @Action = 'Insert', @Rows = @@rowcount;
    DECLARE @Indexes TABLE (
        Tbl   VARCHAR (100),
        Ind   VARCHAR (200),
        TblId INT          ,
        IndId INT           PRIMARY KEY (Tbl, Ind));
    INSERT INTO @Indexes
    SELECT Tbl,
           I.Name,
           TblId,
           I.index_id
    FROM   (SELECT object_id(Tbl) AS TblId,
                   Tbl
            FROM   @Tables) AS O
           INNER JOIN
           sys.indexes AS I
           ON I.object_id = TblId;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = '@Indexes', @Action = 'Insert', @Rows = @@rowcount;
    INSERT INTO dbo.IndexProperties (TableName, IndexName, PropertyName, PropertyValue)
    SELECT Tbl,
           Ind,
           'DATA_COMPRESSION',
           data_comp
    FROM   (SELECT Tbl,
                   Ind,
                   isnull((SELECT TOP 1 CASE WHEN data_compression_desc = 'PAGE' THEN 'PAGE' END
                           FROM   sys.partitions
                           WHERE  object_id = TblId
                                  AND index_id = IndId), 'NONE') AS data_comp
            FROM   @Indexes) AS A
    WHERE  NOT EXISTS (SELECT *
                       FROM   dbo.IndexProperties
                       WHERE  TableName = Tbl
                              AND IndexName = Ind);
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = 'IndexProperties', @Action = 'Insert', @Rows = @@rowcount;
    DELETE @Indexes
    WHERE  Tbl = 'Resource'
           OR IndId = 1;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = '@Indexes', @Action = 'Delete', @Rows = @@rowcount;
    WHILE EXISTS (SELECT *
                  FROM   @Indexes)
        BEGIN
            SELECT TOP 1 @Tbl = Tbl,
                         @Ind = Ind
            FROM   @Indexes;
            SET @Txt = 'ALTER INDEX ' + @Ind + ' ON dbo.' + @Tbl + ' DISABLE';
            EXECUTE (@Txt);
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = @Ind, @Action = 'Disable', @Text = @Txt;
            DELETE @Indexes
            WHERE  Tbl = @Tbl
                   AND Ind = @Ind;
        END
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st;
    THROW;
END CATCH

GO
CREATE PROCEDURE dbo.EnqueueJobs
@QueueType TINYINT, @Definitions StringList READONLY, @GroupId BIGINT=NULL, @ForceOneActiveJobGroup BIT=1, @Status TINYINT=NULL, @Result VARCHAR (MAX)=NULL, @StartDate DATETIME=NULL, @ReturnJobs BIT=1
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'EnqueueJobs', @Mode AS VARCHAR (100) = 'Q=' + isnull(CONVERT (VARCHAR, @QueueType), 'NULL') + ' D=' + CONVERT (VARCHAR, (SELECT count(*)
                                                                                                                                                         FROM   @Definitions)) + ' G=' + isnull(CONVERT (VARCHAR, @GroupId), 'NULL') + ' F=' + isnull(CONVERT (VARCHAR, @ForceOneActiveJobGroup), 'NULL') + ' S=' + isnull(CONVERT (VARCHAR, @Status), 'NULL'), @st AS DATETIME = getUTCdate(), @Lock AS VARCHAR (100) = 'EnqueueJobs_' + CONVERT (VARCHAR, @QueueType), @MaxJobId AS BIGINT, @Rows AS INT, @msg AS VARCHAR (1000), @JobIds AS BigintList, @InputRows AS INT;
BEGIN TRY
    DECLARE @Input TABLE (
        DefinitionHash VARBINARY (20) PRIMARY KEY,
        Definition     VARCHAR (MAX) );
    INSERT INTO @Input
    SELECT hashbytes('SHA1', String) AS DefinitionHash,
           String AS Definition
    FROM   @Definitions;
    SET @InputRows = @@rowcount;
    INSERT INTO @JobIds
    SELECT JobId
    FROM   @Input AS A
           INNER JOIN
           dbo.JobQueue AS B
           ON B.QueueType = @QueueType
              AND B.DefinitionHash = A.DefinitionHash
              AND B.Status <> 5;
    IF @@rowcount < @InputRows
        BEGIN
            BEGIN TRANSACTION;
            EXECUTE sp_getapplock @Lock, 'Exclusive';
            IF @ForceOneActiveJobGroup = 1
               AND EXISTS (SELECT *
                           FROM   dbo.JobQueue
                           WHERE  QueueType = @QueueType
                                  AND Status IN (0, 1)
                                  AND (@GroupId IS NULL
                                       OR GroupId <> @GroupId))
                RAISERROR ('There are other active job groups', 18, 127);
            SET @MaxJobId = isnull((SELECT   TOP 1 JobId
                                    FROM     dbo.JobQueue
                                    WHERE    QueueType = @QueueType
                                    ORDER BY JobId DESC), 0);
            INSERT INTO dbo.JobQueue (QueueType, GroupId, JobId, Definition, DefinitionHash, Status, Result, StartDate, EndDate)
            OUTPUT inserted.JobId INTO @JobIds
            SELECT @QueueType,
                   isnull(@GroupId, @MaxJobId + 1) AS GroupId,
                   JobId,
                   Definition,
                   DefinitionHash,
                   isnull(@Status, 0) AS Status,
                   CASE WHEN @Status = 2 THEN @Result ELSE NULL END AS Result,
                   CASE WHEN @Status = 1 THEN getUTCdate() ELSE @StartDate END AS StartDate,
                   CASE WHEN @Status = 2 THEN getUTCdate() ELSE NULL END AS EndDate
            FROM   (SELECT @MaxJobId + row_number() OVER (ORDER BY Dummy) AS JobId,
                           *
                    FROM   (SELECT *,
                                   0 AS Dummy
                            FROM   @Input) AS A) AS A
            WHERE  NOT EXISTS (SELECT *
                               FROM   dbo.JobQueue AS B WITH (INDEX (IX_QueueType_DefinitionHash))
                               WHERE  B.QueueType = @QueueType
                                      AND B.DefinitionHash = A.DefinitionHash
                                      AND B.Status <> 5);
            SET @Rows = @@rowcount;
            COMMIT TRANSACTION;
        END
    IF @ReturnJobs = 1
        EXECUTE dbo.GetJobs @QueueType = @QueueType, @JobIds = @JobIds;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @Rows;
END TRY
BEGIN CATCH
    IF @@trancount > 0
        ROLLBACK;
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH

GO
CREATE PROCEDURE dbo.ExecuteCommandForRebuildIndexes
@Tbl VARCHAR (100), @Ind VARCHAR (1000), @Cmd VARCHAR (MAX)
WITH EXECUTE AS 'dbo'
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'ExecuteCommandForRebuildIndexes', @Mode AS VARCHAR (200) = 'Tbl=' + isnull(@Tbl, 'NULL'), @st AS DATETIME, @Retries AS INT = 0, @Action AS VARCHAR (100), @msg AS VARCHAR (1000);
RetryOnTempdbError:
BEGIN TRY
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Start', @Text = @Cmd;
    SET @st = getUTCdate();
    IF @Tbl IS NULL
        RAISERROR ('@Tbl IS NULL', 18, 127);
    IF @Cmd IS NULL
        RAISERROR ('@Cmd IS NULL', 18, 127);
    SET @Action = CASE WHEN @Cmd LIKE 'UPDATE STAT%' THEN 'Update statistics' WHEN @Cmd LIKE 'CREATE%INDEX%' THEN 'Create Index' WHEN @Cmd LIKE 'ALTER%INDEX%REBUILD%' THEN 'Rebuild Index' WHEN @Cmd LIKE 'ALTER%TABLE%ADD%' THEN 'Add Constraint' END;
    IF @Action IS NULL
        BEGIN
            SET @msg = 'Not supported command = ' + CONVERT (VARCHAR (900), @Cmd);
            RAISERROR (@msg, 18, 127);
        END
    IF @Action = 'Create Index'
        WAITFOR DELAY '00:00:05';
    EXECUTE (@Cmd);
    SELECT @Ind;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Action = @Action, @Status = 'End', @Start = @st, @Text = @Cmd;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    IF error_number() = 40544
        BEGIN
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st, @Retry = @Retries;
            SET @Retries = @Retries + 1;
            IF @Tbl = 'TokenText_96'
                WAITFOR DELAY '01:00:00';
            ELSE
                WAITFOR DELAY '00:10:00';
            GOTO RetryOnTempdbError;
        END
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st;
    THROW;
END CATCH

GO
CREATE OR ALTER PROCEDURE dbo.FetchEventAgentCheckpoint
@CheckpointId VARCHAR (64)
AS
BEGIN
    SELECT TOP (1) CheckpointId,
                   LastProcessedDateTime,
                   LastProcessedIdentifier
    FROM   dbo.EventAgentCheckpoint
    WHERE  CheckpointId = @CheckpointId;
END

GO
CREATE PROCEDURE dbo.FetchResourceChanges_3
@startId BIGINT, @lastProcessedUtcDateTime DATETIME2 (7), @pageSize SMALLINT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @precedingPartitionBoundary AS DATETIME2 (7) = (SELECT   TOP (1) CAST (prv.value AS DATETIME2 (7)) AS value
                                                            FROM     sys.partition_range_values AS prv WITH (NOLOCK)
                                                                     INNER JOIN
                                                                     sys.partition_functions AS pf WITH (NOLOCK)
                                                                     ON pf.function_id = prv.function_id
                                                            WHERE    pf.name = N'PartitionFunction_ResourceChangeData_Timestamp'
                                                                     AND SQL_VARIANT_PROPERTY(prv.Value, 'BaseType') = 'datetime2'
                                                                     AND CAST (prv.value AS DATETIME2 (7)) < DATEADD(HOUR, DATEDIFF(HOUR, 0, @lastProcessedUtcDateTime), 0)
                                                            ORDER BY prv.boundary_id DESC);
    IF (@precedingPartitionBoundary IS NULL)
        BEGIN
            SET @precedingPartitionBoundary = CONVERT (DATETIME2 (7), N'1970-01-01T00:00:00.0000000');
        END
    DECLARE @endDateTimeToFilter AS DATETIME2 (7) = DATEADD(HOUR, 1, SYSUTCDATETIME());
    WITH     PartitionBoundaries
    AS       (SELECT CAST (prv.value AS DATETIME2 (7)) AS PartitionBoundary
              FROM   sys.partition_range_values AS prv WITH (NOLOCK)
                     INNER JOIN
                     sys.partition_functions AS pf WITH (NOLOCK)
                     ON pf.function_id = prv.function_id
              WHERE  pf.name = N'PartitionFunction_ResourceChangeData_Timestamp'
                     AND SQL_VARIANT_PROPERTY(prv.Value, 'BaseType') = 'datetime2'
                     AND CAST (prv.value AS DATETIME2 (7)) BETWEEN @precedingPartitionBoundary AND @endDateTimeToFilter)
    SELECT   TOP (@pageSize) Id,
                             Timestamp,
                             ResourceId,
                             ResourceTypeId,
                             ResourceVersion,
                             ResourceChangeTypeId
    FROM     PartitionBoundaries AS p CROSS APPLY (SELECT   TOP (@pageSize) Id,
                                                                            Timestamp,
                                                                            ResourceId,
                                                                            ResourceTypeId,
                                                                            ResourceVersion,
                                                                            ResourceChangeTypeId
                                                   FROM     dbo.ResourceChangeData WITH (TABLOCK, HOLDLOCK)
                                                   WHERE    Id >= @startId
                                                            AND $PARTITION.PartitionFunction_ResourceChangeData_Timestamp (Timestamp) = $PARTITION.PartitionFunction_ResourceChangeData_Timestamp (p.PartitionBoundary)
                                                   ORDER BY Id ASC) AS rcd
    ORDER BY rcd.Id ASC;
END

GO
CREATE PROCEDURE dbo.GetActiveJobs
@QueueType TINYINT, @GroupId BIGINT=NULL
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'GetActiveJobs', @Mode AS VARCHAR (100) = 'Q=' + isnull(CONVERT (VARCHAR, @QueueType), 'NULL') + ' G=' + isnull(CONVERT (VARCHAR, @GroupId), 'NULL'), @st AS DATETIME = getUTCdate(), @JobIds AS BigintList, @PartitionId AS TINYINT, @MaxPartitions AS TINYINT = 16, @LookedAtPartitions AS TINYINT = 0, @Rows AS INT = 0;
BEGIN TRY
    SET @PartitionId = @MaxPartitions * rand();
    WHILE @LookedAtPartitions < @MaxPartitions
        BEGIN
            IF @GroupId IS NULL
                INSERT INTO @JobIds
                SELECT JobId
                FROM   dbo.JobQueue
                WHERE  PartitionId = @PartitionId
                       AND QueueType = @QueueType
                       AND Status IN (0, 1);
            ELSE
                INSERT INTO @JobIds
                SELECT JobId
                FROM   dbo.JobQueue
                WHERE  PartitionId = @PartitionId
                       AND QueueType = @QueueType
                       AND GroupId = @GroupId
                       AND Status IN (0, 1);
            SET @Rows += @@rowcount;
            SET @PartitionId = CASE WHEN @PartitionId = 15 THEN 0 ELSE @PartitionId + 1 END;
            SET @LookedAtPartitions += 1;
        END
    IF @Rows > 0
        EXECUTE dbo.GetJobs @QueueType = @QueueType, @JobIds = @JobIds;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @Rows;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH

GO
CREATE PROCEDURE dbo.GetCommandsForRebuildIndexes
@RebuildClustered BIT
WITH EXECUTE AS 'dbo'
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'GetCommandsForRebuildIndexes', @Mode AS VARCHAR (200) = 'PS=PartitionScheme_ResourceTypeId RC=' + isnull(CONVERT (VARCHAR, @RebuildClustered), 'NULL'), @st AS DATETIME = getUTCdate(), @Tbl AS VARCHAR (100), @TblInt AS VARCHAR (100), @Ind AS VARCHAR (200), @IndId AS INT, @Supported AS BIT, @Txt AS VARCHAR (MAX), @Rows AS BIGINT, @Pages AS BIGINT, @ResourceTypeId AS SMALLINT, @IndexesCnt AS INT, @DataComp AS VARCHAR (100);
BEGIN TRY
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Start';
    DECLARE @Commands TABLE (
        Tbl   VARCHAR (100),
        Ind   VARCHAR (200),
        Txt   VARCHAR (MAX),
        Pages BIGINT       );
    DECLARE @ResourceTypes TABLE (
        ResourceTypeId SMALLINT PRIMARY KEY);
    DECLARE @Indexes TABLE (
        Ind   VARCHAR (200) PRIMARY KEY,
        IndId INT          );
    DECLARE @Tables TABLE (
        name      VARCHAR (100) PRIMARY KEY,
        Supported BIT          );
    INSERT INTO @Tables
    EXECUTE dbo.GetPartitionedTables @IncludeNotDisabled = 1, @IncludeNotSupported = 1;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = '@Tables', @Action = 'Insert', @Rows = @@rowcount;
    WHILE EXISTS (SELECT *
                  FROM   @Tables)
        BEGIN
            SELECT   TOP 1 @Tbl = name,
                           @Supported = Supported
            FROM     @Tables
            ORDER BY name;
            IF @Supported = 0
                BEGIN
                    INSERT INTO @Commands
                    SELECT @Tbl,
                           name,
                           'ALTER INDEX ' + name + ' ON dbo.' + @Tbl + ' REBUILD' + CASE WHEN (SELECT PropertyValue
                                                                                               FROM   dbo.IndexProperties
                                                                                               WHERE  TableName = @Tbl
                                                                                                      AND IndexName = name) = 'PAGE' THEN ' PARTITION = ALL WITH (DATA_COMPRESSION = PAGE)' ELSE '' END,
                           CONVERT (BIGINT, 9e18)
                    FROM   sys.indexes
                    WHERE  object_id = object_id(@Tbl)
                           AND (is_disabled = 1
                                AND index_id > 1
                                AND @RebuildClustered = 0
                                OR index_id = 1
                                   AND @RebuildClustered = 1);
                    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = '@Commands', @Action = 'Insert', @Rows = @@rowcount, @Text = 'Not supported tables with disabled indexes';
                END
            ELSE
                BEGIN
                    DELETE @ResourceTypes;
                    INSERT INTO @ResourceTypes
                    SELECT CONVERT (SMALLINT, substring(name, charindex('_', name) + 1, 6)) AS ResourceTypeId
                    FROM   sys.sysobjects
                    WHERE  name LIKE @Tbl + '[_]%';
                    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = '@ResourceTypes', @Action = 'Insert', @Rows = @@rowcount;
                    WHILE EXISTS (SELECT *
                                  FROM   @ResourceTypes)
                        BEGIN
                            SET @ResourceTypeId = (SELECT   TOP 1 ResourceTypeId
                                                   FROM     @ResourceTypes
                                                   ORDER BY ResourceTypeId);
                            SET @TblInt = @Tbl + '_' + CONVERT (VARCHAR, @ResourceTypeId);
                            SET @Pages = (SELECT dpages
                                          FROM   sysindexes
                                          WHERE  id = object_id(@TblInt)
                                                 AND indid IN (0, 1));
                            DELETE @Indexes;
                            INSERT INTO @Indexes
                            SELECT name,
                                   index_id
                            FROM   sys.indexes
                            WHERE  object_id = object_id(@Tbl)
                                   AND (index_id > 1
                                        AND @RebuildClustered = 0
                                        OR index_id = 1
                                           AND @RebuildClustered = 1);
                            SET @IndexesCnt = 0;
                            WHILE EXISTS (SELECT *
                                          FROM   @Indexes)
                                BEGIN
                                    SELECT   TOP 1 @Ind = Ind,
                                                   @IndId = IndId
                                    FROM     @Indexes
                                    ORDER BY Ind;
                                    IF @IndId = 1
                                        BEGIN
                                            SET @Txt = 'ALTER INDEX ' + @Ind + ' ON dbo.' + @TblInt + ' REBUILD' + CASE WHEN (SELECT PropertyValue
                                                                                                                              FROM   dbo.IndexProperties
                                                                                                                              WHERE  TableName = @Tbl
                                                                                                                                     AND IndexName = @Ind) = 'PAGE' THEN ' PARTITION = ALL WITH (DATA_COMPRESSION = PAGE)' ELSE '' END;
                                            INSERT INTO @Commands
                                            SELECT @TblInt,
                                                   @Ind,
                                                   @Txt,
                                                   @Pages;
                                            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = @TblInt, @Action = 'Add command', @Rows = @@rowcount, @Text = @Txt;
                                        END
                                    ELSE
                                        IF NOT EXISTS (SELECT *
                                                       FROM   sys.indexes
                                                       WHERE  object_id = object_id(@TblInt)
                                                              AND name = @Ind)
                                            BEGIN
                                                EXECUTE dbo.GetIndexCommands @Tbl = @Tbl, @Ind = @Ind, @AddPartClause = 0, @IncludeClustered = 0, @Txt = @Txt OUTPUT;
                                                SET @Txt = replace(@Txt, '[' + @Tbl + ']', @TblInt);
                                                IF @Txt IS NOT NULL
                                                    BEGIN
                                                        SET @IndexesCnt = @IndexesCnt + 1;
                                                        INSERT INTO @Commands
                                                        SELECT @TblInt,
                                                               @Ind,
                                                               @Txt,
                                                               @Pages;
                                                        EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = @TblInt, @Action = 'Add command', @Rows = @@rowcount, @Text = @Txt;
                                                    END
                                            END
                                    DELETE @Indexes
                                    WHERE  Ind = @Ind;
                                END
                            IF @IndexesCnt > 1
                                BEGIN
                                    INSERT INTO @Commands
                                    SELECT @TblInt,
                                           'UPDATE STAT',
                                           'UPDATE STATISTICS dbo.' + @TblInt,
                                           @Pages;
                                    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = @TblInt, @Action = 'Add command', @Rows = @@rowcount, @Text = 'Add stats update';
                                END
                            DELETE @ResourceTypes
                            WHERE  ResourceTypeId = @ResourceTypeId;
                        END
                END
            DELETE @Tables
            WHERE  name = @Tbl;
        END
    SELECT   Tbl,
             Ind,
             Txt
    FROM     @Commands
    ORDER BY Pages DESC, Tbl, CASE WHEN Txt LIKE 'UPDATE STAT%' THEN 0 ELSE 1 END;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = '@Commands', @Action = 'Select', @Rows = @@rowcount;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st;
    THROW;
END CATCH

GO
CREATE OR ALTER PROCEDURE dbo.GetGeoReplicationLag
AS
BEGIN
    SET NOCOUNT ON;
    SELECT replication_state_desc,
           replication_lag_sec,
           last_replication
    FROM   sys.dm_geo_replication_link_status
    WHERE  role_desc = 'PRIMARY';
END

GO
CREATE PROCEDURE dbo.GetIndexCommands
@Tbl VARCHAR (100), @Ind VARCHAR (200), @AddPartClause BIT, @IncludeClustered BIT, @Txt VARCHAR (MAX)=NULL OUTPUT
WITH EXECUTE AS 'dbo'
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'GetIndexCommands', @Mode AS VARCHAR (200) = 'Tbl=' + isnull(@Tbl, 'NULL') + ' Ind=' + isnull(@Ind, 'NULL'), @st AS DATETIME = getUTCdate();
DECLARE @Indexes TABLE (
    Ind VARCHAR (200) PRIMARY KEY,
    Txt VARCHAR (MAX));
BEGIN TRY
    IF @Tbl IS NULL
        RAISERROR ('@Tbl IS NULL', 18, 127);
    INSERT INTO @Indexes
    SELECT Ind,
           CASE WHEN is_primary_key = 1 THEN 'ALTER TABLE dbo.[' + Tbl + '] ADD PRIMARY KEY ' + CASE WHEN type = 1 THEN ' CLUSTERED' ELSE '' END ELSE 'CREATE' + CASE WHEN is_unique = 1 THEN ' UNIQUE' ELSE '' END + CASE WHEN type = 1 THEN ' CLUSTERED' ELSE '' END + ' INDEX ' + Ind + ' ON dbo.[' + Tbl + ']' END + ' (' + KeyCols + ')' + IncClause + CASE WHEN filter_def IS NOT NULL THEN ' WHERE ' + filter_def ELSE '' END + CASE WHEN data_comp IS NOT NULL THEN ' WITH (DATA_COMPRESSION = ' + data_comp + ')' ELSE '' END + CASE WHEN @AddPartClause = 1 THEN PartClause ELSE '' END
    FROM   (SELECT O.Name AS Tbl,
                   I.Name AS Ind,
                   isnull((SELECT TOP 1 CASE WHEN data_compression_desc = 'PAGE' THEN 'PAGE' END
                           FROM   sys.partitions AS P
                           WHERE  P.object_id = I.object_id
                                  AND I.index_id = P.index_id), (SELECT NULLIF (PropertyValue, 'NONE')
                                                                 FROM   dbo.IndexProperties
                                                                 WHERE  TableName = O.Name
                                                                        AND IndexName = I.Name
                                                                        AND PropertyName = 'DATA_COMPRESSION')) AS data_comp,
                   replace(replace(replace(replace(I.filter_definition, '[', ''), ']', ''), '(', ''), ')', '') AS filter_def,
                   I.is_unique,
                   I.is_primary_key,
                   I.type,
                   KeyCols,
                   CASE WHEN IncCols IS NOT NULL THEN ' INCLUDE (' + IncCols + ')' ELSE '' END AS IncClause,
                   CASE WHEN EXISTS (SELECT *
                                     FROM   sys.partition_schemes AS S
                                     WHERE  S.data_space_id = I.data_space_id
                                            AND name = 'PartitionScheme_ResourceTypeId') THEN ' ON PartitionScheme_ResourceTypeId (ResourceTypeId)' ELSE '' END AS PartClause
            FROM   sys.indexes AS I
                   INNER JOIN
                   sys.objects AS O
                   ON O.object_id = I.object_id CROSS APPLY (SELECT   string_agg(CASE WHEN IC.key_ordinal > 0
                                                                                           AND IC.is_included_column = 0 THEN C.name END, ',') WITHIN GROUP (ORDER BY key_ordinal) AS KeyCols,
                                                                      string_agg(CASE WHEN IC.is_included_column = 1 THEN C.name END, ',') WITHIN GROUP (ORDER BY key_ordinal) AS IncCols
                                                             FROM     sys.index_columns AS IC
                                                                      INNER JOIN
                                                                      sys.columns AS C
                                                                      ON C.object_id = IC.object_id
                                                                         AND C.column_id = IC.column_id
                                                             WHERE    IC.object_id = I.object_id
                                                                      AND IC.index_id = I.index_id
                                                             GROUP BY IC.object_id, IC.index_id) AS IC
            WHERE  O.name = @Tbl
                   AND (@Ind IS NULL
                        OR I.name = @Ind)
                   AND (@IncludeClustered = 1
                        OR index_id > 1)) AS A;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = '@Indexes', @Action = 'Insert', @Rows = @@rowcount;
    IF @Ind IS NULL
        SELECT Ind,
               Txt
        FROM   @Indexes;
    ELSE
        SET @Txt = (SELECT Txt
                    FROM   @Indexes);
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Text = @Txt;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st;
    THROW;
END CATCH

GO
CREATE PROCEDURE dbo.GetJobs
@QueueType TINYINT, @JobId BIGINT=NULL, @JobIds BigintList READONLY, @GroupId BIGINT=NULL, @ReturnDefinition BIT=1
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'GetJobs', @Mode AS VARCHAR (100) = 'Q=' + isnull(CONVERT (VARCHAR, @QueueType), 'NULL') + ' J=' + isnull(CONVERT (VARCHAR, @JobId), 'NULL') + ' G=' + isnull(CONVERT (VARCHAR, @GroupId), 'NULL'), @st AS DATETIME = getUTCdate(), @PartitionId AS TINYINT = @JobId % 16;
BEGIN TRY
    IF @JobId IS NULL
       AND @GroupId IS NULL
       AND NOT EXISTS (SELECT *
                       FROM   @JobIds)
        RAISERROR ('@JobId = NULL and @GroupId = NULL and @JobIds is empty', 18, 127);
    IF @JobId IS NOT NULL
        SELECT GroupId,
               JobId,
               CASE WHEN @ReturnDefinition = 1 THEN Definition ELSE NULL END AS Definition,
               Version,
               Status,
               Priority,
               Data,
               Result,
               CreateDate,
               StartDate,
               EndDate,
               HeartbeatDate,
               CancelRequested
        FROM   dbo.JobQueue
        WHERE  QueueType = @QueueType
               AND PartitionId = @PartitionId
               AND JobId = isnull(@JobId, -1)
               AND Status <> 5;
    ELSE
        IF @GroupId IS NOT NULL
            SELECT GroupId,
                   JobId,
                   CASE WHEN @ReturnDefinition = 1 THEN Definition ELSE NULL END AS Definition,
                   Version,
                   Status,
                   Priority,
                   Data,
                   Result,
                   CreateDate,
                   StartDate,
                   EndDate,
                   HeartbeatDate,
                   CancelRequested
            FROM   dbo.JobQueue WITH (INDEX (IX_QueueType_GroupId))
            WHERE  QueueType = @QueueType
                   AND GroupId = isnull(@GroupId, -1)
                   AND Status <> 5;
        ELSE
            SELECT GroupId,
                   JobId,
                   CASE WHEN @ReturnDefinition = 1 THEN Definition ELSE NULL END AS Definition,
                   Version,
                   Status,
                   Priority,
                   Data,
                   Result,
                   CreateDate,
                   StartDate,
                   EndDate,
                   HeartbeatDate,
                   CancelRequested
            FROM   dbo.JobQueue
            WHERE  QueueType = @QueueType
                   AND JobId IN (SELECT Id
                                 FROM   @JobIds)
                   AND PartitionId = JobId % 16
                   AND Status <> 5;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @@rowcount;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH

GO
CREATE PROCEDURE dbo.GetPartitionedTables
@IncludeNotDisabled BIT=1, @IncludeNotSupported BIT=1
WITH EXECUTE AS 'dbo'
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'GetPartitionedTables', @Mode AS VARCHAR (200) = 'PS=PartitionScheme_ResourceTypeId D=' + isnull(CONVERT (VARCHAR, @IncludeNotDisabled), 'NULL') + ' S=' + isnull(CONVERT (VARCHAR, @IncludeNotSupported), 'NULL'), @st AS DATETIME = getUTCdate();
DECLARE @NotSupportedTables TABLE (
    id INT PRIMARY KEY);
BEGIN TRY
    INSERT INTO @NotSupportedTables
    SELECT DISTINCT O.object_id
    FROM   sys.indexes AS I
           INNER JOIN
           sys.objects AS O
           ON O.object_id = I.object_id
    WHERE  O.type = 'u'
           AND EXISTS (SELECT *
                       FROM   sys.partition_schemes AS PS
                       WHERE  PS.data_space_id = I.data_space_id
                              AND name = 'PartitionScheme_ResourceTypeId')
           AND (NOT EXISTS (SELECT *
                            FROM   sys.index_columns AS IC
                                   INNER JOIN
                                   sys.columns AS C
                                   ON C.object_id = IC.object_id
                                      AND C.column_id = IC.column_id
                            WHERE  IC.object_id = I.object_id
                                   AND IC.index_id = I.index_id
                                   AND IC.key_ordinal > 0
                                   AND IC.is_included_column = 0
                                   AND C.name = 'ResourceTypeId')
                OR EXISTS (SELECT *
                           FROM   sys.indexes AS NSI
                           WHERE  NSI.object_id = O.object_id
                                  AND NOT EXISTS (SELECT *
                                                  FROM   sys.partition_schemes AS PS
                                                  WHERE  PS.data_space_id = NSI.data_space_id
                                                         AND name = 'PartitionScheme_ResourceTypeId')));
    SELECT   CONVERT (VARCHAR (100), O.name),
             CONVERT (BIT, CASE WHEN EXISTS (SELECT *
                                             FROM   @NotSupportedTables AS NSI
                                             WHERE  NSI.id = O.object_id) THEN 0 ELSE 1 END)
    FROM     sys.indexes AS I
             INNER JOIN
             sys.objects AS O
             ON O.object_id = I.object_id
    WHERE    O.type = 'u'
             AND I.index_id IN (0, 1)
             AND EXISTS (SELECT *
                         FROM   sys.partition_schemes AS PS
                         WHERE  PS.data_space_id = I.data_space_id
                                AND name = 'PartitionScheme_ResourceTypeId')
             AND EXISTS (SELECT *
                         FROM   sys.index_columns AS IC
                                INNER JOIN
                                sys.columns AS C
                                ON C.object_id = I.object_id
                                   AND C.column_id = IC.column_id
                                   AND IC.is_included_column = 0
                                   AND C.name = 'ResourceTypeId')
             AND (@IncludeNotSupported = 1
                  OR NOT EXISTS (SELECT *
                                 FROM   @NotSupportedTables AS NSI
                                 WHERE  NSI.id = O.object_id))
             AND (@IncludeNotDisabled = 1
                  OR EXISTS (SELECT *
                             FROM   sys.indexes AS D
                             WHERE  D.object_id = O.object_id
                                    AND D.is_disabled = 1))
    ORDER BY 1;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @@rowcount;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st;
    THROW;
END CATCH

GO
CREATE PROCEDURE dbo.GetReindexJobById
@id VARCHAR (64)
AS
SET NOCOUNT ON;
SELECT RawJobRecord,
       JobVersion
FROM   dbo.ReindexJob
WHERE  Id = @id;

GO
CREATE PROCEDURE dbo.GetResources
@ResourceKeys dbo.ResourceKeyList READONLY
AS
SET NOCOUNT ON;
DECLARE @st AS DATETIME = getUTCdate(), @SP AS VARCHAR (100) = 'GetResources', @InputRows AS INT, @DummyTop AS BIGINT = 9223372036854775807, @NotNullVersionExists AS BIT, @NullVersionExists AS BIT, @MinRT AS SMALLINT, @MaxRT AS SMALLINT;
SELECT @MinRT = min(ResourceTypeId),
       @MaxRT = max(ResourceTypeId),
       @InputRows = count(*),
       @NotNullVersionExists = max(CASE WHEN Version IS NOT NULL THEN 1 ELSE 0 END),
       @NullVersionExists = max(CASE WHEN Version IS NULL THEN 1 ELSE 0 END)
FROM   @ResourceKeys;
DECLARE @Mode AS VARCHAR (100) = 'RT=[' + CONVERT (VARCHAR, @MinRT) + ',' + CONVERT (VARCHAR, @MaxRT) + '] Cnt=' + CONVERT (VARCHAR, @InputRows) + ' NNVE=' + CONVERT (VARCHAR, @NotNullVersionExists) + ' NVE=' + CONVERT (VARCHAR, @NullVersionExists);
BEGIN TRY
    IF @NotNullVersionExists = 1
        IF @NullVersionExists = 0
            SELECT B.ResourceTypeId,
                   B.ResourceId,
                   ResourceSurrogateId,
                   B.Version,
                   IsDeleted,
                   IsHistory,
                   RawResource,
                   IsRawResourceMetaSet,
                   SearchParamHash
            FROM   (SELECT TOP (@DummyTop) *
                    FROM   @ResourceKeys) AS A
                   INNER JOIN
                   dbo.Resource AS B WITH (INDEX (IX_Resource_ResourceTypeId_ResourceId_Version))
                   ON B.ResourceTypeId = A.ResourceTypeId
                      AND B.ResourceId = A.ResourceId
                      AND B.Version = A.Version
            OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1));
        ELSE
            SELECT *
            FROM   (SELECT B.ResourceTypeId,
                           B.ResourceId,
                           ResourceSurrogateId,
                           B.Version,
                           IsDeleted,
                           IsHistory,
                           RawResource,
                           IsRawResourceMetaSet,
                           SearchParamHash
                    FROM   (SELECT TOP (@DummyTop) *
                            FROM   @ResourceKeys
                            WHERE  Version IS NOT NULL) AS A
                           INNER JOIN
                           dbo.Resource AS B WITH (INDEX (IX_Resource_ResourceTypeId_ResourceId_Version))
                           ON B.ResourceTypeId = A.ResourceTypeId
                              AND B.ResourceId = A.ResourceId
                              AND B.Version = A.Version
                    UNION ALL
                    SELECT B.ResourceTypeId,
                           B.ResourceId,
                           ResourceSurrogateId,
                           B.Version,
                           IsDeleted,
                           IsHistory,
                           RawResource,
                           IsRawResourceMetaSet,
                           SearchParamHash
                    FROM   (SELECT TOP (@DummyTop) *
                            FROM   @ResourceKeys
                            WHERE  Version IS NULL) AS A
                           INNER JOIN
                           dbo.Resource AS B WITH (INDEX (IX_Resource_ResourceTypeId_ResourceId))
                           ON B.ResourceTypeId = A.ResourceTypeId
                              AND B.ResourceId = A.ResourceId
                    WHERE  IsHistory = 0) AS A
            OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1));
    ELSE
        SELECT B.ResourceTypeId,
               B.ResourceId,
               ResourceSurrogateId,
               B.Version,
               IsDeleted,
               IsHistory,
               RawResource,
               IsRawResourceMetaSet,
               SearchParamHash
        FROM   (SELECT TOP (@DummyTop) *
                FROM   @ResourceKeys) AS A
               INNER JOIN
               dbo.Resource AS B WITH (INDEX (IX_Resource_ResourceTypeId_ResourceId))
               ON B.ResourceTypeId = A.ResourceTypeId
                  AND B.ResourceId = A.ResourceId
        WHERE  IsHistory = 0
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1));
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @@rowcount;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st;
    THROW;
END CATCH

GO
CREATE PROCEDURE dbo.GetResourcesByTransactionId
@TransactionId BIGINT, @IncludeHistory BIT=0, @ReturnResourceKeysOnly BIT=0
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = object_name(@@procid), @Mode AS VARCHAR (100) = 'T=' + CONVERT (VARCHAR, @TransactionId) + ' H=' + CONVERT (VARCHAR, @IncludeHistory), @st AS DATETIME = getUTCdate(), @DummyTop AS BIGINT = 9223372036854775807, @TypeId AS SMALLINT;
BEGIN TRY
    DECLARE @Types TABLE (
        TypeId SMALLINT      PRIMARY KEY,
        Name   VARCHAR (100));
    INSERT INTO @Types
    EXECUTE dbo.GetUsedResourceTypes ;
    DECLARE @Keys TABLE (
        TypeId      SMALLINT,
        SurrogateId BIGINT   PRIMARY KEY (TypeId, SurrogateId));
    WHILE EXISTS (SELECT *
                  FROM   @Types)
        BEGIN
            SET @TypeId = (SELECT   TOP 1 TypeId
                           FROM     @Types
                           ORDER BY TypeId);
            INSERT INTO @Keys
            SELECT @TypeId,
                   ResourceSurrogateId
            FROM   dbo.Resource
            WHERE  ResourceTypeId = @TypeId
                   AND TransactionId = @TransactionId;
            DELETE @Types
            WHERE  TypeId = @TypeId;
        END
    IF @ReturnResourceKeysOnly = 0
        SELECT ResourceTypeId,
               ResourceId,
               ResourceSurrogateId,
               Version,
               IsDeleted,
               IsHistory,
               RawResource,
               IsRawResourceMetaSet,
               SearchParamHash,
               RequestMethod
        FROM   (SELECT TOP (@DummyTop) *
                FROM   @Keys) AS A
               INNER JOIN
               dbo.Resource AS B
               ON ResourceTypeId = TypeId
                  AND ResourceSurrogateId = SurrogateId
        WHERE  IsHistory = 0
               OR @IncludeHistory = 1
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1));
    ELSE
        SELECT ResourceTypeId,
               ResourceId,
               ResourceSurrogateId,
               Version,
               IsDeleted
        FROM   (SELECT TOP (@DummyTop) *
                FROM   @Keys) AS A
               INNER JOIN
               dbo.Resource AS B
               ON ResourceTypeId = TypeId
                  AND ResourceSurrogateId = SurrogateId
        WHERE  IsHistory = 0
               OR @IncludeHistory = 1
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1));
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @@rowcount;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH

GO
CREATE PROCEDURE dbo.GetResourcesByTypeAndSurrogateIdRange
@ResourceTypeId SMALLINT, @StartId BIGINT, @EndId BIGINT, @GlobalEndId BIGINT=NULL, @IncludeHistory BIT=0, @IncludeDeleted BIT=0
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'GetResourcesByTypeAndSurrogateIdRange', @Mode AS VARCHAR (100) = 'RT=' + isnull(CONVERT (VARCHAR, @ResourceTypeId), 'NULL') + ' S=' + isnull(CONVERT (VARCHAR, @StartId), 'NULL') + ' E=' + isnull(CONVERT (VARCHAR, @EndId), 'NULL') + ' GE=' + isnull(CONVERT (VARCHAR, @GlobalEndId), 'NULL') + ' HI=' + isnull(CONVERT (VARCHAR, @IncludeHistory), 'NULL') + ' DE' + isnull(CONVERT (VARCHAR, @IncludeDeleted), 'NULL'), @st AS DATETIME = getUTCdate(), @DummyTop AS BIGINT = 9223372036854775807;
BEGIN TRY
    DECLARE @ResourceIds TABLE (
        ResourceId VARCHAR (64) COLLATE Latin1_General_100_CS_AS PRIMARY KEY);
    DECLARE @SurrogateIds TABLE (
        MaxSurrogateId BIGINT PRIMARY KEY);
    IF @GlobalEndId IS NOT NULL
       AND @IncludeHistory = 0
        BEGIN
            INSERT INTO @ResourceIds
            SELECT DISTINCT ResourceId
            FROM   dbo.Resource
            WHERE  ResourceTypeId = @ResourceTypeId
                   AND ResourceSurrogateId BETWEEN @StartId AND @EndId
                   AND IsHistory = 1
                   AND (IsDeleted = 0
                        OR @IncludeDeleted = 1)
            OPTION (MAXDOP 1);
            IF @@rowcount > 0
                INSERT INTO @SurrogateIds
                SELECT ResourceSurrogateId
                FROM   (SELECT ResourceId,
                               ResourceSurrogateId,
                               row_number() OVER (PARTITION BY ResourceId ORDER BY ResourceSurrogateId DESC) AS RowId
                        FROM   dbo.Resource WITH (INDEX (IX_Resource_ResourceTypeId_ResourceId_Version))
                        WHERE  ResourceTypeId = @ResourceTypeId
                               AND ResourceId IN (SELECT TOP (@DummyTop) ResourceId
                                                  FROM   @ResourceIds)
                               AND ResourceSurrogateId BETWEEN @StartId AND @GlobalEndId) AS A
                WHERE  RowId = 1
                       AND ResourceSurrogateId BETWEEN @StartId AND @EndId
                OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1));
        END
    SELECT ResourceTypeId,
           ResourceId,
           Version,
           IsDeleted,
           ResourceSurrogateId,
           RequestMethod,
           CONVERT (BIT, 1) AS IsMatch,
           CONVERT (BIT, 0) AS IsPartial,
           IsRawResourceMetaSet,
           SearchParamHash,
           RawResource
    FROM   dbo.Resource
    WHERE  ResourceTypeId = @ResourceTypeId
           AND ResourceSurrogateId BETWEEN @StartId AND @EndId
           AND (IsHistory = 0
                OR @IncludeHistory = 1)
           AND (IsDeleted = 0
                OR @IncludeDeleted = 1)
    UNION ALL
    SELECT ResourceTypeId,
           ResourceId,
           Version,
           IsDeleted,
           ResourceSurrogateId,
           RequestMethod,
           CONVERT (BIT, 1) AS IsMatch,
           CONVERT (BIT, 0) AS IsPartial,
           IsRawResourceMetaSet,
           SearchParamHash,
           RawResource
    FROM   @SurrogateIds
           INNER JOIN
           dbo.Resource
           ON ResourceTypeId = @ResourceTypeId
              AND ResourceSurrogateId = MaxSurrogateId
    WHERE  IsHistory = 1
           AND (IsDeleted = 0
                OR @IncludeDeleted = 1)
    OPTION (MAXDOP 1);
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @@rowcount;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH

GO
CREATE PROCEDURE dbo.GetResourceSearchParamStats
@Table VARCHAR (100)=NULL, @ResourceTypeId SMALLINT=NULL, @SearchParamId SMALLINT=NULL
WITH EXECUTE AS 'dbo'
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = object_name(@@procid), @Mode AS VARCHAR (200) = 'T=' + isnull(@Table, 'NULL') + ' RT=' + isnull(CONVERT (VARCHAR, @ResourceTypeId), 'NULL') + ' SP=' + isnull(CONVERT (VARCHAR, @SearchParamId), 'NULL'), @st AS DATETIME = getUTCdate();
BEGIN TRY
    SELECT T.name AS TableName,
           S.name AS StatsName,
           db_name() AS DatabaseName
    FROM   sys.stats AS S
           INNER JOIN
           sys.tables AS T
           ON T.object_id = S.object_id
    WHERE  T.name LIKE '%SearchParam'
           AND T.name <> 'SearchParam'
           AND S.name LIKE 'ST[_]%'
           AND (T.name LIKE @Table
                OR @Table IS NULL)
           AND (S.name LIKE '%ResourceTypeId[_]' + CONVERT (VARCHAR, @ResourceTypeId) + '[_]%'
                OR @ResourceTypeId IS NULL)
           AND (S.name LIKE '%SearchParamId[_]' + CONVERT (VARCHAR, @SearchParamId)
                OR @SearchParamId IS NULL);
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Rows = @@rowcount, @Start = @st;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st;
    THROW;
END CATCH

GO
CREATE PROCEDURE dbo.GetResourceSurrogateIdRanges
@ResourceTypeId SMALLINT, @StartId BIGINT, @EndId BIGINT, @RangeSize INT, @NumberOfRanges INT=100, @Up BIT=1
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'GetResourceSurrogateIdRanges', @Mode AS VARCHAR (100) = 'RT=' + isnull(CONVERT (VARCHAR, @ResourceTypeId), 'NULL') + ' S=' + isnull(CONVERT (VARCHAR, @StartId), 'NULL') + ' E=' + isnull(CONVERT (VARCHAR, @EndId), 'NULL') + ' R=' + isnull(CONVERT (VARCHAR, @RangeSize), 'NULL') + ' UP=' + isnull(CONVERT (VARCHAR, @Up), 'NULL'), @st AS DATETIME = getUTCdate();
BEGIN TRY
    IF @Up = 1
        SELECT   RangeId,
                 min(ResourceSurrogateId),
                 max(ResourceSurrogateId),
                 count(*)
        FROM     (SELECT isnull(CONVERT (INT, (row_number() OVER (ORDER BY ResourceSurrogateId) - 1) / @RangeSize), 0) AS RangeId,
                         ResourceSurrogateId
                  FROM   (SELECT   TOP (@RangeSize * @NumberOfRanges) ResourceSurrogateId
                          FROM     dbo.Resource
                          WHERE    ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId >= @StartId
                                   AND ResourceSurrogateId <= @EndId
                          ORDER BY ResourceSurrogateId) AS A) AS A
        GROUP BY RangeId
        OPTION (MAXDOP 1);
    ELSE
        SELECT   RangeId,
                 min(ResourceSurrogateId),
                 max(ResourceSurrogateId),
                 count(*)
        FROM     (SELECT isnull(CONVERT (INT, (row_number() OVER (ORDER BY ResourceSurrogateId) - 1) / @RangeSize), 0) AS RangeId,
                         ResourceSurrogateId
                  FROM   (SELECT   TOP (@RangeSize * @NumberOfRanges) ResourceSurrogateId
                          FROM     dbo.Resource
                          WHERE    ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId >= @StartId
                                   AND ResourceSurrogateId <= @EndId
                          ORDER BY ResourceSurrogateId DESC) AS A) AS A
        GROUP BY RangeId
        OPTION (MAXDOP 1);
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @@rowcount;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH

GO
CREATE PROCEDURE dbo.GetResourceVersions
@ResourceDateKeys dbo.ResourceDateKeyList READONLY
AS
SET NOCOUNT ON;
DECLARE @st AS DATETIME = getUTCdate(), @SP AS VARCHAR (100) = 'GetResourceVersions', @Mode AS VARCHAR (100) = 'Rows=' + CONVERT (VARCHAR, (SELECT count(*)
                                                                                                                                            FROM   @ResourceDateKeys)), @DummyTop AS BIGINT = 9223372036854775807;
BEGIN TRY
    SELECT A.ResourceTypeId,
           A.ResourceId,
           A.ResourceSurrogateId,
           CASE WHEN D.Version IS NOT NULL THEN 0 WHEN isnull(U.Version, 1) - isnull(L.Version, 0) > ResourceIndex THEN isnull(U.Version, 1) - ResourceIndex ELSE isnull(M.Version, 0) - ResourceIndex END AS Version,
           isnull(D.Version, 0) AS MatchedVersion,
           D.RawResource AS MatchedRawResource
    FROM   (SELECT TOP (@DummyTop) *,
                                   CONVERT (INT, row_number() OVER (PARTITION BY ResourceTypeId, ResourceId ORDER BY ResourceSurrogateId DESC)) AS ResourceIndex
            FROM   @ResourceDateKeys) AS A OUTER APPLY (SELECT   TOP 1 *
                                                        FROM     dbo.Resource AS B WITH (INDEX (IX_Resource_ResourceTypeId_ResourceId_Version))
                                                        WHERE    B.ResourceTypeId = A.ResourceTypeId
                                                                 AND B.ResourceId = A.ResourceId
                                                                 AND B.Version > 0
                                                                 AND B.ResourceSurrogateId < A.ResourceSurrogateId
                                                        ORDER BY B.ResourceSurrogateId DESC) AS L OUTER APPLY (SELECT   TOP 1 *
                                                                                                               FROM     dbo.Resource AS B WITH (INDEX (IX_Resource_ResourceTypeId_ResourceId_Version))
                                                                                                               WHERE    B.ResourceTypeId = A.ResourceTypeId
                                                                                                                        AND B.ResourceId = A.ResourceId
                                                                                                                        AND B.Version > 0
                                                                                                                        AND B.ResourceSurrogateId > A.ResourceSurrogateId
                                                                                                               ORDER BY B.ResourceSurrogateId) AS U OUTER APPLY (SELECT   TOP 1 *
                                                                                                                                                                 FROM     dbo.Resource AS B WITH (INDEX (IX_Resource_ResourceTypeId_ResourceId_Version))
                                                                                                                                                                 WHERE    B.ResourceTypeId = A.ResourceTypeId
                                                                                                                                                                          AND B.ResourceId = A.ResourceId
                                                                                                                                                                          AND B.Version < 0
                                                                                                                                                                 ORDER BY B.Version) AS M OUTER APPLY (SELECT TOP 1 *
                                                                                                                                                                                                       FROM   dbo.Resource AS B WITH (INDEX (IX_Resource_ResourceTypeId_ResourceId_Version))
                                                                                                                                                                                                       WHERE  B.ResourceTypeId = A.ResourceTypeId
                                                                                                                                                                                                              AND B.ResourceId = A.ResourceId
                                                                                                                                                                                                              AND B.ResourceSurrogateId BETWEEN A.ResourceSurrogateId AND A.ResourceSurrogateId + 79999) AS D
    OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1));
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @@rowcount;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st;
    THROW;
END CATCH

GO
CREATE PROCEDURE dbo.GetSearchParamStatuses
AS
SET NOCOUNT ON;
SELECT SearchParamId,
       Uri,
       Status,
       LastUpdated,
       IsPartiallySupported
FROM   dbo.SearchParam;

GO
CREATE PROCEDURE dbo.GetTransactions
@StartNotInclusiveTranId BIGINT, @EndInclusiveTranId BIGINT, @EndDate DATETIME=NULL
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = object_name(@@procid), @Mode AS VARCHAR (100) = 'ST=' + CONVERT (VARCHAR, @StartNotInclusiveTranId) + ' ET=' + CONVERT (VARCHAR, @EndInclusiveTranId) + ' ED=' + isnull(CONVERT (VARCHAR, @EndDate, 121), 'NULL'), @st AS DATETIME = getUTCdate();
IF @EndDate IS NULL
    SET @EndDate = getUTCdate();
SELECT   TOP 10000 SurrogateIdRangeFirstValue,
                   VisibleDate,
                   InvisibleHistoryRemovedDate
FROM     dbo.Transactions
WHERE    SurrogateIdRangeFirstValue > @StartNotInclusiveTranId
         AND SurrogateIdRangeFirstValue <= @EndInclusiveTranId
         AND EndDate <= @EndDate
ORDER BY SurrogateIdRangeFirstValue;
EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @@rowcount;

GO
CREATE PROCEDURE dbo.GetUsedResourceTypes
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'GetUsedResourceTypes', @Mode AS VARCHAR (100) = '', @st AS DATETIME = getUTCdate();
BEGIN TRY
    SELECT ResourceTypeId,
           Name
    FROM   dbo.ResourceType AS A
    WHERE  EXISTS (SELECT *
                   FROM   dbo.Resource AS B
                   WHERE  B.ResourceTypeId = A.ResourceTypeId);
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @@rowcount;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH

GO
CREATE PROCEDURE dbo.HardDeleteResource
@ResourceTypeId SMALLINT, @ResourceId VARCHAR (64), @KeepCurrentVersion BIT, @IsResourceChangeCaptureEnabled BIT
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = object_name(@@procid), @Mode AS VARCHAR (200) = 'RT=' + CONVERT (VARCHAR, @ResourceTypeId) + ' R=' + @ResourceId + ' V=' + CONVERT (VARCHAR, @KeepCurrentVersion) + ' CC=' + CONVERT (VARCHAR, @IsResourceChangeCaptureEnabled), @st AS DATETIME = getUTCdate(), @TransactionId AS BIGINT;
BEGIN TRY
    IF @IsResourceChangeCaptureEnabled = 1
        EXECUTE dbo.MergeResourcesBeginTransaction @Count = 1, @TransactionId = @TransactionId OUTPUT;
    IF @KeepCurrentVersion = 0
        BEGIN TRANSACTION;
    DECLARE @SurrogateIds TABLE (
        ResourceSurrogateId BIGINT NOT NULL);
    IF @IsResourceChangeCaptureEnabled = 1
       AND NOT EXISTS (SELECT *
                       FROM   dbo.Parameters
                       WHERE  Id = 'InvisibleHistory.IsEnabled'
                              AND Number = 0)
        UPDATE dbo.Resource
        SET    IsDeleted            = 1,
               RawResource          = 0xF,
               SearchParamHash      = NULL,
               HistoryTransactionId = @TransactionId
        OUTPUT deleted.ResourceSurrogateId INTO @SurrogateIds
        WHERE  ResourceTypeId = @ResourceTypeId
               AND ResourceId = @ResourceId
               AND (@KeepCurrentVersion = 0
                    OR IsHistory = 1)
               AND RawResource <> 0xF;
    ELSE
        DELETE dbo.Resource
        OUTPUT deleted.ResourceSurrogateId INTO @SurrogateIds
        WHERE  ResourceTypeId = @ResourceTypeId
               AND ResourceId = @ResourceId
               AND (@KeepCurrentVersion = 0
                    OR IsHistory = 1)
               AND RawResource <> 0xF;
    IF @KeepCurrentVersion = 0
        BEGIN
            DELETE B
            FROM   @SurrogateIds AS A
                   INNER LOOP JOIN
                   dbo.ResourceWriteClaim AS B WITH (INDEX (1), FORCESEEK, PAGLOCK)
                   ON B.ResourceSurrogateId = A.ResourceSurrogateId
            OPTION (MAXDOP 1);
            DELETE B
            FROM   @SurrogateIds AS A
                   INNER LOOP JOIN
                   dbo.ReferenceSearchParam AS B WITH (INDEX (1), FORCESEEK, PAGLOCK)
                   ON B.ResourceTypeId = @ResourceTypeId
                      AND B.ResourceSurrogateId = A.ResourceSurrogateId
            OPTION (MAXDOP 1);
            DELETE B
            FROM   @SurrogateIds AS A
                   INNER LOOP JOIN
                   dbo.TokenSearchParam AS B WITH (INDEX (1), FORCESEEK, PAGLOCK)
                   ON B.ResourceTypeId = @ResourceTypeId
                      AND B.ResourceSurrogateId = A.ResourceSurrogateId
            OPTION (MAXDOP 1);
            DELETE B
            FROM   @SurrogateIds AS A
                   INNER LOOP JOIN
                   dbo.TokenText AS B WITH (INDEX (1), FORCESEEK, PAGLOCK)
                   ON B.ResourceTypeId = @ResourceTypeId
                      AND B.ResourceSurrogateId = A.ResourceSurrogateId
            OPTION (MAXDOP 1);
            DELETE B
            FROM   @SurrogateIds AS A
                   INNER LOOP JOIN
                   dbo.StringSearchParam AS B WITH (INDEX (1), FORCESEEK, PAGLOCK)
                   ON B.ResourceTypeId = @ResourceTypeId
                      AND B.ResourceSurrogateId = A.ResourceSurrogateId
            OPTION (MAXDOP 1);
            DELETE B
            FROM   @SurrogateIds AS A
                   INNER LOOP JOIN
                   dbo.UriSearchParam AS B WITH (INDEX (1), FORCESEEK, PAGLOCK)
                   ON B.ResourceTypeId = @ResourceTypeId
                      AND B.ResourceSurrogateId = A.ResourceSurrogateId
            OPTION (MAXDOP 1);
            DELETE B
            FROM   @SurrogateIds AS A
                   INNER LOOP JOIN
                   dbo.NumberSearchParam AS B WITH (INDEX (1), FORCESEEK, PAGLOCK)
                   ON B.ResourceTypeId = @ResourceTypeId
                      AND B.ResourceSurrogateId = A.ResourceSurrogateId
            OPTION (MAXDOP 1);
            DELETE B
            FROM   @SurrogateIds AS A
                   INNER LOOP JOIN
                   dbo.QuantitySearchParam AS B WITH (INDEX (1), FORCESEEK, PAGLOCK)
                   ON B.ResourceTypeId = @ResourceTypeId
                      AND B.ResourceSurrogateId = A.ResourceSurrogateId
            OPTION (MAXDOP 1);
            DELETE B
            FROM   @SurrogateIds AS A
                   INNER LOOP JOIN
                   dbo.DateTimeSearchParam AS B WITH (INDEX (1), FORCESEEK, PAGLOCK)
                   ON B.ResourceTypeId = @ResourceTypeId
                      AND B.ResourceSurrogateId = A.ResourceSurrogateId
            OPTION (MAXDOP 1);
            DELETE B
            FROM   @SurrogateIds AS A
                   INNER LOOP JOIN
                   dbo.ReferenceTokenCompositeSearchParam AS B WITH (INDEX (1), FORCESEEK, PAGLOCK)
                   ON B.ResourceTypeId = @ResourceTypeId
                      AND B.ResourceSurrogateId = A.ResourceSurrogateId
            OPTION (MAXDOP 1);
            DELETE B
            FROM   @SurrogateIds AS A
                   INNER LOOP JOIN
                   dbo.TokenTokenCompositeSearchParam AS B WITH (INDEX (1), FORCESEEK, PAGLOCK)
                   ON B.ResourceTypeId = @ResourceTypeId
                      AND B.ResourceSurrogateId = A.ResourceSurrogateId
            OPTION (MAXDOP 1);
            DELETE B
            FROM   @SurrogateIds AS A
                   INNER LOOP JOIN
                   dbo.TokenDateTimeCompositeSearchParam AS B WITH (INDEX (1), FORCESEEK, PAGLOCK)
                   ON B.ResourceTypeId = @ResourceTypeId
                      AND B.ResourceSurrogateId = A.ResourceSurrogateId
            OPTION (MAXDOP 1);
            DELETE B
            FROM   @SurrogateIds AS A
                   INNER LOOP JOIN
                   dbo.TokenQuantityCompositeSearchParam AS B WITH (INDEX (1), FORCESEEK, PAGLOCK)
                   ON B.ResourceTypeId = @ResourceTypeId
                      AND B.ResourceSurrogateId = A.ResourceSurrogateId
            OPTION (MAXDOP 1);
            DELETE B
            FROM   @SurrogateIds AS A
                   INNER LOOP JOIN
                   dbo.TokenStringCompositeSearchParam AS B WITH (INDEX (1), FORCESEEK, PAGLOCK)
                   ON B.ResourceTypeId = @ResourceTypeId
                      AND B.ResourceSurrogateId = A.ResourceSurrogateId
            OPTION (MAXDOP 1);
            DELETE B
            FROM   @SurrogateIds AS A
                   INNER LOOP JOIN
                   dbo.TokenNumberNumberCompositeSearchParam AS B WITH (INDEX (1), FORCESEEK, PAGLOCK)
                   ON B.ResourceTypeId = @ResourceTypeId
                      AND B.ResourceSurrogateId = A.ResourceSurrogateId
            OPTION (MAXDOP 1);
        END
    IF @@trancount > 0
        COMMIT TRANSACTION;
    IF @IsResourceChangeCaptureEnabled = 1
        EXECUTE dbo.MergeResourcesCommitTransaction @TransactionId;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st;
END TRY
BEGIN CATCH
    IF @@trancount > 0
        ROLLBACK;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st;
    THROW;
END CATCH

GO
CREATE PROCEDURE dbo.InitializeIndexProperties
AS
SET NOCOUNT ON;
INSERT INTO dbo.IndexProperties (TableName, IndexName, PropertyName, PropertyValue)
SELECT Tbl,
       Ind,
       'DATA_COMPRESSION',
       isnull(data_comp, 'NONE')
FROM   (SELECT O.Name AS Tbl,
               I.Name AS Ind,
               (SELECT TOP 1 CASE WHEN data_compression_desc = 'PAGE' THEN 'PAGE' END
                FROM   sys.partitions AS P
                WHERE  P.object_id = I.object_id
                       AND I.index_id = P.index_id) AS data_comp
        FROM   sys.indexes AS I
               INNER JOIN
               sys.objects AS O
               ON O.object_id = I.object_id
        WHERE  O.type = 'u'
               AND EXISTS (SELECT *
                           FROM   sys.partition_schemes AS PS
                           WHERE  PS.data_space_id = I.data_space_id
                                  AND name = 'PartitionScheme_ResourceTypeId')) AS A
WHERE  NOT EXISTS (SELECT *
                   FROM   dbo.IndexProperties
                   WHERE  TableName = Tbl
                          AND IndexName = Ind);

GO
CREATE PROCEDURE dbo.LogEvent
@Process VARCHAR (100), @Status VARCHAR (10), @Mode VARCHAR (200)=NULL, @Action VARCHAR (20)=NULL, @Target VARCHAR (100)=NULL, @Rows BIGINT=NULL, @Start DATETIME=NULL, @Text NVARCHAR (3500)=NULL, @EventId BIGINT=NULL OUTPUT, @Retry INT=NULL
AS
SET NOCOUNT ON;
DECLARE @ErrorNumber AS INT = error_number(), @ErrorMessage AS VARCHAR (1000) = '', @TranCount AS INT = @@trancount, @DoWork AS BIT = 0, @NumberAdded AS BIT;
IF @ErrorNumber IS NOT NULL
   OR @Status IN ('Warn', 'Error')
    SET @DoWork = 1;
IF @DoWork = 0
    SET @DoWork = CASE WHEN EXISTS (SELECT *
                                    FROM   dbo.Parameters
                                    WHERE  Id = isnull(@Process, '')
                                           AND Char = 'LogEvent') THEN 1 ELSE 0 END;
IF @DoWork = 0
    RETURN;
IF @ErrorNumber IS NOT NULL
    SET @ErrorMessage = CASE WHEN @Retry IS NOT NULL THEN 'Retry ' + CONVERT (VARCHAR, @Retry) + ', ' ELSE '' END + 'Error ' + CONVERT (VARCHAR, error_number()) + ': ' + CONVERT (VARCHAR (1000), error_message()) + ', Level ' + CONVERT (VARCHAR, error_severity()) + ', State ' + CONVERT (VARCHAR, error_state()) + CASE WHEN error_procedure() IS NOT NULL THEN ', Procedure ' + error_procedure() ELSE '' END + ', Line ' + CONVERT (VARCHAR, error_line());
IF @TranCount > 0
   AND @ErrorNumber IS NOT NULL
    ROLLBACK;
IF databasepropertyex(db_name(), 'UpdateAbility') = 'READ_WRITE'
    BEGIN
        INSERT INTO dbo.EventLog (Process, Status, Mode, Action, Target, Rows, Milliseconds, EventDate, EventText, SPID, HostName)
        SELECT @Process,
               @Status,
               @Mode,
               @Action,
               @Target,
               @Rows,
               datediff(millisecond, @Start, getUTCdate()),
               getUTCdate() AS EventDate,
               CASE WHEN @ErrorNumber IS NULL THEN @Text ELSE @ErrorMessage + CASE WHEN isnull(@Text, '') <> '' THEN '. ' + @Text ELSE '' END END AS Text,
               @@SPID,
               host_name() AS HostName;
        SET @EventId = scope_identity();
    END
IF @TranCount > 0
   AND @ErrorNumber IS NOT NULL
    BEGIN TRANSACTION;

GO
CREATE PROCEDURE dbo.LogSchemaMigrationProgress
@message VARCHAR (MAX)
AS
INSERT  INTO dbo.SchemaMigrationProgress (Message)
VALUES                                  (@message);

GO
CREATE PROCEDURE dbo.MergeResources
@AffectedRows INT=0 OUTPUT, @RaiseExceptionOnConflict BIT=1, @IsResourceChangeCaptureEnabled BIT=0, @TransactionId BIGINT=NULL, @SingleTransaction BIT=1, @Resources dbo.ResourceList READONLY, @ResourceWriteClaims dbo.ResourceWriteClaimList READONLY, @ReferenceSearchParams dbo.ReferenceSearchParamList READONLY, @TokenSearchParams dbo.TokenSearchParamList READONLY, @TokenTexts dbo.TokenTextList READONLY, @StringSearchParams dbo.StringSearchParamList READONLY, @UriSearchParams dbo.UriSearchParamList READONLY, @NumberSearchParams dbo.NumberSearchParamList READONLY, @QuantitySearchParams dbo.QuantitySearchParamList READONLY, @DateTimeSearchParms dbo.DateTimeSearchParamList READONLY, @ReferenceTokenCompositeSearchParams dbo.ReferenceTokenCompositeSearchParamList READONLY, @TokenTokenCompositeSearchParams dbo.TokenTokenCompositeSearchParamList READONLY, @TokenDateTimeCompositeSearchParams dbo.TokenDateTimeCompositeSearchParamList READONLY, @TokenQuantityCompositeSearchParams dbo.TokenQuantityCompositeSearchParamList READONLY, @TokenStringCompositeSearchParams dbo.TokenStringCompositeSearchParamList READONLY, @TokenNumberNumberCompositeSearchParams dbo.TokenNumberNumberCompositeSearchParamList READONLY
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
    IF @RaiseExceptionOnConflict = 1
       AND error_number() IN (2601, 2627)
       AND error_message() LIKE '%''dbo.Resource''%'
        THROW 50409, 'Resource has been recently updated or added, please compare the resource content in code for any duplicate updates', 1;
    ELSE
        THROW;
END CATCH

GO
CREATE PROCEDURE dbo.MergeResourcesAdvanceTransactionVisibility
@AffectedRows INT=0 OUTPUT
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = object_name(@@procid), @Mode AS VARCHAR (100) = '', @st AS DATETIME = getUTCdate(), @msg AS VARCHAR (1000), @MaxTransactionId AS BIGINT, @MinTransactionId AS BIGINT, @MinNotCompletedTransactionId AS BIGINT, @CurrentTransactionId AS BIGINT;
SET @AffectedRows = 0;
BEGIN TRY
    EXECUTE dbo.MergeResourcesGetTransactionVisibility @MinTransactionId OUTPUT;
    SET @MinTransactionId += 1;
    SET @CurrentTransactionId = (SELECT   TOP 1 SurrogateIdRangeFirstValue
                                 FROM     dbo.Transactions
                                 ORDER BY SurrogateIdRangeFirstValue DESC);
    SET @MinNotCompletedTransactionId = isnull((SELECT   TOP 1 SurrogateIdRangeFirstValue
                                                FROM     dbo.Transactions
                                                WHERE    IsCompleted = 0
                                                         AND SurrogateIdRangeFirstValue BETWEEN @MinTransactionId AND @CurrentTransactionId
                                                ORDER BY SurrogateIdRangeFirstValue), @CurrentTransactionId + 1);
    SET @MaxTransactionId = (SELECT   TOP 1 SurrogateIdRangeFirstValue
                             FROM     dbo.Transactions
                             WHERE    IsCompleted = 1
                                      AND SurrogateIdRangeFirstValue BETWEEN @MinTransactionId AND @CurrentTransactionId
                                      AND SurrogateIdRangeFirstValue < @MinNotCompletedTransactionId
                             ORDER BY SurrogateIdRangeFirstValue DESC);
    IF @MaxTransactionId >= @MinTransactionId
        BEGIN
            UPDATE A
            SET    IsVisible   = 1,
                   VisibleDate = getUTCdate()
            FROM   dbo.Transactions AS A WITH (INDEX (1))
            WHERE  SurrogateIdRangeFirstValue BETWEEN @MinTransactionId AND @CurrentTransactionId
                   AND SurrogateIdRangeFirstValue <= @MaxTransactionId;
            SET @AffectedRows += @@rowcount;
        END
    SET @msg = 'Min=' + CONVERT (VARCHAR, @MinTransactionId) + ' C=' + CONVERT (VARCHAR, @CurrentTransactionId) + ' MinNC=' + CONVERT (VARCHAR, @MinNotCompletedTransactionId) + ' Max=' + CONVERT (VARCHAR, @MaxTransactionId);
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @AffectedRows, @Text = @msg;
END TRY
BEGIN CATCH
    IF @@trancount > 0
        ROLLBACK;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH

GO
CREATE PROCEDURE dbo.MergeResourcesBeginTransaction
@Count INT, @TransactionId BIGINT OUTPUT, @SequenceRangeFirstValue INT=NULL OUTPUT, @HeartbeatDate DATETIME=NULL, @EnableThrottling BIT=0
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'MergeResourcesBeginTransaction', @Mode AS VARCHAR (200) = 'Cnt=' + CONVERT (VARCHAR, @Count) + ' HB=' + isnull(CONVERT (VARCHAR, @HeartbeatDate, 121), 'NULL') + ' ET=' + CONVERT (VARCHAR, @EnableThrottling), @st AS DATETIME = getUTCdate(), @FirstValueVar AS SQL_VARIANT, @LastValueVar AS SQL_VARIANT, @OptimalConcurrency AS INT = isnull((SELECT Number
                                                                                                                                                                                                                                                                                                                                                                                  FROM   Parameters
                                                                                                                                                                                                                                                                                                                                                                                  WHERE  Id = 'MergeResources.OptimalConcurrentCalls'), 256), @CurrentConcurrency AS INT, @msg AS VARCHAR (1000);
BEGIN TRY
    SET @TransactionId = NULL;
    IF @@trancount > 0
        RAISERROR ('MergeResourcesBeginTransaction cannot be called inside outer transaction.', 18, 127);
    IF @EnableThrottling = 1
        BEGIN
            SET @CurrentConcurrency = (SELECT count(*)
                                       FROM   sys.dm_exec_sessions
                                       WHERE  status <> 'sleeping'
                                              AND program_name = 'MergeResources');
            IF @CurrentConcurrency > @OptimalConcurrency
                BEGIN
                    SET @msg = 'Number of concurrent MergeResources calls = ' + CONVERT (VARCHAR, @CurrentConcurrency) + ' is above optimal = ' + CONVERT (VARCHAR, @OptimalConcurrency) + '.';
                    THROW 50410, @msg, 1;
                END
        END
    SET @FirstValueVar = NULL;
    WHILE @FirstValueVar IS NULL
        BEGIN
            EXECUTE sys.sp_sequence_get_range @sequence_name = 'dbo.ResourceSurrogateIdUniquifierSequence', @range_size = @Count, @range_first_value = @FirstValueVar OUTPUT, @range_last_value = @LastValueVar OUTPUT;
            SET @SequenceRangeFirstValue = CONVERT (INT, @FirstValueVar);
            IF @SequenceRangeFirstValue > CONVERT (INT, @LastValueVar)
                SET @FirstValueVar = NULL;
        END
    SET @TransactionId = datediff_big(millisecond, '0001-01-01', sysUTCdatetime()) * 80000 + @SequenceRangeFirstValue;
    INSERT INTO dbo.Transactions (SurrogateIdRangeFirstValue, SurrogateIdRangeLastValue, HeartbeatDate)
    SELECT @TransactionId,
           @TransactionId + @Count - 1,
           isnull(@HeartbeatDate, getUTCdate());
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    IF @@trancount > 0
        ROLLBACK;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH

GO
CREATE PROCEDURE dbo.MergeResourcesCommitTransaction
@TransactionId BIGINT, @FailureReason VARCHAR (MAX)=NULL, @OverrideIsControlledByClientCheck BIT=0
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'MergeResourcesCommitTransaction', @st AS DATETIME = getUTCdate(), @InitialTranCount AS INT = @@trancount, @IsCompletedBefore AS BIT, @Rows AS INT, @msg AS VARCHAR (1000);
DECLARE @Mode AS VARCHAR (200) = 'TR=' + CONVERT (VARCHAR, @TransactionId) + ' OC=' + isnull(CONVERT (VARCHAR, @OverrideIsControlledByClientCheck), 'NULL');
BEGIN TRY
    IF @InitialTranCount = 0
        BEGIN TRANSACTION;
    UPDATE dbo.Transactions
    SET    IsCompleted        = 1,
           @IsCompletedBefore = IsCompleted,
           EndDate            = getUTCdate(),
           IsSuccess          = CASE WHEN @FailureReason IS NULL THEN 1 ELSE 0 END,
           FailureReason      = @FailureReason
    WHERE  SurrogateIdRangeFirstValue = @TransactionId
           AND (IsControlledByClient = 1
                OR @OverrideIsControlledByClientCheck = 1);
    SET @Rows = @@rowcount;
    IF @Rows = 0
        BEGIN
            SET @msg = 'Transaction [' + CONVERT (VARCHAR (20), @TransactionId) + '] is not controlled by client or does not exist.';
            RAISERROR (@msg, 18, 127);
        END
    IF @IsCompletedBefore = 1
        BEGIN
            IF @InitialTranCount = 0
                ROLLBACK;
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @Rows, @Target = '@IsCompletedBefore', @Text = '=1';
            RETURN;
        END
    IF @InitialTranCount = 0
        COMMIT TRANSACTION;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @Rows;
END TRY
BEGIN CATCH
    IF @InitialTranCount = 0
       AND @@trancount > 0
        ROLLBACK;
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH

GO
CREATE PROCEDURE dbo.MergeResourcesDeleteInvisibleHistory
@TransactionId BIGINT, @AffectedRows INT=NULL OUTPUT
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = object_name(@@procid), @Mode AS VARCHAR (100) = 'T=' + CONVERT (VARCHAR, @TransactionId), @st AS DATETIME = getUTCdate(), @TypeId AS SMALLINT;
SET @AffectedRows = 0;
BEGIN TRY
    DECLARE @Types TABLE (
        TypeId SMALLINT      PRIMARY KEY,
        Name   VARCHAR (100));
    INSERT INTO @Types
    EXECUTE dbo.GetUsedResourceTypes ;
    WHILE EXISTS (SELECT *
                  FROM   @Types)
        BEGIN
            SET @TypeId = (SELECT   TOP 1 TypeId
                           FROM     @Types
                           ORDER BY TypeId);
            DELETE dbo.Resource
            WHERE  ResourceTypeId = @TypeId
                   AND HistoryTransactionId = @TransactionId
                   AND RawResource = 0xF;
            SET @AffectedRows += @@rowcount;
            DELETE @Types
            WHERE  TypeId = @TypeId;
        END
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @AffectedRows;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH

GO
CREATE PROCEDURE dbo.MergeResourcesGetTimeoutTransactions
@TimeoutSec INT
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = object_name(@@procid), @Mode AS VARCHAR (100) = 'T=' + CONVERT (VARCHAR, @TimeoutSec), @st AS DATETIME = getUTCdate(), @MinTransactionId AS BIGINT;
BEGIN TRY
    EXECUTE dbo.MergeResourcesGetTransactionVisibility @MinTransactionId OUTPUT;
    SELECT   SurrogateIdRangeFirstValue
    FROM     dbo.Transactions
    WHERE    SurrogateIdRangeFirstValue > @MinTransactionId
             AND IsCompleted = 0
             AND datediff(second, HeartbeatDate, getUTCdate()) > @TimeoutSec
    ORDER BY SurrogateIdRangeFirstValue;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @@rowcount;
END TRY
BEGIN CATCH
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH

GO
CREATE PROCEDURE dbo.MergeResourcesGetTransactionVisibility
@TransactionId BIGINT OUTPUT
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = object_name(@@procid), @Mode AS VARCHAR (100) = '', @st AS DATETIME = getUTCdate();
SET @TransactionId = isnull((SELECT   TOP 1 SurrogateIdRangeFirstValue
                             FROM     dbo.Transactions
                             WHERE    IsVisible = 1
                             ORDER BY SurrogateIdRangeFirstValue DESC), -1);
EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @@rowcount, @Text = @TransactionId;

GO
CREATE PROCEDURE dbo.MergeResourcesPutTransactionHeartbeat
@TransactionId BIGINT
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'MergeResourcesPutTransactionHeartbeat', @Mode AS VARCHAR (100) = 'TR=' + CONVERT (VARCHAR, @TransactionId);
BEGIN TRY
    UPDATE dbo.Transactions
    SET    HeartbeatDate = getUTCdate()
    WHERE  SurrogateIdRangeFirstValue = @TransactionId
           AND IsControlledByClient = 1;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH

GO
CREATE PROCEDURE dbo.MergeResourcesPutTransactionInvisibleHistory
@TransactionId BIGINT
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = object_name(@@procid), @Mode AS VARCHAR (100) = 'TR=' + CONVERT (VARCHAR, @TransactionId), @st AS DATETIME = getUTCdate();
BEGIN TRY
    UPDATE dbo.Transactions
    SET    InvisibleHistoryRemovedDate = getUTCdate()
    WHERE  SurrogateIdRangeFirstValue = @TransactionId
           AND InvisibleHistoryRemovedDate IS NULL;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @@rowcount;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH

GO
CREATE PROCEDURE dbo.PutJobCancelation
@QueueType TINYINT, @GroupId BIGINT=NULL, @JobId BIGINT=NULL
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'PutJobCancelation', @Mode AS VARCHAR (100) = 'Q=' + isnull(CONVERT (VARCHAR, @QueueType), 'NULL') + ' G=' + isnull(CONVERT (VARCHAR, @GroupId), 'NULL') + ' J=' + isnull(CONVERT (VARCHAR, @JobId), 'NULL'), @st AS DATETIME = getUTCdate(), @Rows AS INT, @PartitionId AS TINYINT = @JobId % 16;
BEGIN TRY
    IF @JobId IS NULL
       AND @GroupId IS NULL
        RAISERROR ('@JobId = NULL and @GroupId = NULL', 18, 127);
    IF @JobId IS NOT NULL
        BEGIN
            UPDATE dbo.JobQueue
            SET    Status  = 4,
                   EndDate = getUTCdate(),
                   Version = datediff_big(millisecond, '0001-01-01', getUTCdate())
            WHERE  QueueType = @QueueType
                   AND PartitionId = @PartitionId
                   AND JobId = @JobId
                   AND Status = 0;
            SET @Rows = @@rowcount;
            IF @Rows = 0
                BEGIN
                    UPDATE dbo.JobQueue
                    SET    CancelRequested = 1
                    WHERE  QueueType = @QueueType
                           AND PartitionId = @PartitionId
                           AND JobId = @JobId
                           AND Status = 1;
                    SET @Rows = @@rowcount;
                END
        END
    ELSE
        BEGIN
            UPDATE dbo.JobQueue
            SET    Status  = 4,
                   EndDate = getUTCdate(),
                   Version = datediff_big(millisecond, '0001-01-01', getUTCdate())
            WHERE  QueueType = @QueueType
                   AND GroupId = @GroupId
                   AND Status = 0;
            SET @Rows = @@rowcount;
            UPDATE dbo.JobQueue
            SET    CancelRequested = 1
            WHERE  QueueType = @QueueType
                   AND GroupId = @GroupId
                   AND Status = 1;
            SET @Rows += @@rowcount;
        END
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @Rows;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH

GO
CREATE PROCEDURE dbo.PutJobHeartbeat
@QueueType TINYINT, @JobId BIGINT, @Version BIGINT, @CancelRequested BIT=0 OUTPUT
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'PutJobHeartbeat', @Mode AS VARCHAR (100), @st AS DATETIME = getUTCdate(), @Rows AS INT = 0, @PartitionId AS TINYINT = @JobId % 16;
SET @Mode = 'Q=' + CONVERT (VARCHAR, @QueueType) + ' J=' + CONVERT (VARCHAR, @JobId) + ' P=' + CONVERT (VARCHAR, @PartitionId) + ' V=' + CONVERT (VARCHAR, @Version);
BEGIN TRY
    UPDATE dbo.JobQueue
    SET    @CancelRequested = CancelRequested,
           HeartbeatDate    = getUTCdate()
    WHERE  QueueType = @QueueType
           AND PartitionId = @PartitionId
           AND JobId = @JobId
           AND Status = 1
           AND Version = @Version;
    SET @Rows = @@rowcount;
    IF @Rows = 0
       AND NOT EXISTS (SELECT *
                       FROM   dbo.JobQueue
                       WHERE  QueueType = @QueueType
                              AND PartitionId = @PartitionId
                              AND JobId = @JobId
                              AND Version = @Version
                              AND Status IN (2, 3, 4))
        BEGIN
            IF EXISTS (SELECT *
                       FROM   dbo.JobQueue
                       WHERE  QueueType = @QueueType
                              AND PartitionId = @PartitionId
                              AND JobId = @JobId)
                THROW 50412, 'Precondition failed', 1;
            ELSE
                THROW 50404, 'Job record not found', 1;
        END
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @Rows;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH

GO
CREATE PROCEDURE dbo.PutJobStatus
@QueueType TINYINT, @JobId BIGINT, @Version BIGINT, @Failed BIT, @Data BIGINT, @FinalResult VARCHAR (MAX), @RequestCancellationOnFailure BIT
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'PutJobStatus', @Mode AS VARCHAR (100), @st AS DATETIME = getUTCdate(), @Rows AS INT = 0, @PartitionId AS TINYINT = @JobId % 16, @GroupId AS BIGINT;
SET @Mode = 'Q=' + CONVERT (VARCHAR, @QueueType) + ' J=' + CONVERT (VARCHAR, @JobId) + ' P=' + CONVERT (VARCHAR, @PartitionId) + ' V=' + CONVERT (VARCHAR, @Version) + ' F=' + CONVERT (VARCHAR, @Failed) + ' R=' + isnull(@FinalResult, 'NULL');
BEGIN TRY
    UPDATE dbo.JobQueue
    SET    EndDate  = getUTCdate(),
           Status   = CASE WHEN @Failed = 1 THEN 3 WHEN CancelRequested = 1 THEN 4 ELSE 2 END,
           Data     = @Data,
           Result   = @FinalResult,
           @GroupId = GroupId
    WHERE  QueueType = @QueueType
           AND PartitionId = @PartitionId
           AND JobId = @JobId
           AND Status = 1
           AND Version = @Version;
    SET @Rows = @@rowcount;
    IF @Rows = 0
        BEGIN
            SET @GroupId = (SELECT GroupId
                            FROM   dbo.JobQueue
                            WHERE  QueueType = @QueueType
                                   AND PartitionId = @PartitionId
                                   AND JobId = @JobId
                                   AND Version = @Version
                                   AND Status IN (2, 3, 4));
            IF @GroupId IS NULL
                IF EXISTS (SELECT *
                           FROM   dbo.JobQueue
                           WHERE  QueueType = @QueueType
                                  AND PartitionId = @PartitionId
                                  AND JobId = @JobId)
                    THROW 50412, 'Precondition failed', 1;
                ELSE
                    THROW 50404, 'Job record not found', 1;
        END
    IF @Failed = 1
       AND @RequestCancellationOnFailure = 1
        EXECUTE dbo.PutJobCancelation @QueueType = @QueueType, @GroupId = @GroupId;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @Rows;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH

GO
CREATE OR ALTER PROCEDURE dbo.RemovePartitionFromResourceChanges_2
@partitionNumberToSwitchOut INT, @partitionBoundaryToMerge DATETIME2 (7)
AS
BEGIN
    TRUNCATE TABLE dbo.ResourceChangeDataStaging;
    ALTER TABLE dbo.ResourceChangeData SWITCH PARTITION @partitionNumberToSwitchOut TO dbo.ResourceChangeDataStaging;
    ALTER PARTITION FUNCTION PartitionFunction_ResourceChangeData_Timestamp( )
        MERGE RANGE (@partitionBoundaryToMerge);
    TRUNCATE TABLE dbo.ResourceChangeDataStaging;
END

GO
CREATE PROCEDURE dbo.SwitchPartitionsIn
@Tbl VARCHAR (100)
WITH EXECUTE AS 'dbo'
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'SwitchPartitionsIn', @Mode AS VARCHAR (200) = 'Tbl=' + isnull(@Tbl, 'NULL'), @st AS DATETIME = getUTCdate(), @ResourceTypeId AS SMALLINT, @Rows AS BIGINT, @Txt AS VARCHAR (1000), @TblInt AS VARCHAR (100), @Ind AS VARCHAR (200), @IndId AS INT, @DataComp AS VARCHAR (100);
DECLARE @Indexes TABLE (
    IndId INT           PRIMARY KEY,
    name  VARCHAR (200));
DECLARE @ResourceTypes TABLE (
    ResourceTypeId SMALLINT PRIMARY KEY);
BEGIN TRY
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Start';
    IF @Tbl IS NULL
        RAISERROR ('@Tbl IS NULL', 18, 127);
    INSERT INTO @Indexes
    SELECT index_id,
           name
    FROM   sys.indexes
    WHERE  object_id = object_id(@Tbl)
           AND is_disabled = 1;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = '@Indexes', @Action = 'Insert', @Rows = @@rowcount;
    WHILE EXISTS (SELECT *
                  FROM   @Indexes)
        BEGIN
            SELECT   TOP 1 @IndId = IndId,
                           @Ind = name
            FROM     @Indexes
            ORDER BY IndId;
            SET @DataComp = CASE WHEN (SELECT PropertyValue
                                       FROM   dbo.IndexProperties
                                       WHERE  TableName = @Tbl
                                              AND IndexName = @Ind) = 'PAGE' THEN ' PARTITION = ALL WITH (DATA_COMPRESSION = PAGE)' ELSE '' END;
            SET @Txt = 'IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id(''' + @Tbl + ''') AND name = ''' + @Ind + ''' AND is_disabled = 1) ALTER INDEX ' + @Ind + ' ON dbo.' + @Tbl + ' REBUILD' + @DataComp;
            EXECUTE (@Txt);
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = @Ind, @Action = 'Rebuild', @Text = @Txt;
            DELETE @Indexes
            WHERE  IndId = @IndId;
        END
    INSERT INTO @ResourceTypes
    SELECT CONVERT (SMALLINT, substring(name, charindex('_', name) + 1, 6)) AS ResourceTypeId
    FROM   sys.objects AS O
    WHERE  name LIKE @Tbl + '[_]%'
           AND EXISTS (SELECT *
                       FROM   sysindexes
                       WHERE  id = O.object_id
                              AND indid IN (0, 1)
                              AND rows > 0);
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = '#ResourceTypes', @Action = 'Select Into', @Rows = @@rowcount;
    WHILE EXISTS (SELECT *
                  FROM   @ResourceTypes)
        BEGIN
            SET @ResourceTypeId = (SELECT TOP 1 ResourceTypeId
                                   FROM   @ResourceTypes);
            SET @TblInt = @Tbl + '_' + CONVERT (VARCHAR, @ResourceTypeId);
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = @TblInt;
            SET @Txt = 'ALTER TABLE dbo.' + @TblInt + ' SWITCH TO dbo.' + @Tbl + ' PARTITION $partition.PartitionFunction_ResourceTypeId(' + CONVERT (VARCHAR, @ResourceTypeId) + ')';
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = @Tbl, @Action = 'Switch in start', @Text = @Txt;
            EXECUTE (@Txt);
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = @Tbl, @Action = 'Switch in', @Text = @Txt;
            IF EXISTS (SELECT *
                       FROM   sysindexes
                       WHERE  id = object_id(@TblInt)
                              AND rows > 0)
                BEGIN
                    SET @Txt = @TblInt + ' is not empty after switch';
                    RAISERROR (@Txt, 18, 127);
                END
            EXECUTE ('DROP TABLE dbo.' + @TblInt);
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = @TblInt, @Action = 'Drop';
            DELETE @ResourceTypes
            WHERE  ResourceTypeId = @ResourceTypeId;
        END
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st;
    THROW;
END CATCH

GO
CREATE PROCEDURE dbo.SwitchPartitionsInAllTables
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'SwitchPartitionsInAllTables', @Mode AS VARCHAR (200) = 'PS=PartitionScheme_ResourceTypeId', @st AS DATETIME = getUTCdate(), @Tbl AS VARCHAR (100);
BEGIN TRY
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Start';
    DECLARE @Tables TABLE (
        name      VARCHAR (100) PRIMARY KEY,
        supported BIT          );
    INSERT INTO @Tables
    EXECUTE dbo.GetPartitionedTables @IncludeNotDisabled = 1, @IncludeNotSupported = 0;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = '@Tables', @Action = 'Insert', @Rows = @@rowcount;
    WHILE EXISTS (SELECT *
                  FROM   @Tables)
        BEGIN
            SET @Tbl = (SELECT   TOP 1 name
                        FROM     @Tables
                        ORDER BY name);
            EXECUTE dbo.SwitchPartitionsIn @Tbl = @Tbl;
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = 'SwitchPartitionsIn', @Action = 'Execute', @Text = @Tbl;
            DELETE @Tables
            WHERE  name = @Tbl;
        END
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st;
    THROW;
END CATCH

GO
CREATE PROCEDURE dbo.SwitchPartitionsOut
@Tbl VARCHAR (100), @RebuildClustered BIT
WITH EXECUTE AS 'dbo'
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'SwitchPartitionsOut', @Mode AS VARCHAR (200) = 'Tbl=' + isnull(@Tbl, 'NULL') + ' ND=' + isnull(CONVERT (VARCHAR, @RebuildClustered), 'NULL'), @st AS DATETIME = getUTCdate(), @ResourceTypeId AS SMALLINT, @Rows AS BIGINT, @Txt AS VARCHAR (MAX), @TblInt AS VARCHAR (100), @IndId AS INT, @Ind AS VARCHAR (200), @Name AS VARCHAR (100), @checkName AS VARCHAR (200), @definition AS VARCHAR (200);
DECLARE @Indexes TABLE (
    IndId      INT           PRIMARY KEY,
    name       VARCHAR (200),
    IsDisabled BIT          );
DECLARE @IndexesRT TABLE (
    IndId      INT           PRIMARY KEY,
    name       VARCHAR (200),
    IsDisabled BIT          );
DECLARE @ResourceTypes TABLE (
    ResourceTypeId             SMALLINT PRIMARY KEY,
    partition_number_roundtrip INT     ,
    partition_number           INT     ,
    row_count                  BIGINT  );
DECLARE @Names TABLE (
    name VARCHAR (100) PRIMARY KEY);
DECLARE @CheckConstraints TABLE (
    CheckName       VARCHAR (200),
    CheckDefinition VARCHAR (200));
BEGIN TRY
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Start';
    IF @Tbl IS NULL
        RAISERROR ('@Tbl IS NULL', 18, 127);
    IF @RebuildClustered IS NULL
        RAISERROR ('@RebuildClustered IS NULL', 18, 127);
    INSERT INTO @Indexes
    SELECT index_id,
           name,
           is_disabled
    FROM   sys.indexes
    WHERE  object_id = object_id(@Tbl)
           AND (is_disabled = 0
                OR @RebuildClustered = 1);
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = '@Indexes', @Action = 'Insert', @Rows = @@rowcount;
    INSERT INTO @ResourceTypes
    SELECT partition_number - 1 AS ResourceTypeId,
           $PARTITION.PartitionFunction_ResourceTypeId (partition_number - 1) AS partition_number_roundtrip,
           partition_number,
           row_count
    FROM   sys.dm_db_partition_stats
    WHERE  object_id = object_id(@Tbl)
           AND index_id = 1
           AND row_count > 0;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = '@ResourceTypes', @Action = 'Insert', @Rows = @@rowcount, @Text = 'For partition switch';
    IF EXISTS (SELECT *
               FROM   @ResourceTypes
               WHERE  partition_number_roundtrip <> partition_number)
        RAISERROR ('Partition sanity check failed', 18, 127);
    WHILE EXISTS (SELECT *
                  FROM   @ResourceTypes)
        BEGIN
            SELECT   TOP 1 @ResourceTypeId = ResourceTypeId,
                           @Rows = row_count
            FROM     @ResourceTypes
            ORDER BY ResourceTypeId;
            SET @TblInt = @Tbl + '_' + CONVERT (VARCHAR, @ResourceTypeId);
            SET @Txt = 'Starting @ResourceTypeId=' + CONVERT (VARCHAR, @ResourceTypeId) + ' row_count=' + CONVERT (VARCHAR, @Rows);
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Text = @Txt;
            IF NOT EXISTS (SELECT *
                           FROM   sysindexes
                           WHERE  id = object_id(@TblInt)
                                  AND rows > 0)
                BEGIN
                    IF object_id(@TblInt) IS NOT NULL
                        BEGIN
                            EXECUTE ('DROP TABLE dbo.' + @TblInt);
                            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = @TblInt, @Action = 'Drop';
                        END
                    EXECUTE ('SELECT * INTO dbo.' + @TblInt + ' FROM dbo.' + @Tbl + ' WHERE 1 = 2');
                    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = @TblInt, @Action = 'Select Into', @Rows = @@rowcount;
                    DELETE @CheckConstraints;
                    INSERT INTO @CheckConstraints
                    SELECT name,
                           definition
                    FROM   sys.check_constraints
                    WHERE  parent_object_id = object_id(@Tbl);
                    WHILE EXISTS (SELECT *
                                  FROM   @CheckConstraints)
                        BEGIN
                            SELECT TOP 1 @checkName = CheckName,
                                         @definition = CheckDefinition
                            FROM   @CheckConstraints;
                            SET @Txt = 'ALTER TABLE ' + @TblInt + ' ADD CHECK ' + @definition;
                            EXECUTE (@Txt);
                            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = @TblInt, @Action = 'ALTER', @Text = @Txt;
                            DELETE @CheckConstraints
                            WHERE  CheckName = @checkName;
                        END
                    DELETE @Names;
                    INSERT INTO @Names
                    SELECT name
                    FROM   sys.columns
                    WHERE  object_id = object_id(@Tbl)
                           AND is_sparse = 1;
                    WHILE EXISTS (SELECT *
                                  FROM   @Names)
                        BEGIN
                            SET @Name = (SELECT   TOP 1 name
                                         FROM     @Names
                                         ORDER BY name);
                            SET @Txt = (SELECT 'ALTER TABLE dbo.' + @TblInt + ' ALTER COLUMN ' + @Name + ' ' + T.name + '(' + CONVERT (VARCHAR, C.precision) + ',' + CONVERT (VARCHAR, C.scale) + ') SPARSE NULL'
                                        FROM   sys.types AS T
                                               INNER JOIN
                                               sys.columns AS C
                                               ON C.system_type_id = T.system_type_id
                                        WHERE  C.object_id = object_id(@Tbl)
                                               AND C.name = @Name);
                            EXECUTE (@Txt);
                            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = @TblInt, @Action = 'ALTER', @Text = @Txt;
                            DELETE @Names
                            WHERE  name = @Name;
                        END
                END
            INSERT INTO @IndexesRT
            SELECT *
            FROM   @Indexes
            WHERE  IsDisabled = 0;
            WHILE EXISTS (SELECT *
                          FROM   @IndexesRT)
                BEGIN
                    SELECT   TOP 1 @IndId = IndId,
                                   @Ind = name
                    FROM     @IndexesRT
                    ORDER BY IndId;
                    IF NOT EXISTS (SELECT *
                                   FROM   sys.indexes
                                   WHERE  object_id = object_id(@TblInt)
                                          AND name = @Ind)
                        BEGIN
                            EXECUTE dbo.GetIndexCommands @Tbl = @Tbl, @Ind = @Ind, @AddPartClause = 0, @IncludeClustered = 1, @Txt = @Txt OUTPUT;
                            SET @Txt = replace(@Txt, '[' + @Tbl + ']', @TblInt);
                            EXECUTE (@Txt);
                            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = @TblInt, @Action = 'Create Index', @Text = @Txt;
                        END
                    DELETE @IndexesRT
                    WHERE  IndId = @IndId;
                END
            SET @Txt = 'ALTER TABLE dbo.' + @TblInt + ' ADD CHECK (ResourceTypeId >= ' + CONVERT (VARCHAR, @ResourceTypeId) + ' AND ResourceTypeId < ' + CONVERT (VARCHAR, @ResourceTypeId) + ' + 1)';
            EXECUTE (@Txt);
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = @Tbl, @Action = 'Add check', @Text = @Txt;
            SET @Txt = 'ALTER TABLE dbo.' + @Tbl + ' SWITCH PARTITION $partition.PartitionFunction_ResourceTypeId(' + CONVERT (VARCHAR, @ResourceTypeId) + ') TO dbo.' + @TblInt;
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = @Tbl, @Action = 'Switch out start', @Text = @Txt;
            EXECUTE (@Txt);
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = @Tbl, @Action = 'Switch out end', @Text = @Txt;
            DELETE @ResourceTypes
            WHERE  ResourceTypeId = @ResourceTypeId;
        END
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st;
    THROW;
END CATCH

GO
CREATE PROCEDURE dbo.SwitchPartitionsOutAllTables
@RebuildClustered BIT
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'SwitchPartitionsOutAllTables', @Mode AS VARCHAR (200) = 'PS=PartitionScheme_ResourceTypeId ND=' + isnull(CONVERT (VARCHAR, @RebuildClustered), 'NULL'), @st AS DATETIME = getUTCdate(), @Tbl AS VARCHAR (100);
BEGIN TRY
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Start';
    DECLARE @Tables TABLE (
        name      VARCHAR (100) PRIMARY KEY,
        supported BIT          );
    INSERT INTO @Tables
    EXECUTE dbo.GetPartitionedTables @IncludeNotDisabled = @RebuildClustered, @IncludeNotSupported = 0;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = '@Tables', @Action = 'Insert', @Rows = @@rowcount;
    WHILE EXISTS (SELECT *
                  FROM   @Tables)
        BEGIN
            SET @Tbl = (SELECT   TOP 1 name
                        FROM     @Tables
                        ORDER BY name);
            EXECUTE dbo.SwitchPartitionsOut @Tbl = @Tbl, @RebuildClustered = @RebuildClustered;
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = 'SwitchPartitionsOut', @Action = 'Execute', @Text = @Tbl;
            DELETE @Tables
            WHERE  name = @Tbl;
        END
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st;
    THROW;
END CATCH

GO
GO
CREATE OR ALTER PROCEDURE dbo.UpdateEventAgentCheckpoint
@CheckpointId VARCHAR (64), @LastProcessedDateTime DATETIMEOFFSET (7)=NULL, @LastProcessedIdentifier VARCHAR (64)=NULL
AS
BEGIN
    IF EXISTS (SELECT *
               FROM   dbo.EventAgentCheckpoint
               WHERE  CheckpointId = @CheckpointId)
        UPDATE dbo.EventAgentCheckpoint
        SET    CheckpointId            = @CheckpointId,
               LastProcessedDateTime   = @LastProcessedDateTime,
               LastProcessedIdentifier = @LastProcessedIdentifier,
               UpdatedOn               = sysutcdatetime()
        WHERE  CheckpointId = @CheckpointId;
    ELSE
        INSERT  INTO dbo.EventAgentCheckpoint (CheckpointId, LastProcessedDateTime, LastProcessedIdentifier, UpdatedOn)
        VALUES                               (@CheckpointId, @LastProcessedDateTime, @LastProcessedIdentifier, sysutcdatetime());
END

GO
CREATE PROCEDURE dbo.UpdateReindexJob
@id VARCHAR (64), @status VARCHAR (10), @rawJobRecord VARCHAR (MAX), @jobVersion BINARY (8)
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @currentJobVersion AS BINARY (8);
SELECT @currentJobVersion = JobVersion
FROM   dbo.ReindexJob WITH (UPDLOCK, HOLDLOCK)
WHERE  Id = @id;
IF (@currentJobVersion IS NULL)
    BEGIN
        THROW 50404, 'Reindex job record not found', 1;
    END
IF (@jobVersion <> @currentJobVersion)
    BEGIN
        THROW 50412, 'Precondition failed', 1;
    END
DECLARE @heartbeatDateTime AS DATETIME2 (7) = SYSUTCDATETIME();
UPDATE dbo.ReindexJob
SET    Status            = @status,
       HeartbeatDateTime = @heartbeatDateTime,
       RawJobRecord      = @rawJobRecord
WHERE  Id = @id;
SELECT @@DBTS;
COMMIT TRANSACTION;

GO
CREATE PROCEDURE dbo.UpdateResourceSearchParams
@FailedResources INT=0 OUTPUT, @Resources dbo.ResourceList READONLY, @ResourceWriteClaims dbo.ResourceWriteClaimList READONLY, @ReferenceSearchParams dbo.ReferenceSearchParamList READONLY, @TokenSearchParams dbo.TokenSearchParamList READONLY, @TokenTexts dbo.TokenTextList READONLY, @StringSearchParams dbo.StringSearchParamList READONLY, @UriSearchParams dbo.UriSearchParamList READONLY, @NumberSearchParams dbo.NumberSearchParamList READONLY, @QuantitySearchParams dbo.QuantitySearchParamList READONLY, @DateTimeSearchParams dbo.DateTimeSearchParamList READONLY, @ReferenceTokenCompositeSearchParams dbo.ReferenceTokenCompositeSearchParamList READONLY, @TokenTokenCompositeSearchParams dbo.TokenTokenCompositeSearchParamList READONLY, @TokenDateTimeCompositeSearchParams dbo.TokenDateTimeCompositeSearchParamList READONLY, @TokenQuantityCompositeSearchParams dbo.TokenQuantityCompositeSearchParamList READONLY, @TokenStringCompositeSearchParams dbo.TokenStringCompositeSearchParamList READONLY, @TokenNumberNumberCompositeSearchParams dbo.TokenNumberNumberCompositeSearchParamList READONLY
AS
SET NOCOUNT ON;
DECLARE @st AS DATETIME = getUTCdate(), @SP AS VARCHAR (100) = object_name(@@procid), @Mode AS VARCHAR (200) = isnull((SELECT 'RT=[' + CONVERT (VARCHAR, min(ResourceTypeId)) + ',' + CONVERT (VARCHAR, max(ResourceTypeId)) + '] Sur=[' + CONVERT (VARCHAR, min(ResourceSurrogateId)) + ',' + CONVERT (VARCHAR, max(ResourceSurrogateId)) + '] V=' + CONVERT (VARCHAR, max(Version)) + ' Rows=' + CONVERT (VARCHAR, count(*))
                                                                                                                       FROM   @Resources), 'Input=Empty'), @Rows AS INT;
BEGIN TRY
    DECLARE @Ids TABLE (
        ResourceTypeId      SMALLINT NOT NULL,
        ResourceSurrogateId BIGINT   NOT NULL);
    BEGIN TRANSACTION;
    UPDATE B
    SET    SearchParamHash = A.SearchParamHash
    OUTPUT deleted.ResourceTypeId, deleted.ResourceSurrogateId INTO @Ids
    FROM   @Resources AS A
           INNER JOIN
           dbo.Resource AS B
           ON B.ResourceTypeId = A.ResourceTypeId
              AND B.ResourceSurrogateId = A.ResourceSurrogateId
    WHERE  B.IsHistory = 0;
    SET @Rows = @@rowcount;
    DELETE B
    FROM   @Ids AS A
           INNER JOIN
           dbo.ResourceWriteClaim AS B
           ON B.ResourceSurrogateId = A.ResourceSurrogateId;
    DELETE B
    FROM   @Ids AS A
           INNER JOIN
           dbo.ReferenceSearchParam AS B
           ON B.ResourceTypeId = A.ResourceTypeId
              AND B.ResourceSurrogateId = A.ResourceSurrogateId;
    DELETE B
    FROM   @Ids AS A
           INNER JOIN
           dbo.TokenSearchParam AS B
           ON B.ResourceTypeId = A.ResourceTypeId
              AND B.ResourceSurrogateId = A.ResourceSurrogateId;
    DELETE B
    FROM   @Ids AS A
           INNER JOIN
           dbo.TokenText AS B
           ON B.ResourceTypeId = A.ResourceTypeId
              AND B.ResourceSurrogateId = A.ResourceSurrogateId;
    DELETE B
    FROM   @Ids AS A
           INNER JOIN
           dbo.StringSearchParam AS B
           ON B.ResourceTypeId = A.ResourceTypeId
              AND B.ResourceSurrogateId = A.ResourceSurrogateId;
    DELETE B
    FROM   @Ids AS A
           INNER JOIN
           dbo.UriSearchParam AS B
           ON B.ResourceTypeId = A.ResourceTypeId
              AND B.ResourceSurrogateId = A.ResourceSurrogateId;
    DELETE B
    FROM   @Ids AS A
           INNER JOIN
           dbo.NumberSearchParam AS B
           ON B.ResourceTypeId = A.ResourceTypeId
              AND B.ResourceSurrogateId = A.ResourceSurrogateId;
    DELETE B
    FROM   @Ids AS A
           INNER JOIN
           dbo.QuantitySearchParam AS B
           ON B.ResourceTypeId = A.ResourceTypeId
              AND B.ResourceSurrogateId = A.ResourceSurrogateId;
    DELETE B
    FROM   @Ids AS A
           INNER JOIN
           dbo.DateTimeSearchParam AS B
           ON B.ResourceTypeId = A.ResourceTypeId
              AND B.ResourceSurrogateId = A.ResourceSurrogateId;
    DELETE B
    FROM   @Ids AS A
           INNER JOIN
           dbo.ReferenceTokenCompositeSearchParam AS B
           ON B.ResourceTypeId = A.ResourceTypeId
              AND B.ResourceSurrogateId = A.ResourceSurrogateId;
    DELETE B
    FROM   @Ids AS A
           INNER JOIN
           dbo.TokenTokenCompositeSearchParam AS B
           ON B.ResourceTypeId = A.ResourceTypeId
              AND B.ResourceSurrogateId = A.ResourceSurrogateId;
    DELETE B
    FROM   @Ids AS A
           INNER JOIN
           dbo.TokenDateTimeCompositeSearchParam AS B
           ON B.ResourceTypeId = A.ResourceTypeId
              AND B.ResourceSurrogateId = A.ResourceSurrogateId;
    DELETE B
    FROM   @Ids AS A
           INNER JOIN
           dbo.TokenQuantityCompositeSearchParam AS B
           ON B.ResourceTypeId = A.ResourceTypeId
              AND B.ResourceSurrogateId = A.ResourceSurrogateId;
    DELETE B
    FROM   @Ids AS A
           INNER JOIN
           dbo.TokenStringCompositeSearchParam AS B
           ON B.ResourceTypeId = A.ResourceTypeId
              AND B.ResourceSurrogateId = A.ResourceSurrogateId;
    DELETE B
    FROM   @Ids AS A
           INNER JOIN
           dbo.TokenNumberNumberCompositeSearchParam AS B
           ON B.ResourceTypeId = A.ResourceTypeId
              AND B.ResourceSurrogateId = A.ResourceSurrogateId;
    INSERT INTO dbo.ResourceWriteClaim (ResourceSurrogateId, ClaimTypeId, ClaimValue)
    SELECT ResourceSurrogateId,
           ClaimTypeId,
           ClaimValue
    FROM   @ResourceWriteClaims;
    INSERT INTO dbo.ReferenceSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           BaseUri,
           ReferenceResourceTypeId,
           ReferenceResourceId,
           ReferenceResourceVersion
    FROM   @ReferenceSearchParams;
    INSERT INTO dbo.TokenSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           SystemId,
           Code,
           CodeOverflow
    FROM   @TokenSearchParams;
    INSERT INTO dbo.TokenText (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           Text
    FROM   @TokenTexts;
    INSERT INTO dbo.StringSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           Text,
           TextOverflow,
           IsMin,
           IsMax
    FROM   @StringSearchParams;
    INSERT INTO dbo.UriSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           Uri
    FROM   @UriSearchParams;
    INSERT INTO dbo.NumberSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           SingleValue,
           LowValue,
           HighValue
    FROM   @NumberSearchParams;
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
    INSERT INTO dbo.DateTimeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           StartDateTime,
           EndDateTime,
           IsLongerThanADay,
           IsMin,
           IsMax
    FROM   @DateTimeSearchParams;
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
    COMMIT TRANSACTION;
    SET @FailedResources = (SELECT count(*)
                            FROM   @Resources) - @Rows;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @Rows;
END TRY
BEGIN CATCH
    IF @@trancount > 0
        ROLLBACK;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st;
    THROW;
END CATCH

GO
CREATE PROCEDURE dbo.UpsertSearchParams
@searchParams dbo.SearchParamTableType_2 READONLY
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;
DECLARE @lastUpdated AS DATETIMEOFFSET (7) = SYSDATETIMEOFFSET();
DECLARE @summaryOfChanges TABLE (
    Uri    VARCHAR (128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Action VARCHAR (20)  NOT NULL);
MERGE INTO dbo.SearchParam WITH (TABLOCKX)
 AS target
USING @searchParams AS source ON target.Uri = source.Uri
WHEN MATCHED THEN UPDATE 
SET Status               = source.Status,
    LastUpdated          = @lastUpdated,
    IsPartiallySupported = source.IsPartiallySupported
WHEN NOT MATCHED BY TARGET THEN INSERT (Uri, Status, LastUpdated, IsPartiallySupported) VALUES (source.Uri, source.Status, @lastUpdated, source.IsPartiallySupported)
OUTPUT source.Uri, $ACTION INTO @summaryOfChanges;
SELECT SearchParamId,
       SearchParam.Uri
FROM   dbo.SearchParam AS searchParam
       INNER JOIN
       @summaryOfChanges AS upsertedSearchParam
       ON searchParam.Uri = upsertedSearchParam.Uri
WHERE  upsertedSearchParam.Action = 'INSERT';
COMMIT TRANSACTION;

GO
CREATE VIEW dbo.CurrentResource
AS
SELECT *
FROM   dbo.Resource
WHERE  IsHistory = 0;

GO
