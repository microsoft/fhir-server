
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


GO
INSERT  INTO dbo.SchemaVersion
VALUES (48, 'started');

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

CREATE TYPE dbo.StringList AS TABLE (
    String VARCHAR (MAX));

CREATE TYPE dbo.BulkResourceWriteClaimTableType_1 AS TABLE (
    Offset      INT            NOT NULL,
    ClaimTypeId TINYINT        NOT NULL,
    ClaimValue  NVARCHAR (128) NOT NULL);

CREATE TYPE dbo.BulkCompartmentAssignmentTableType_1 AS TABLE (
    Offset              INT          NOT NULL,
    CompartmentTypeId   TINYINT      NOT NULL,
    ReferenceResourceId VARCHAR (64) COLLATE Latin1_General_100_CS_AS NOT NULL);

CREATE TYPE dbo.BulkReferenceSearchParamTableType_1 AS TABLE (
    Offset                   INT           NOT NULL,
    SearchParamId            SMALLINT      NOT NULL,
    BaseUri                  VARCHAR (128) COLLATE Latin1_General_100_CS_AS NULL,
    ReferenceResourceTypeId  SMALLINT      NULL,
    ReferenceResourceId      VARCHAR (64)  COLLATE Latin1_General_100_CS_AS NOT NULL,
    ReferenceResourceVersion INT           NULL);

CREATE TYPE dbo.BulkTokenSearchParamTableType_1 AS TABLE (
    Offset        INT           NOT NULL,
    SearchParamId SMALLINT      NOT NULL,
    SystemId      INT           NULL,
    Code          VARCHAR (128) COLLATE Latin1_General_100_CS_AS NOT NULL);

CREATE TYPE dbo.BulkTokenSearchParamTableType_2 AS TABLE (
    Offset        INT           NOT NULL,
    SearchParamId SMALLINT      NOT NULL,
    SystemId      INT           NULL,
    Code          VARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow  VARCHAR (MAX) COLLATE Latin1_General_100_CS_AS NULL);

CREATE TYPE dbo.BulkTokenTextTableType_1 AS TABLE (
    Offset        INT            NOT NULL,
    SearchParamId SMALLINT       NOT NULL,
    Text          NVARCHAR (400) COLLATE Latin1_General_CI_AI NOT NULL);

CREATE TYPE dbo.BulkStringSearchParamTableType_1 AS TABLE (
    Offset        INT            NOT NULL,
    SearchParamId SMALLINT       NOT NULL,
    Text          NVARCHAR (256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL,
    TextOverflow  NVARCHAR (MAX) COLLATE Latin1_General_100_CI_AI_SC NULL);

CREATE TYPE dbo.BulkStringSearchParamTableType_2 AS TABLE (
    Offset        INT            NOT NULL,
    SearchParamId SMALLINT       NOT NULL,
    Text          NVARCHAR (256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL,
    TextOverflow  NVARCHAR (MAX) COLLATE Latin1_General_100_CI_AI_SC NULL,
    IsMin         BIT            NOT NULL,
    IsMax         BIT            NOT NULL);

CREATE TYPE dbo.BulkUriSearchParamTableType_1 AS TABLE (
    Offset        INT           NOT NULL,
    SearchParamId SMALLINT      NOT NULL,
    Uri           VARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL);

CREATE TYPE dbo.BulkNumberSearchParamTableType_1 AS TABLE (
    Offset        INT             NOT NULL,
    SearchParamId SMALLINT        NOT NULL,
    SingleValue   DECIMAL (18, 6) NULL,
    LowValue      DECIMAL (18, 6) NULL,
    HighValue     DECIMAL (18, 6) NULL);

CREATE TYPE dbo.BulkQuantitySearchParamTableType_1 AS TABLE (
    Offset         INT             NOT NULL,
    SearchParamId  SMALLINT        NOT NULL,
    SystemId       INT             NULL,
    QuantityCodeId INT             NULL,
    SingleValue    DECIMAL (18, 6) NULL,
    LowValue       DECIMAL (18, 6) NULL,
    HighValue      DECIMAL (18, 6) NULL);

CREATE TYPE dbo.BulkDateTimeSearchParamTableType_1 AS TABLE (
    Offset           INT                NOT NULL,
    SearchParamId    SMALLINT           NOT NULL,
    StartDateTime    DATETIMEOFFSET (7) NOT NULL,
    EndDateTime      DATETIMEOFFSET (7) NOT NULL,
    IsLongerThanADay BIT                NOT NULL);

CREATE TYPE dbo.BulkDateTimeSearchParamTableType_2 AS TABLE (
    Offset           INT                NOT NULL,
    SearchParamId    SMALLINT           NOT NULL,
    StartDateTime    DATETIMEOFFSET (7) NOT NULL,
    EndDateTime      DATETIMEOFFSET (7) NOT NULL,
    IsLongerThanADay BIT                NOT NULL,
    IsMin            BIT                NOT NULL,
    IsMax            BIT                NOT NULL);

CREATE TYPE dbo.BulkReferenceTokenCompositeSearchParamTableType_1 AS TABLE (
    Offset                    INT           NOT NULL,
    SearchParamId             SMALLINT      NOT NULL,
    BaseUri1                  VARCHAR (128) COLLATE Latin1_General_100_CS_AS NULL,
    ReferenceResourceTypeId1  SMALLINT      NULL,
    ReferenceResourceId1      VARCHAR (64)  COLLATE Latin1_General_100_CS_AS NOT NULL,
    ReferenceResourceVersion1 INT           NULL,
    SystemId2                 INT           NULL,
    Code2                     VARCHAR (128) COLLATE Latin1_General_100_CS_AS NOT NULL);

CREATE TYPE dbo.BulkReferenceTokenCompositeSearchParamTableType_2 AS TABLE (
    Offset                    INT           NOT NULL,
    SearchParamId             SMALLINT      NOT NULL,
    BaseUri1                  VARCHAR (128) COLLATE Latin1_General_100_CS_AS NULL,
    ReferenceResourceTypeId1  SMALLINT      NULL,
    ReferenceResourceId1      VARCHAR (64)  COLLATE Latin1_General_100_CS_AS NOT NULL,
    ReferenceResourceVersion1 INT           NULL,
    SystemId2                 INT           NULL,
    Code2                     VARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow2             VARCHAR (MAX) COLLATE Latin1_General_100_CS_AS NULL);

CREATE TYPE dbo.BulkTokenTokenCompositeSearchParamTableType_1 AS TABLE (
    Offset        INT           NOT NULL,
    SearchParamId SMALLINT      NOT NULL,
    SystemId1     INT           NULL,
    Code1         VARCHAR (128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    SystemId2     INT           NULL,
    Code2         VARCHAR (128) COLLATE Latin1_General_100_CS_AS NOT NULL);

CREATE TYPE dbo.BulkTokenTokenCompositeSearchParamTableType_2 AS TABLE (
    Offset        INT           NOT NULL,
    SearchParamId SMALLINT      NOT NULL,
    SystemId1     INT           NULL,
    Code1         VARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow1 VARCHAR (MAX) COLLATE Latin1_General_100_CS_AS NULL,
    SystemId2     INT           NULL,
    Code2         VARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow2 VARCHAR (MAX) COLLATE Latin1_General_100_CS_AS NULL);

CREATE TYPE dbo.BulkTokenDateTimeCompositeSearchParamTableType_1 AS TABLE (
    Offset            INT                NOT NULL,
    SearchParamId     SMALLINT           NOT NULL,
    SystemId1         INT                NULL,
    Code1             VARCHAR (128)      COLLATE Latin1_General_100_CS_AS NOT NULL,
    StartDateTime2    DATETIMEOFFSET (7) NOT NULL,
    EndDateTime2      DATETIMEOFFSET (7) NOT NULL,
    IsLongerThanADay2 BIT                NOT NULL);

CREATE TYPE dbo.BulkTokenDateTimeCompositeSearchParamTableType_2 AS TABLE (
    Offset            INT                NOT NULL,
    SearchParamId     SMALLINT           NOT NULL,
    SystemId1         INT                NULL,
    Code1             VARCHAR (256)      COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow1     VARCHAR (MAX)      COLLATE Latin1_General_100_CS_AS NULL,
    StartDateTime2    DATETIMEOFFSET (7) NOT NULL,
    EndDateTime2      DATETIMEOFFSET (7) NOT NULL,
    IsLongerThanADay2 BIT                NOT NULL);

CREATE TYPE dbo.BulkTokenQuantityCompositeSearchParamTableType_1 AS TABLE (
    Offset          INT             NOT NULL,
    SearchParamId   SMALLINT        NOT NULL,
    SystemId1       INT             NULL,
    Code1           VARCHAR (128)   COLLATE Latin1_General_100_CS_AS NOT NULL,
    SystemId2       INT             NULL,
    QuantityCodeId2 INT             NULL,
    SingleValue2    DECIMAL (18, 6) NULL,
    LowValue2       DECIMAL (18, 6) NULL,
    HighValue2      DECIMAL (18, 6) NULL);

CREATE TYPE dbo.BulkTokenQuantityCompositeSearchParamTableType_2 AS TABLE (
    Offset          INT             NOT NULL,
    SearchParamId   SMALLINT        NOT NULL,
    SystemId1       INT             NULL,
    Code1           VARCHAR (256)   COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow1   VARCHAR (MAX)   COLLATE Latin1_General_100_CS_AS NULL,
    SystemId2       INT             NULL,
    QuantityCodeId2 INT             NULL,
    SingleValue2    DECIMAL (18, 6) NULL,
    LowValue2       DECIMAL (18, 6) NULL,
    HighValue2      DECIMAL (18, 6) NULL);

CREATE TYPE dbo.BulkTokenStringCompositeSearchParamTableType_1 AS TABLE (
    Offset        INT            NOT NULL,
    SearchParamId SMALLINT       NOT NULL,
    SystemId1     INT            NULL,
    Code1         VARCHAR (128)  COLLATE Latin1_General_100_CS_AS NOT NULL,
    Text2         NVARCHAR (256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL,
    TextOverflow2 NVARCHAR (MAX) COLLATE Latin1_General_100_CI_AI_SC NULL);

CREATE TYPE dbo.BulkTokenStringCompositeSearchParamTableType_2 AS TABLE (
    Offset        INT            NOT NULL,
    SearchParamId SMALLINT       NOT NULL,
    SystemId1     INT            NULL,
    Code1         VARCHAR (256)  COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow1 VARCHAR (MAX)  COLLATE Latin1_General_100_CS_AS NULL,
    Text2         NVARCHAR (256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL,
    TextOverflow2 NVARCHAR (MAX) COLLATE Latin1_General_100_CI_AI_SC NULL);

CREATE TYPE dbo.BulkTokenNumberNumberCompositeSearchParamTableType_1 AS TABLE (
    Offset        INT             NOT NULL,
    SearchParamId SMALLINT        NOT NULL,
    SystemId1     INT             NULL,
    Code1         VARCHAR (128)   COLLATE Latin1_General_100_CS_AS NOT NULL,
    SingleValue2  DECIMAL (18, 6) NULL,
    LowValue2     DECIMAL (18, 6) NULL,
    HighValue2    DECIMAL (18, 6) NULL,
    SingleValue3  DECIMAL (18, 6) NULL,
    LowValue3     DECIMAL (18, 6) NULL,
    HighValue3    DECIMAL (18, 6) NULL,
    HasRange      BIT             NOT NULL);

CREATE TYPE dbo.BulkTokenNumberNumberCompositeSearchParamTableType_2 AS TABLE (
    Offset        INT             NOT NULL,
    SearchParamId SMALLINT        NOT NULL,
    SystemId1     INT             NULL,
    Code1         VARCHAR (256)   COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow1 VARCHAR (MAX)   COLLATE Latin1_General_100_CS_AS NULL,
    SingleValue2  DECIMAL (18, 6) NULL,
    LowValue2     DECIMAL (18, 6) NULL,
    HighValue2    DECIMAL (18, 6) NULL,
    SingleValue3  DECIMAL (18, 6) NULL,
    LowValue3     DECIMAL (18, 6) NULL,
    HighValue3    DECIMAL (18, 6) NULL,
    HasRange      BIT             NOT NULL);

CREATE TYPE dbo.SearchParamTableType_1 AS TABLE (
    Uri                  VARCHAR (128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Status               VARCHAR (10)  NOT NULL,
    IsPartiallySupported BIT           NOT NULL);

CREATE TYPE dbo.BulkReindexResourceTableType_1 AS TABLE (
    Offset          INT          NOT NULL,
    ResourceTypeId  SMALLINT     NOT NULL,
    ResourceId      VARCHAR (64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    ETag            INT          NULL,
    SearchParamHash VARCHAR (64) NOT NULL);

CREATE TYPE dbo.BulkImportResourceType_1 AS TABLE (
    ResourceTypeId       SMALLINT        NOT NULL,
    ResourceId           VARCHAR (64)    COLLATE Latin1_General_100_CS_AS NOT NULL,
    Version              INT             NOT NULL,
    IsHistory            BIT             NOT NULL,
    ResourceSurrogateId  BIGINT          NOT NULL,
    IsDeleted            BIT             NOT NULL,
    RequestMethod        VARCHAR (10)    NULL,
    RawResource          VARBINARY (MAX) NOT NULL,
    IsRawResourceMetaSet BIT             DEFAULT 0 NOT NULL,
    SearchParamHash      VARCHAR (64)    NULL);

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

ALTER TABLE dbo.CompartmentAssignment SET (LOCK_ESCALATION = AUTO);

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
    IsHistory           BIT           NOT NULL,
    IsMin               BIT           CONSTRAINT date_IsMin_Constraint DEFAULT 0 NOT NULL,
    IsMax               BIT           CONSTRAINT date_IsMax_Constraint DEFAULT 0 NOT NULL
);

ALTER TABLE dbo.DateTimeSearchParam SET (LOCK_ESCALATION = AUTO);

CREATE CLUSTERED INDEX IXC_DateTimeSearchParam
    ON dbo.DateTimeSearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE NONCLUSTERED INDEX IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime
    ON dbo.DateTimeSearchParam(ResourceTypeId, SearchParamId, StartDateTime, EndDateTime, ResourceSurrogateId)
    INCLUDE(IsLongerThanADay, IsMin, IsMax) WHERE IsHistory = 0
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE NONCLUSTERED INDEX IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime
    ON dbo.DateTimeSearchParam(ResourceTypeId, SearchParamId, EndDateTime, StartDateTime, ResourceSurrogateId)
    INCLUDE(IsLongerThanADay, IsMin, IsMax) WHERE IsHistory = 0
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE NONCLUSTERED INDEX IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime_Long
    ON dbo.DateTimeSearchParam(ResourceTypeId, SearchParamId, StartDateTime, EndDateTime, ResourceSurrogateId)
    INCLUDE(IsMin, IsMax) WHERE IsHistory = 0
                                AND IsLongerThanADay = 1
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE NONCLUSTERED INDEX IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime_Long
    ON dbo.DateTimeSearchParam(ResourceTypeId, SearchParamId, EndDateTime, StartDateTime, ResourceSurrogateId)
    INCLUDE(IsMin, IsMax) WHERE IsHistory = 0
                                AND IsLongerThanADay = 1
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

CREATE TABLE dbo.ExportJob (
    Id                VARCHAR (64)  COLLATE Latin1_General_100_CS_AS NOT NULL,
    Hash              VARCHAR (64)  COLLATE Latin1_General_100_CS_AS NOT NULL,
    Status            VARCHAR (10)  NOT NULL,
    HeartbeatDateTime DATETIME2 (7) NULL,
    RawJobRecord      VARCHAR (MAX) NOT NULL,
    JobVersion        ROWVERSION    NOT NULL,
    CONSTRAINT PKC_ExportJob PRIMARY KEY CLUSTERED (Id)
);

CREATE UNIQUE NONCLUSTERED INDEX IX_ExportJob_Hash_Status_HeartbeatDateTime
    ON dbo.ExportJob(Hash, Status, HeartbeatDateTime);

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
    ResourceTypeId      SMALLINT        NOT NULL,
    ResourceSurrogateId BIGINT          NOT NULL,
    SearchParamId       SMALLINT        NOT NULL,
    SingleValue         DECIMAL (18, 6) NULL,
    LowValue            DECIMAL (18, 6) NOT NULL,
    HighValue           DECIMAL (18, 6) NOT NULL,
    IsHistory           BIT             NOT NULL
);

ALTER TABLE dbo.NumberSearchParam SET (LOCK_ESCALATION = AUTO);

CREATE CLUSTERED INDEX IXC_NumberSearchParam
    ON dbo.NumberSearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE NONCLUSTERED INDEX IX_NumberSearchParam_SearchParamId_SingleValue
    ON dbo.NumberSearchParam(ResourceTypeId, SearchParamId, SingleValue, ResourceSurrogateId) WHERE IsHistory = 0
                                                                                                    AND SingleValue IS NOT NULL
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE NONCLUSTERED INDEX IX_NumberSearchParam_SearchParamId_LowValue_HighValue
    ON dbo.NumberSearchParam(ResourceTypeId, SearchParamId, LowValue, HighValue, ResourceSurrogateId) WHERE IsHistory = 0
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE NONCLUSTERED INDEX IX_NumberSearchParam_SearchParamId_HighValue_LowValue
    ON dbo.NumberSearchParam(ResourceTypeId, SearchParamId, HighValue, LowValue, ResourceSurrogateId) WHERE IsHistory = 0
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
    ResourceTypeId      SMALLINT        NOT NULL,
    ResourceSurrogateId BIGINT          NOT NULL,
    SearchParamId       SMALLINT        NOT NULL,
    SystemId            INT             NULL,
    QuantityCodeId      INT             NULL,
    SingleValue         DECIMAL (18, 6) NULL,
    LowValue            DECIMAL (18, 6) NOT NULL,
    HighValue           DECIMAL (18, 6) NOT NULL,
    IsHistory           BIT             NOT NULL
);

ALTER TABLE dbo.QuantitySearchParam SET (LOCK_ESCALATION = AUTO);

CREATE CLUSTERED INDEX IXC_QuantitySearchParam
    ON dbo.QuantitySearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE NONCLUSTERED INDEX IX_QuantitySearchParam_SearchParamId_QuantityCodeId_SingleValue
    ON dbo.QuantitySearchParam(ResourceTypeId, SearchParamId, QuantityCodeId, SingleValue, ResourceSurrogateId)
    INCLUDE(SystemId) WHERE IsHistory = 0
                            AND SingleValue IS NOT NULL
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE NONCLUSTERED INDEX IX_QuantitySearchParam_SearchParamId_QuantityCodeId_LowValue_HighValue
    ON dbo.QuantitySearchParam(ResourceTypeId, SearchParamId, QuantityCodeId, LowValue, HighValue, ResourceSurrogateId)
    INCLUDE(SystemId) WHERE IsHistory = 0
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE NONCLUSTERED INDEX IX_QuantitySearchParam_SearchParamId_QuantityCodeId_HighValue_LowValue
    ON dbo.QuantitySearchParam(ResourceTypeId, SearchParamId, QuantityCodeId, HighValue, LowValue, ResourceSurrogateId)
    INCLUDE(SystemId) WHERE IsHistory = 0
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE TABLE dbo.ReferenceSearchParam (
    ResourceTypeId           SMALLINT      NOT NULL,
    ResourceSurrogateId      BIGINT        NOT NULL,
    SearchParamId            SMALLINT      NOT NULL,
    BaseUri                  VARCHAR (128) COLLATE Latin1_General_100_CS_AS NULL,
    ReferenceResourceTypeId  SMALLINT      NULL,
    ReferenceResourceId      VARCHAR (64)  COLLATE Latin1_General_100_CS_AS NOT NULL,
    ReferenceResourceVersion INT           NULL,
    IsHistory                BIT           NOT NULL
);

ALTER TABLE dbo.ReferenceSearchParam SET (LOCK_ESCALATION = AUTO);

CREATE CLUSTERED INDEX IXC_ReferenceSearchParam
    ON dbo.ReferenceSearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE NONCLUSTERED INDEX IX_ReferenceSearchParam_SearchParamId_ReferenceResourceTypeId_ReferenceResourceId_BaseUri_ReferenceResourceVersion
    ON dbo.ReferenceSearchParam(ResourceTypeId, SearchParamId, ReferenceResourceId, ReferenceResourceTypeId, BaseUri, ResourceSurrogateId)
    INCLUDE(ReferenceResourceVersion) WHERE IsHistory = 0 WITH (DATA_COMPRESSION = PAGE)
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
    IsHistory                 BIT           NOT NULL,
    CodeOverflow2             VARCHAR (MAX) COLLATE Latin1_General_100_CS_AS NULL
);

ALTER TABLE dbo.ReferenceTokenCompositeSearchParam
    ADD CONSTRAINT CHK_ReferenceTokenCompositeSearchParam_CodeOverflow2 CHECK (LEN(Code2) = 256
                                                                               OR CodeOverflow2 IS NULL);

ALTER TABLE dbo.ReferenceTokenCompositeSearchParam SET (LOCK_ESCALATION = AUTO);

CREATE CLUSTERED INDEX IXC_ReferenceTokenCompositeSearchParam
    ON dbo.ReferenceTokenCompositeSearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE NONCLUSTERED INDEX IX_ReferenceTokenCompositeSearchParam_ReferenceResourceId1_Code2
    ON dbo.ReferenceTokenCompositeSearchParam(ResourceTypeId, SearchParamId, ReferenceResourceId1, Code2, ResourceSurrogateId)
    INCLUDE(ReferenceResourceTypeId1, BaseUri1, SystemId2) WHERE IsHistory = 0 WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE TABLE dbo.ReindexJob (
    Id                VARCHAR (64)  COLLATE Latin1_General_100_CS_AS NOT NULL,
    Status            VARCHAR (10)  NOT NULL,
    HeartbeatDateTime DATETIME2 (7) NULL,
    RawJobRecord      VARCHAR (MAX) NOT NULL,
    JobVersion        ROWVERSION    NOT NULL,
    CONSTRAINT PKC_ReindexJob PRIMARY KEY CLUSTERED (Id)
);

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
    CONSTRAINT PKC_Resource PRIMARY KEY CLUSTERED (ResourceTypeId, ResourceSurrogateId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId),
    CONSTRAINT CH_Resource_RawResource_Length CHECK (RawResource > 0x0)
);

ALTER TABLE dbo.Resource SET (LOCK_ESCALATION = AUTO);

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
    Status               VARCHAR (10)       NULL,
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
    IsHistory           BIT            NOT NULL,
    IsMin               BIT            CONSTRAINT string_IsMin_Constraint DEFAULT 0 NOT NULL,
    IsMax               BIT            CONSTRAINT string_IsMax_Constraint DEFAULT 0 NOT NULL
);

ALTER TABLE dbo.StringSearchParam SET (LOCK_ESCALATION = AUTO);

CREATE CLUSTERED INDEX IXC_StringSearchParam
    ON dbo.StringSearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE NONCLUSTERED INDEX IX_StringSearchParam_SearchParamId_Text
    ON dbo.StringSearchParam(ResourceTypeId, SearchParamId, Text, ResourceSurrogateId)
    INCLUDE(TextOverflow, IsMin, IsMax) WHERE IsHistory = 0 WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE NONCLUSTERED INDEX IX_StringSearchParam_SearchParamId_TextWithOverflow
    ON dbo.StringSearchParam(ResourceTypeId, SearchParamId, Text, ResourceSurrogateId)
    INCLUDE(IsMin, IsMax) WHERE IsHistory = 0
                                AND TextOverflow IS NOT NULL WITH (DATA_COMPRESSION = PAGE)
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
    IsHistory           BIT           NOT NULL,
    CodeOverflow1       VARCHAR (MAX) COLLATE Latin1_General_100_CS_AS NULL
);

ALTER TABLE dbo.TokenDateTimeCompositeSearchParam
    ADD CONSTRAINT CHK_TokenDateTimeCompositeSearchParam_CodeOverflow1 CHECK (LEN(Code1) = 256
                                                                              OR CodeOverflow1 IS NULL);

ALTER TABLE dbo.TokenDateTimeCompositeSearchParam SET (LOCK_ESCALATION = AUTO);

CREATE CLUSTERED INDEX IXC_TokenDateTimeCompositeSearchParam
    ON dbo.TokenDateTimeCompositeSearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE NONCLUSTERED INDEX IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2
    ON dbo.TokenDateTimeCompositeSearchParam(ResourceTypeId, SearchParamId, Code1, StartDateTime2, EndDateTime2, ResourceSurrogateId)
    INCLUDE(SystemId1, IsLongerThanADay2) WHERE IsHistory = 0 WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE NONCLUSTERED INDEX IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2
    ON dbo.TokenDateTimeCompositeSearchParam(ResourceTypeId, SearchParamId, Code1, EndDateTime2, StartDateTime2, ResourceSurrogateId)
    INCLUDE(SystemId1, IsLongerThanADay2) WHERE IsHistory = 0 WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE NONCLUSTERED INDEX IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2_Long
    ON dbo.TokenDateTimeCompositeSearchParam(ResourceTypeId, SearchParamId, Code1, StartDateTime2, EndDateTime2, ResourceSurrogateId)
    INCLUDE(SystemId1) WHERE IsHistory = 0
                             AND IsLongerThanADay2 = 1 WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE NONCLUSTERED INDEX IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2_Long
    ON dbo.TokenDateTimeCompositeSearchParam(ResourceTypeId, SearchParamId, Code1, EndDateTime2, StartDateTime2, ResourceSurrogateId)
    INCLUDE(SystemId1) WHERE IsHistory = 0
                             AND IsLongerThanADay2 = 1 WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE TABLE dbo.TokenNumberNumberCompositeSearchParam (
    ResourceTypeId      SMALLINT        NOT NULL,
    ResourceSurrogateId BIGINT          NOT NULL,
    SearchParamId       SMALLINT        NOT NULL,
    SystemId1           INT             NULL,
    Code1               VARCHAR (256)   COLLATE Latin1_General_100_CS_AS NOT NULL,
    SingleValue2        DECIMAL (18, 6) NULL,
    LowValue2           DECIMAL (18, 6) NULL,
    HighValue2          DECIMAL (18, 6) NULL,
    SingleValue3        DECIMAL (18, 6) NULL,
    LowValue3           DECIMAL (18, 6) NULL,
    HighValue3          DECIMAL (18, 6) NULL,
    HasRange            BIT             NOT NULL,
    IsHistory           BIT             NOT NULL,
    CodeOverflow1       VARCHAR (MAX)   COLLATE Latin1_General_100_CS_AS NULL
);

ALTER TABLE dbo.TokenNumberNumberCompositeSearchParam
    ADD CONSTRAINT CHK_TokenNumberNumberCompositeSearchParam_CodeOverflow1 CHECK (LEN(Code1) = 256
                                                                                  OR CodeOverflow1 IS NULL);

ALTER TABLE dbo.TokenNumberNumberCompositeSearchParam SET (LOCK_ESCALATION = AUTO);

CREATE CLUSTERED INDEX IXC_TokenNumberNumberCompositeSearchParam
    ON dbo.TokenNumberNumberCompositeSearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE NONCLUSTERED INDEX IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_Text2
    ON dbo.TokenNumberNumberCompositeSearchParam(ResourceTypeId, SearchParamId, Code1, SingleValue2, SingleValue3, ResourceSurrogateId)
    INCLUDE(SystemId1) WHERE IsHistory = 0
                             AND HasRange = 0 WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE NONCLUSTERED INDEX IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_LowValue2_HighValue2_LowValue3_HighValue3
    ON dbo.TokenNumberNumberCompositeSearchParam(ResourceTypeId, SearchParamId, Code1, LowValue2, HighValue2, LowValue3, HighValue3, ResourceSurrogateId)
    INCLUDE(SystemId1) WHERE IsHistory = 0
                             AND HasRange = 1 WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE TABLE dbo.TokenQuantityCompositeSearchParam (
    ResourceTypeId      SMALLINT        NOT NULL,
    ResourceSurrogateId BIGINT          NOT NULL,
    SearchParamId       SMALLINT        NOT NULL,
    SystemId1           INT             NULL,
    Code1               VARCHAR (256)   COLLATE Latin1_General_100_CS_AS NOT NULL,
    SystemId2           INT             NULL,
    QuantityCodeId2     INT             NULL,
    SingleValue2        DECIMAL (18, 6) NULL,
    LowValue2           DECIMAL (18, 6) NULL,
    HighValue2          DECIMAL (18, 6) NULL,
    IsHistory           BIT             NOT NULL,
    CodeOverflow1       VARCHAR (MAX)   COLLATE Latin1_General_100_CS_AS NULL
);

ALTER TABLE dbo.TokenQuantityCompositeSearchParam
    ADD CONSTRAINT CHK_TokenQuantityCompositeSearchParam_CodeOverflow1 CHECK (LEN(Code1) = 256
                                                                              OR CodeOverflow1 IS NULL);

ALTER TABLE dbo.TokenQuantityCompositeSearchParam SET (LOCK_ESCALATION = AUTO);

CREATE CLUSTERED INDEX IXC_TokenQuantityCompositeSearchParam
    ON dbo.TokenQuantityCompositeSearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE NONCLUSTERED INDEX IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_SingleValue2
    ON dbo.TokenQuantityCompositeSearchParam(ResourceTypeId, SearchParamId, Code1, SingleValue2, ResourceSurrogateId)
    INCLUDE(QuantityCodeId2, SystemId1, SystemId2) WHERE IsHistory = 0
                                                         AND SingleValue2 IS NOT NULL WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE NONCLUSTERED INDEX IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_LowValue2_HighValue2
    ON dbo.TokenQuantityCompositeSearchParam(ResourceTypeId, SearchParamId, Code1, LowValue2, HighValue2, ResourceSurrogateId)
    INCLUDE(QuantityCodeId2, SystemId1, SystemId2) WHERE IsHistory = 0
                                                         AND LowValue2 IS NOT NULL WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE NONCLUSTERED INDEX IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_HighValue2_LowValue2
    ON dbo.TokenQuantityCompositeSearchParam(ResourceTypeId, SearchParamId, Code1, HighValue2, LowValue2, ResourceSurrogateId)
    INCLUDE(QuantityCodeId2, SystemId1, SystemId2) WHERE IsHistory = 0
                                                         AND LowValue2 IS NOT NULL WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE TABLE dbo.TokenSearchParam (
    ResourceTypeId      SMALLINT      NOT NULL,
    ResourceSurrogateId BIGINT        NOT NULL,
    SearchParamId       SMALLINT      NOT NULL,
    SystemId            INT           NULL,
    Code                VARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    IsHistory           BIT           NOT NULL,
    CodeOverflow        VARCHAR (MAX) COLLATE Latin1_General_100_CS_AS NULL
);

ALTER TABLE dbo.TokenSearchParam
    ADD CONSTRAINT CHK_TokenSearchParam_CodeOverflow CHECK (LEN(Code) = 256
                                                            OR CodeOverflow IS NULL);

ALTER TABLE dbo.TokenSearchParam SET (LOCK_ESCALATION = AUTO);

CREATE CLUSTERED INDEX IXC_TokenSearchParam
    ON dbo.TokenSearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE NONCLUSTERED INDEX IX_TokenSeachParam_SearchParamId_Code_SystemId
    ON dbo.TokenSearchParam(ResourceTypeId, SearchParamId, Code, ResourceSurrogateId)
    INCLUDE(SystemId) WHERE IsHistory = 0 WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE TABLE dbo.TokenStringCompositeSearchParam (
    ResourceTypeId      SMALLINT       NOT NULL,
    ResourceSurrogateId BIGINT         NOT NULL,
    SearchParamId       SMALLINT       NOT NULL,
    SystemId1           INT            NULL,
    Code1               VARCHAR (256)  COLLATE Latin1_General_100_CS_AS NOT NULL,
    Text2               NVARCHAR (256) COLLATE Latin1_General_CI_AI NOT NULL,
    TextOverflow2       NVARCHAR (MAX) COLLATE Latin1_General_CI_AI NULL,
    IsHistory           BIT            NOT NULL,
    CodeOverflow1       VARCHAR (MAX)  COLLATE Latin1_General_100_CS_AS NULL
);

ALTER TABLE dbo.TokenStringCompositeSearchParam
    ADD CONSTRAINT CHK_TokenStringCompositeSearchParam_CodeOverflow1 CHECK (LEN(Code1) = 256
                                                                            OR CodeOverflow1 IS NULL);

ALTER TABLE dbo.TokenStringCompositeSearchParam SET (LOCK_ESCALATION = AUTO);

CREATE CLUSTERED INDEX IXC_TokenStringCompositeSearchParam
    ON dbo.TokenStringCompositeSearchParam(ResourceSurrogateId, SearchParamId) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE NONCLUSTERED INDEX IX_TokenStringCompositeSearchParam_SearchParamId_Code1_Text2
    ON dbo.TokenStringCompositeSearchParam(ResourceTypeId, SearchParamId, Code1, Text2, ResourceSurrogateId)
    INCLUDE(SystemId1, TextOverflow2) WHERE IsHistory = 0 WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE NONCLUSTERED INDEX IX_TokenStringCompositeSearchParam_SearchParamId_Code1_Text2WithOverflow
    ON dbo.TokenStringCompositeSearchParam(ResourceTypeId, SearchParamId, Code1, Text2, ResourceSurrogateId)
    INCLUDE(SystemId1) WHERE IsHistory = 0
                             AND TextOverflow2 IS NOT NULL WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE TABLE dbo.TokenText (
    ResourceTypeId      SMALLINT       NOT NULL,
    ResourceSurrogateId BIGINT         NOT NULL,
    SearchParamId       SMALLINT       NOT NULL,
    Text                NVARCHAR (400) COLLATE Latin1_General_CI_AI NOT NULL,
    IsHistory           BIT            NOT NULL
);

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
    IsHistory           BIT           NOT NULL,
    CodeOverflow1       VARCHAR (MAX) COLLATE Latin1_General_100_CS_AS NULL,
    CodeOverflow2       VARCHAR (MAX) COLLATE Latin1_General_100_CS_AS NULL
);

ALTER TABLE dbo.TokenTokenCompositeSearchParam
    ADD CONSTRAINT CHK_TokenTokenCompositeSearchParam_CodeOverflow1 CHECK (LEN(Code1) = 256
                                                                           OR CodeOverflow1 IS NULL);

ALTER TABLE dbo.TokenTokenCompositeSearchParam
    ADD CONSTRAINT CHK_TokenTokenCompositeSearchParam_CodeOverflow2 CHECK (LEN(Code2) = 256
                                                                           OR CodeOverflow2 IS NULL);

ALTER TABLE dbo.TokenTokenCompositeSearchParam SET (LOCK_ESCALATION = AUTO);

CREATE CLUSTERED INDEX IXC_TokenTokenCompositeSearchParam
    ON dbo.TokenTokenCompositeSearchParam(ResourceSurrogateId, SearchParamId) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE NONCLUSTERED INDEX IX_TokenTokenCompositeSearchParam_Code1_Code2
    ON dbo.TokenTokenCompositeSearchParam(ResourceTypeId, SearchParamId, Code1, Code2, ResourceSurrogateId)
    INCLUDE(SystemId1, SystemId2) WHERE IsHistory = 0 WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE TABLE dbo.UriSearchParam (
    ResourceTypeId      SMALLINT      NOT NULL,
    ResourceSurrogateId BIGINT        NOT NULL,
    SearchParamId       SMALLINT      NOT NULL,
    Uri                 VARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    IsHistory           BIT           NOT NULL
);

ALTER TABLE dbo.UriSearchParam SET (LOCK_ESCALATION = AUTO);

CREATE CLUSTERED INDEX IXC_UriSearchParam
    ON dbo.UriSearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

CREATE NONCLUSTERED INDEX IX_UriSearchParam_SearchParamId_Uri
    ON dbo.UriSearchParam(ResourceTypeId, SearchParamId, Uri, ResourceSurrogateId) WHERE IsHistory = 0 WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

COMMIT
GO
CREATE PROCEDURE dbo.AcquireExportJobs
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
FROM   dbo.ExportJob WITH (TABLOCKX)
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
        FROM     dbo.ExportJob
        WHERE    (Status = 'Queued'
                  OR (Status = 'Running'
                      AND HeartbeatDateTime <= @expirationDateTime))
        ORDER BY HeartbeatDateTime;
        DECLARE @heartbeatDateTime AS DATETIME2 (7) = SYSUTCDATETIME();
        UPDATE dbo.ExportJob
        SET    Status            = 'Running',
               HeartbeatDateTime = @heartbeatDateTime,
               RawJobRecord      = JSON_MODIFY(RawJobRecord, '$.status', 'Running')
        OUTPUT inserted.RawJobRecord, inserted.JobVersion
        FROM   dbo.ExportJob AS job
               INNER JOIN
               @availableJobs AS availableJob
               ON job.Id = availableJob.Id
                  AND job.JobVersion = availableJob.JobVersion;
    END
COMMIT TRANSACTION;

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
CREATE PROCEDURE dbo.BatchDeleteResourceParams
@tableName NVARCHAR (128), @resourceTypeId SMALLINT, @startResourceSurrogateId BIGINT, @endResourceSurrogateId BIGINT, @batchSize INT
AS
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;
DECLARE @Sql AS NVARCHAR (MAX);
DECLARE @ParmDefinition AS NVARCHAR (512);
IF OBJECT_ID(@tableName) IS NOT NULL
    BEGIN
        SET @sql = N'DELETE TOP(@BatchSizeParam) FROM ' + @tableName + N' WITH (TABLOCK) WHERE ResourceTypeId = @ResourceTypeIdParam AND ResourceSurrogateId >= @StartResourceSurrogateIdParam AND ResourceSurrogateId < @EndResourceSurrogateIdParam';
        SET @parmDefinition = N'@BatchSizeParam int, @ResourceTypeIdParam smallint, @StartResourceSurrogateIdParam bigint, @EndResourceSurrogateIdParam bigint';
        EXECUTE sp_executesql @sql, @parmDefinition, @BatchSizeParam = @batchSize, @ResourceTypeIdParam = @resourceTypeId, @StartResourceSurrogateIdParam = @startResourceSurrogateId, @EndResourceSurrogateIdParam = @endResourceSurrogateId;
    END
COMMIT TRANSACTION;
RETURN @@rowcount;

GO
CREATE PROCEDURE dbo.BatchDeleteResources
@resourceTypeId SMALLINT, @startResourceSurrogateId BIGINT, @endResourceSurrogateId BIGINT, @batchSize INT
AS
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;
DELETE TOP (@batchSize)
       dbo.Resource WITH (TABLOCK)
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId >= @startResourceSurrogateId
       AND ResourceSurrogateId < @endResourceSurrogateId;
COMMIT TRANSACTION;
RETURN @@rowcount;

GO
CREATE PROCEDURE dbo.BatchDeleteResourceWriteClaims
@startResourceSurrogateId BIGINT, @endResourceSurrogateId BIGINT, @batchSize INT
AS
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;
DELETE TOP (@batchSize)
       dbo.ResourceWriteClaim WITH (TABLOCK)
WHERE  ResourceSurrogateId >= @startResourceSurrogateId
       AND ResourceSurrogateId < @endResourceSurrogateId;
COMMIT TRANSACTION;
RETURN @@rowcount;

GO
CREATE PROCEDURE dbo.BulkMergeResource
@resources dbo.BulkImportResourceType_1 READONLY
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
MERGE INTO [dbo].[Resource] WITH (ROWLOCK, INDEX (IX_Resource_ResourceTypeId_ResourceId_Version))
 AS target
USING @resources AS source ON source.[ResourceTypeId] = target.[ResourceTypeId]
                              AND source.[ResourceId] = target.[ResourceId]
                              AND source.[Version] = target.[Version]
WHEN NOT MATCHED BY TARGET THEN INSERT ([ResourceTypeId], [ResourceId], [Version], [IsHistory], [ResourceSurrogateId], [IsDeleted], [RequestMethod], [RawResource], [IsRawResourceMetaSet], [SearchParamHash]) VALUES ([ResourceTypeId], [ResourceId], [Version], [IsHistory], [ResourceSurrogateId], [IsDeleted], [RequestMethod], [RawResource], [IsRawResourceMetaSet], [SearchParamHash]) OUTPUT Inserted.[ResourceSurrogateId];
COMMIT TRANSACTION;

GO
CREATE PROCEDURE dbo.BulkReindexResources_2
@resourcesToReindex dbo.BulkReindexResourceTableType_1 READONLY, @resourceWriteClaims dbo.BulkResourceWriteClaimTableType_1 READONLY, @compartmentAssignments dbo.BulkCompartmentAssignmentTableType_1 READONLY, @referenceSearchParams dbo.BulkReferenceSearchParamTableType_1 READONLY, @tokenSearchParams dbo.BulkTokenSearchParamTableType_2 READONLY, @tokenTextSearchParams dbo.BulkTokenTextTableType_1 READONLY, @stringSearchParams dbo.BulkStringSearchParamTableType_2 READONLY, @numberSearchParams dbo.BulkNumberSearchParamTableType_1 READONLY, @quantitySearchParams dbo.BulkQuantitySearchParamTableType_1 READONLY, @uriSearchParams dbo.BulkUriSearchParamTableType_1 READONLY, @dateTimeSearchParms dbo.BulkDateTimeSearchParamTableType_2 READONLY, @referenceTokenCompositeSearchParams dbo.BulkReferenceTokenCompositeSearchParamTableType_2 READONLY, @tokenTokenCompositeSearchParams dbo.BulkTokenTokenCompositeSearchParamTableType_2 READONLY, @tokenDateTimeCompositeSearchParams dbo.BulkTokenDateTimeCompositeSearchParamTableType_2 READONLY, @tokenQuantityCompositeSearchParams dbo.BulkTokenQuantityCompositeSearchParamTableType_2 READONLY, @tokenStringCompositeSearchParams dbo.BulkTokenStringCompositeSearchParamTableType_2 READONLY, @tokenNumberNumberCompositeSearchParams dbo.BulkTokenNumberNumberCompositeSearchParamTableType_2 READONLY
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @computedValues TABLE (
    Offset              INT          NOT NULL,
    ResourceTypeId      SMALLINT     NOT NULL,
    VersionProvided     BIGINT       NULL,
    SearchParamHash     VARCHAR (64) NOT NULL,
    ResourceSurrogateId BIGINT       NULL,
    VersionInDatabase   BIGINT       NULL);
INSERT INTO @computedValues
SELECT resourceToReindex.Offset,
       resourceToReindex.ResourceTypeId,
       resourceToReindex.ETag,
       resourceToReindex.SearchParamHash,
       resourceInDB.ResourceSurrogateId,
       resourceInDB.Version
FROM   @resourcesToReindex AS resourceToReindex
       LEFT OUTER JOIN
       dbo.Resource AS resourceInDB WITH (UPDLOCK, INDEX (IX_Resource_ResourceTypeId_ResourceId))
       ON resourceInDB.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND resourceInDB.ResourceId = resourceToReindex.ResourceId
          AND resourceInDB.IsHistory = 0;
DECLARE @versionDiff AS INT;
SET @versionDiff = (SELECT COUNT(*)
                    FROM   @computedValues
                    WHERE  VersionProvided IS NOT NULL
                           AND VersionProvided <> VersionInDatabase);
IF (@versionDiff > 0)
    BEGIN
        DELETE @computedValues
        WHERE  VersionProvided IS NOT NULL
               AND VersionProvided <> VersionInDatabase;
    END
UPDATE resourceInDB
SET    resourceInDB.SearchParamHash = resourceToReindex.SearchParamHash
FROM   @computedValues AS resourceToReindex
       INNER JOIN
       dbo.Resource AS resourceInDB
       ON resourceInDB.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND resourceInDB.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.ResourceWriteClaim AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.CompartmentAssignment AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.ReferenceSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.TokenSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.TokenText AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.StringSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.UriSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.NumberSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.QuantitySearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.DateTimeSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.ReferenceTokenCompositeSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.TokenTokenCompositeSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.TokenDateTimeCompositeSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.TokenQuantityCompositeSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.TokenStringCompositeSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.TokenNumberNumberCompositeSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
INSERT INTO dbo.ResourceWriteClaim (ResourceSurrogateId, ClaimTypeId, ClaimValue)
SELECT DISTINCT resourceToReindex.ResourceSurrogateId,
                searchIndex.ClaimTypeId,
                searchIndex.ClaimValue
FROM   @resourceWriteClaims AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.CompartmentAssignment (ResourceTypeId, ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.CompartmentTypeId,
                searchIndex.ReferenceResourceId,
                0
FROM   @compartmentAssignments AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.ReferenceSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.BaseUri,
                searchIndex.ReferenceResourceTypeId,
                searchIndex.ReferenceResourceId,
                searchIndex.ReferenceResourceVersion,
                0
FROM   @referenceSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.TokenSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.SystemId,
                searchIndex.Code,
                searchIndex.CodeOverflow,
                0
FROM   @tokenSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.TokenText (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.Text,
                0
FROM   @tokenTextSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.StringSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsHistory, IsMin, IsMax)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.Text,
                searchIndex.TextOverflow,
                0,
                searchIndex.IsMin,
                searchIndex.IsMax
FROM   @stringSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.UriSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.Uri,
                0
FROM   @uriSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.NumberSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.SingleValue,
                searchIndex.LowValue,
                searchIndex.HighValue,
                0
FROM   @numberSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.QuantitySearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.SystemId,
                searchIndex.QuantityCodeId,
                searchIndex.SingleValue,
                searchIndex.LowValue,
                searchIndex.HighValue,
                0
FROM   @quantitySearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.DateTimeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsHistory, IsMin, IsMax)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.StartDateTime,
                searchIndex.EndDateTime,
                searchIndex.IsLongerThanADay,
                0,
                searchIndex.IsMin,
                searchIndex.IsMax
FROM   @dateTimeSearchParms AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.ReferenceTokenCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.BaseUri1,
                searchIndex.ReferenceResourceTypeId1,
                searchIndex.ReferenceResourceId1,
                searchIndex.ReferenceResourceVersion1,
                searchIndex.SystemId2,
                searchIndex.Code2,
                searchIndex.CodeOverflow2,
                0
FROM   @referenceTokenCompositeSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.TokenTokenCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.SystemId1,
                searchIndex.Code1,
                searchIndex.CodeOverflow1,
                searchIndex.SystemId2,
                searchIndex.Code2,
                searchIndex.CodeOverflow2,
                0
FROM   @tokenTokenCompositeSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.TokenDateTimeCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.SystemId1,
                searchIndex.Code1,
                searchIndex.CodeOverflow1,
                searchIndex.StartDateTime2,
                searchIndex.EndDateTime2,
                searchIndex.IsLongerThanADay2,
                0
FROM   @tokenDateTimeCompositeSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.TokenQuantityCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.SystemId1,
                searchIndex.Code1,
                searchIndex.CodeOverflow1,
                searchIndex.SingleValue2,
                searchIndex.SystemId2,
                searchIndex.QuantityCodeId2,
                searchIndex.LowValue2,
                searchIndex.HighValue2,
                0
FROM   @tokenQuantityCompositeSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.TokenStringCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.SystemId1,
                searchIndex.Code1,
                searchIndex.CodeOverflow1,
                searchIndex.Text2,
                searchIndex.TextOverflow2,
                0
FROM   @tokenStringCompositeSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.TokenNumberNumberCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.SystemId1,
                searchIndex.Code1,
                searchIndex.CodeOverflow1,
                searchIndex.SingleValue2,
                searchIndex.LowValue2,
                searchIndex.HighValue2,
                searchIndex.SingleValue3,
                searchIndex.LowValue3,
                searchIndex.HighValue3,
                searchIndex.HasRange,
                0
FROM   @tokenNumberNumberCompositeSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
SELECT @versionDiff;
COMMIT TRANSACTION;

GO
CREATE PROCEDURE [dbo].[CancelTask]
@taskId VARCHAR (64)
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @heartbeatDateTime AS DATETIME2 (7) = SYSUTCDATETIME();
IF NOT EXISTS (SELECT *
               FROM   [dbo].[TaskInfo]
               WHERE  TaskId = @taskId)
    BEGIN
        THROW 50404, 'Task not exist', 1;
    END
UPDATE dbo.TaskInfo
SET    IsCanceled        = 1,
       HeartbeatDateTime = @heartbeatDateTime
WHERE  TaskId = @taskId;
SELECT TaskId,
       QueueId,
       Status,
       TaskTypeId,
       RunId,
       IsCanceled,
       RetryCount,
       MaxRetryCount,
       HeartbeatDateTime,
       InputData,
       TaskContext,
       Result
FROM   [dbo].[TaskInfo]
WHERE  TaskId = @taskId;
COMMIT TRANSACTION;

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
CREATE PROCEDURE dbo.CheckActiveReindexJobs
AS
SET NOCOUNT ON;
SELECT Id
FROM   dbo.ReindexJob
WHERE  Status = 'Running'
       OR Status = 'Queued'
       OR Status = 'Paused';

GO
CREATE PROCEDURE dbo.CompleteTask
@taskId VARCHAR (64), @taskResult VARCHAR (MAX), @runId VARCHAR (50)
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT *
               FROM   [dbo].[TaskInfo]
               WHERE  TaskId = @taskId
                      AND RunId = @runId)
    BEGIN
        THROW 50404, 'Task not exist or runid not match', 1;
    END
UPDATE dbo.TaskInfo
SET    Status      = 3,
       EndDateTime = SYSUTCDATETIME(),
       Result      = @taskResult
WHERE  TaskId = @taskId;
COMMIT TRANSACTION;
EXECUTE dbo.GetTaskDetails @TaskId = @taskId;

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
CREATE PROCEDURE dbo.CreateExportJob
@id VARCHAR (64), @hash VARCHAR (64), @status VARCHAR (10), @rawJobRecord VARCHAR (MAX)
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @heartbeatDateTime AS DATETIME2 (7) = SYSUTCDATETIME();
INSERT  INTO dbo.ExportJob (Id, Hash, Status, HeartbeatDateTime, RawJobRecord)
VALUES                    (@id, @hash, @status, @heartbeatDateTime, @rawJobRecord);
SELECT CAST (MIN_ACTIVE_ROWVERSION() AS INT);
COMMIT TRANSACTION;

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
CREATE PROCEDURE [dbo].[CreateTask_3]
@taskId VARCHAR (64), @queueId VARCHAR (64), @taskTypeId SMALLINT, @parentTaskId VARCHAR (64), @maxRetryCount SMALLINT=3, @inputData VARCHAR (MAX), @isUniqueTaskByType BIT
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @heartbeatDateTime AS DATETIME2 (7) = SYSUTCDATETIME();
DECLARE @status AS SMALLINT = 1;
DECLARE @retryCount AS SMALLINT = 0;
DECLARE @isCanceled AS BIT = 0;
IF (@isUniqueTaskByType = 1)
    BEGIN
        IF EXISTS (SELECT *
                   FROM   [dbo].[TaskInfo]
                   WHERE  TaskId = @taskId
                          OR (TaskTypeId = @taskTypeId
                              AND Status <> 3))
            BEGIN
                THROW 50409, 'Task already existed', 1;
            END
    END
ELSE
    BEGIN
        IF EXISTS (SELECT *
                   FROM   [dbo].[TaskInfo]
                   WHERE  TaskId = @taskId)
            BEGIN
                THROW 50409, 'Task already existed', 1;
            END
    END
INSERT  INTO [dbo].[TaskInfo] (TaskId, QueueId, Status, TaskTypeId, IsCanceled, RetryCount, MaxRetryCount, HeartbeatDateTime, InputData, ParentTaskId)
VALUES                       (@taskId, @queueId, @status, @taskTypeId, @isCanceled, @retryCount, @maxRetryCount, @heartbeatDateTime, @inputData, @parentTaskId);
EXECUTE dbo.GetTaskDetails @TaskId = @taskId;
COMMIT TRANSACTION;

GO
CREATE PROCEDURE dbo.Defrag
@TableName VARCHAR (100), @IndexName VARCHAR (200), @PartitionNumber INT, @IsPartitioned BIT
WITH EXECUTE AS 'dbo'
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'Defrag', @Mode AS VARCHAR (200) = @TableName + '.' + @IndexName + '.' + CONVERT (VARCHAR, @PartitionNumber) + '.' + CONVERT (VARCHAR, @IsPartitioned), @st AS DATETIME = getUTCdate(), @SQL AS VARCHAR (3500), @msg AS VARCHAR (1000), @SizeBefore AS FLOAT, @SizeAfter AS FLOAT, @IndexId AS INT;
BEGIN TRY
    SET @IndexId = (SELECT index_id
                    FROM   sys.indexes
                    WHERE  object_id = object_id(@TableName)
                           AND name = @IndexName);
    SET @SizeBefore = (SELECT sum(reserved_page_count)
                       FROM   sys.dm_db_partition_stats
                       WHERE  object_id = object_id(@TableName)
                              AND index_id = @IndexId) * 8.0 / 1024 / 1024;
    SET @msg = 'Size[GB] before=' + CONVERT (VARCHAR, @SizeBefore);
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Start', @Text = @msg;
    SET @Sql = 'ALTER INDEX ' + quotename(@IndexName) + ' ON dbo.' + quotename(@TableName) + ' REORGANIZE' + CASE WHEN @IsPartitioned = 1 THEN ' PARTITION = ' + CONVERT (VARCHAR, @PartitionNumber) ELSE '' END;
    BEGIN TRY
        EXECUTE (@Sql);
        SET @SizeAfter = (SELECT sum(reserved_page_count)
                          FROM   sys.dm_db_partition_stats
                          WHERE  object_id = object_id(@TableName)
                                 AND index_id = @IndexId) * 8.0 / 1024 / 1024;
        SET @msg = 'Size[GB] before=' + CONVERT (VARCHAR, @SizeBefore) + ', after=' + CONVERT (VARCHAR, @SizeAfter) + ', reduced by=' + CONVERT (VARCHAR, @SizeBefore - @SizeAfter);
        EXECUTE dbo.LogEvent @Process = @SP, @Status = 'End', @Mode = @Mode, @Action = 'Reorganize', @Start = @st, @Text = @msg;
    END TRY
    BEGIN CATCH
        EXECUTE dbo.LogEvent @Process = @SP, @Status = 'Error', @Mode = @Mode, @Action = 'Reorganize', @Start = @st, @ReRaisError = 0;
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
CREATE PROCEDURE dbo.DeleteHistory
@DeleteResources BIT=0, @Reset BIT=0
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'DeleteHistory', @Mode AS VARCHAR (100) = 'D=' + isnull(CONVERT (VARCHAR, @DeleteResources), 'NULL') + ' R=' + isnull(CONVERT (VARCHAR, @Reset), 'NULL'), @st AS DATETIME = getUTCdate(), @Rows AS INT = 0, @ResourceRows AS INT = 0, @ResourceTypeId AS SMALLINT, @SurrogateId AS BIGINT, @RowsToProcess AS INT, @Id AS VARCHAR (100) = 'DeleteHistory.LastProcessed.TypeId.SurrogateId';
BEGIN TRY
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
    SET @ResourceTypeId = (SELECT value
                           FROM   string_split (@LastProcessed, '.', 1)
                           WHERE  ordinal = 1);
    SET @SurrogateId = (SELECT value
                        FROM   string_split (@LastProcessed, '.', 1)
                        WHERE  ordinal = 2);
    DELETE @Types
    WHERE  ResourceTypeId < @ResourceTypeId;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Run', @Target = '@Types', @Action = 'Delete', @Rows = @@rowcount;
    WHILE EXISTS (SELECT *
                  FROM   @Types)
        BEGIN
            SET @ResourceTypeId = (SELECT   TOP 1 ResourceTypeId
                                   FROM     @Types
                                   ORDER BY ResourceTypeId);
            SET @RowsToProcess = 1;
            WHILE @RowsToProcess > 0
                BEGIN
                    DELETE @SurrogateIds;
                    INSERT INTO @SurrogateIds
                    SELECT   TOP 1000000 ResourceSurrogateId,
                                         IsHistory
                    FROM     dbo.Resource
                    WHERE    ResourceTypeId = @ResourceTypeId
                             AND ResourceSurrogateId > @SurrogateId
                    ORDER BY ResourceSurrogateId;
                    SET @RowsToProcess = @@rowcount;
                    IF @RowsToProcess > 0
                        SET @SurrogateId = (SELECT max(ResourceSurrogateId)
                                            FROM   @SurrogateIds);
                    SET @LastProcessed = CONVERT (VARCHAR, @ResourceTypeId) + '.' + CONVERT (VARCHAR, @SurrogateId);
                    DELETE @SurrogateIds
                    WHERE  IsHistory = 0;
                    SET @Rows = 0;
                    IF EXISTS (SELECT *
                               FROM   @SurrogateIds)
                        BEGIN
                            DELETE dbo.ResourceWriteClaim
                            WHERE  ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                           FROM   @SurrogateIds);
                            SET @Rows += @@rowcount;
                            DELETE dbo.CompartmentAssignment
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @Rows += @@rowcount;
                            DELETE dbo.ReferenceSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @Rows += @@rowcount;
                            DELETE dbo.TokenSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @Rows += @@rowcount;
                            DELETE dbo.TokenText
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @Rows += @@rowcount;
                            DELETE dbo.StringSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @Rows += @@rowcount;
                            DELETE dbo.UriSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @Rows += @@rowcount;
                            DELETE dbo.NumberSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @Rows += @@rowcount;
                            DELETE dbo.QuantitySearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @Rows += @@rowcount;
                            DELETE dbo.DateTimeSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @Rows += @@rowcount;
                            DELETE dbo.ReferenceTokenCompositeSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @Rows += @@rowcount;
                            DELETE dbo.TokenTokenCompositeSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @Rows += @@rowcount;
                            DELETE dbo.TokenDateTimeCompositeSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @Rows += @@rowcount;
                            DELETE dbo.TokenQuantityCompositeSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @Rows += @@rowcount;
                            DELETE dbo.TokenStringCompositeSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @Rows += @@rowcount;
                            DELETE dbo.TokenNumberNumberCompositeSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @Rows += @@rowcount;
                            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Run', @Target = '*SearchParam', @Action = 'Delete', @Rows = @Rows, @Text = @LastProcessed;
                            IF @DeleteResources = 1
                                BEGIN
                                    DELETE dbo.Resource
                                    WHERE  ResourceTypeId = @ResourceTypeId
                                           AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                                       FROM   @SurrogateIds);
                                    SET @ResourceRows = @@rowcount;
                                    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Run', @Target = 'Resource', @Action = 'Delete', @Rows = @ResourceRows, @Text = @LastProcessed;
                                END
                        END
                    SET @Rows += @ResourceRows;
                    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Run', @Target = 'Total', @Action = 'Delete', @Rows = @Rows, @Text = @LastProcessed;
                    UPDATE dbo.Parameters
                    SET    Char = @LastProcessed
                    WHERE  Id = @Id;
                END
            DELETE @Types
            WHERE  ResourceTypeId = @ResourceTypeId;
            SET @SurrogateId = 0;
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
CREATE PROCEDURE dbo.DequeueJob
@QueueType TINYINT, @Worker VARCHAR (100), @HeartbeatTimeoutSec INT, @InputJobId BIGINT=NULL
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'DequeueJob', @Mode AS VARCHAR (100) = 'Q=' + isnull(CONVERT (VARCHAR, @QueueType), 'NULL') + ' H=' + isnull(CONVERT (VARCHAR, @HeartbeatTimeoutSec), 'NULL') + ' W=' + isnull(@Worker, 'NULL') + ' IJ=' + isnull(CONVERT (VARCHAR, @InputJobId), 'NULL'), @Rows AS INT = 0, @st AS DATETIME = getUTCdate(), @JobId AS BIGINT, @msg AS VARCHAR (100), @Lock AS VARCHAR (100), @PartitionId AS TINYINT, @MaxPartitions AS TINYINT = 16, @LookedAtPartitions AS TINYINT = 0;
BEGIN TRY
    IF EXISTS (SELECT *
               FROM   dbo.Parameters
               WHERE  Id = 'DequeueJobStop'
                      AND Number = 1)
        RETURN;
    IF @InputJobId IS NULL
        SET @PartitionId = @MaxPartitions * rand();
    ELSE
        SET @PartitionId = @InputJobId % 16;
    SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
    WHILE @InputJobId IS NULL
          AND @JobId IS NULL
          AND @LookedAtPartitions <= @MaxPartitions
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
          AND @LookedAtPartitions <= @MaxPartitions
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
CREATE PROCEDURE dbo.EnqueueJobs
@QueueType TINYINT, @Definitions StringList READONLY, @GroupId BIGINT=NULL, @ForceOneActiveJobGroup BIT=1, @IsCompleted BIT=NULL, @ReturnJobs BIT=1
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'EnqueueJobs', @Mode AS VARCHAR (100) = 'Q=' + isnull(CONVERT (VARCHAR, @QueueType), 'NULL') + ' D=' + CONVERT (VARCHAR, (SELECT count(*)
                                                                                                                                                         FROM   @Definitions)) + ' G=' + isnull(CONVERT (VARCHAR, @GroupId), 'NULL') + ' F=' + isnull(CONVERT (VARCHAR, @ForceOneActiveJobGroup), 'NULL') + ' C=' + isnull(CONVERT (VARCHAR, @IsCompleted), 'NULL'), @st AS DATETIME = getUTCdate(), @Lock AS VARCHAR (100) = 'EnqueueJobs_' + CONVERT (VARCHAR, @QueueType), @MaxJobId AS BIGINT, @Rows AS INT, @msg AS VARCHAR (1000), @JobIds AS BigintList, @InputRows AS INT;
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
            INSERT INTO dbo.JobQueue (QueueType, GroupId, JobId, Definition, DefinitionHash, Status)
            OUTPUT inserted.JobId INTO @JobIds
            SELECT @QueueType,
                   isnull(@GroupId, @MaxJobId + 1) AS GroupId,
                   JobId,
                   Definition,
                   DefinitionHash,
                   CASE WHEN @IsCompleted = 1 THEN 2 ELSE 0 END AS Status
            FROM   (SELECT @MaxJobId + row_number() OVER (ORDER BY Dummy) AS JobId,
                           *
                    FROM   (SELECT *,
                                   0 AS Dummy
                            FROM   @Input) AS A) AS A
            WHERE  NOT EXISTS (SELECT *
                               FROM   dbo.JobQueue AS B
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
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st, @ReRaisError = 0, @Retry = @Retries;
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
    WHILE @LookedAtPartitions <= @MaxPartitions
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
CREATE PROCEDURE dbo.GetExportJobByHash
@hash VARCHAR (64)
AS
SET NOCOUNT ON;
SELECT   TOP (1) RawJobRecord,
                 JobVersion
FROM     dbo.ExportJob
WHERE    Hash = @hash
         AND (Status = 'Queued'
              OR Status = 'Running')
ORDER BY HeartbeatDateTime ASC;

GO
CREATE PROCEDURE dbo.GetExportJobById
@id VARCHAR (64)
AS
SET NOCOUNT ON;
SELECT RawJobRecord,
       JobVersion
FROM   dbo.ExportJob
WHERE  Id = @id;

GO
CREATE PROCEDURE [dbo].[GetImportProcessingTaskResult]
@queueId VARCHAR (64), @importTaskId VARCHAR (64)
AS
SET NOCOUNT ON;
SELECT Result
FROM   [dbo].[TaskInfo] WITH (INDEX (IX_QueueId_ParentTaskId))
WHERE  ParentTaskId = @importTaskId
       AND TaskTypeId = 1
       AND Status = 3;

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
CREATE PROCEDURE dbo.GetNextTask_3
@queueId VARCHAR (64), @taskHeartbeatTimeoutThresholdInSeconds INT=600
AS
SET NOCOUNT ON;
DECLARE @lock AS VARCHAR (200) = 'GetNextTask_Q=' + @queueId, @taskId AS VARCHAR (64) = NULL, @expirationDateTime AS DATETIME2 (7), @startDateTime AS DATETIME2 (7) = SYSUTCDATETIME();
SET @expirationDateTime = DATEADD(second, -@taskHeartbeatTimeoutThresholdInSeconds, @startDateTime);
BEGIN TRY
    BEGIN TRANSACTION;
    EXECUTE sp_getapplock @lock, 'Exclusive';
    UPDATE T
    SET    Status            = 2,
           StartDateTime     = @startDateTime,
           HeartbeatDateTime = @startDateTime,
           Worker            = host_name(),
           RunId             = NEWID(),
           @taskId           = T.TaskId
    FROM   dbo.TaskInfo AS T WITH (PAGLOCK)
           INNER JOIN
           (SELECT   TOP 1 TaskId
            FROM     dbo.TaskInfo WITH (INDEX (IX_QueueId_Status))
            WHERE    QueueId = @queueId
                     AND Status = 1
            ORDER BY TaskId) AS S
           ON T.QueueId = @queueId
              AND T.TaskId = S.TaskId;
    IF @taskId IS NULL
        UPDATE T
        SET    StartDateTime     = @startDateTime,
               HeartbeatDateTime = @startDateTime,
               Worker            = host_name(),
               RunId             = NEWID(),
               @taskId           = T.TaskId,
               RestartInfo       = ISNULL(RestartInfo, '') + ' Prev: Worker=' + Worker + ' Start=' + CONVERT (VARCHAR, @startDateTime, 121)
        FROM   dbo.TaskInfo AS T WITH (PAGLOCK)
               INNER JOIN
               (SELECT   TOP 1 TaskId
                FROM     dbo.TaskInfo WITH (INDEX (IX_QueueId_Status))
                WHERE    QueueId = @queueId
                         AND Status = 2
                         AND HeartbeatDateTime <= @expirationDateTime
                ORDER BY TaskId) AS S
               ON T.QueueId = @queueId
                  AND T.TaskId = S.TaskId;
    COMMIT TRANSACTION;
    EXECUTE dbo.GetTaskDetails @TaskId = @taskId;
END TRY
BEGIN CATCH
    IF @@trancount > 0
        ROLLBACK TRANSACTION THROW;
END CATCH

GO
CREATE OR ALTER PROCEDURE dbo.GetNonCompletedJobCountOfSpecificQueueType
@queueType TINYINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT COUNT(*)
    FROM   dbo.JobQueue
    WHERE  QueueType = @queueType
           AND (Status = 0
                OR Status = 1);
END

GO
CREATE PROCEDURE dbo.GetPartitionedTables
@IncludeNotDisabled BIT, @IncludeNotSupported BIT
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
CREATE PROCEDURE dbo.GetResourcesByTypeAndSurrogateIdRange
@ResourceTypeId SMALLINT, @StartId BIGINT, @EndId BIGINT, @GlobalStartId BIGINT=NULL, @GlobalEndId BIGINT=NULL
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'GetResourcesByTypeAndSurrogateIdRange', @Mode AS VARCHAR (100) = 'RT=' + isnull(CONVERT (VARCHAR, @ResourceTypeId), 'NULL') + ' S=' + isnull(CONVERT (VARCHAR, @StartId), 'NULL') + ' E=' + isnull(CONVERT (VARCHAR, @EndId), 'NULL') + ' GS=' + isnull(CONVERT (VARCHAR, @GlobalStartId), 'NULL') + ' GE=' + isnull(CONVERT (VARCHAR, @GlobalEndId), 'NULL'), @st AS DATETIME = getUTCdate();
BEGIN TRY
    DECLARE @ResourceIds TABLE (
        ResourceId          VARCHAR (64) COLLATE Latin1_General_100_CS_AS,
        ResourceSurrogateId BIGINT      ,
        RowId               INT         ,
        PRIMARY KEY (ResourceId, RowId));
    IF @GlobalStartId IS NULL
        SET @GlobalStartId = 0;
    IF @GlobalEndId IS NOT NULL
        INSERT INTO @ResourceIds
        SELECT ResourceId,
               ResourceSurrogateId,
               row_number() OVER (PARTITION BY ResourceId ORDER BY ResourceSurrogateId) AS RowId
        FROM   dbo.Resource
        WHERE  ResourceTypeId = @ResourceTypeId
               AND ResourceId IN (SELECT DISTINCT ResourceId
                                  FROM   dbo.Resource
                                  WHERE  ResourceTypeId = @ResourceTypeId
                                         AND ResourceSurrogateId BETWEEN @StartId AND @EndId
                                         AND IsHistory = 1)
               AND ResourceSurrogateId BETWEEN @GlobalStartId AND @GlobalEndId;
    IF EXISTS (SELECT *
               FROM   @ResourceIds)
        BEGIN
            DECLARE @SurrogateIdMap TABLE (
                MinSurrogateId BIGINT,
                MaxSurrogateId BIGINT);
            INSERT INTO @SurrogateIdMap
            SELECT A.ResourceSurrogateId AS MinSurrogateId,
                   C.ResourceSurrogateId AS MaxSurrogateId
            FROM   (SELECT *
                    FROM   @ResourceIds
                    WHERE  RowId = 1
                           AND ResourceSurrogateId BETWEEN @StartId AND @EndId) AS A CROSS APPLY (SELECT ResourceSurrogateId
                                                                                                  FROM   @ResourceIds AS B
                                                                                                  WHERE  B.ResourceId = A.ResourceId) AS C;
            SELECT @ResourceTypeId,
                   CASE WHEN C.ResourceSurrogateId IS NOT NULL THEN C.ResourceId ELSE A.ResourceId END,
                   CASE WHEN C.ResourceSurrogateId IS NOT NULL THEN C.Version ELSE A.Version END,
                   CASE WHEN C.ResourceSurrogateId IS NOT NULL THEN C.IsDeleted ELSE A.IsDeleted END,
                   isnull(C.ResourceSurrogateId, A.ResourceSurrogateId),
                   CASE WHEN C.ResourceSurrogateId IS NOT NULL THEN C.RequestMethod ELSE A.RequestMethod END,
                   CONVERT (BIT, 1) AS IsMatch,
                   CONVERT (BIT, 0) AS IsPartial,
                   CASE WHEN C.ResourceSurrogateId IS NOT NULL THEN C.IsRawResourceMetaSet ELSE A.IsRawResourceMetaSet END,
                   CASE WHEN C.ResourceSurrogateId IS NOT NULL THEN C.SearchParamHash ELSE A.SearchParamHash END,
                   CASE WHEN C.ResourceSurrogateId IS NOT NULL THEN C.RawResource ELSE A.RawResource END
            FROM   dbo.Resource AS A
                   LEFT OUTER JOIN
                   @SurrogateIdMap AS B
                   ON B.MinSurrogateId = A.ResourceSurrogateId
                   LEFT OUTER JOIN
                   dbo.Resource AS C
                   ON C.ResourceTypeId = @ResourceTypeId
                      AND C.ResourceSurrogateId = MaxSurrogateId
            WHERE  A.ResourceTypeId = @ResourceTypeId
                   AND A.ResourceSurrogateId BETWEEN @StartId AND @EndId
                   AND (A.IsHistory = 0
                        OR MaxSurrogateId IS NOT NULL);
        END
    ELSE
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
               AND IsHistory = 0
               AND IsDeleted = 0;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @@rowcount;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
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
CREATE PROCEDURE [dbo].[GetTaskDetails]
@taskId VARCHAR (64)
AS
SET NOCOUNT ON;
SELECT TaskId,
       QueueId,
       Status,
       TaskTypeId,
       RunId,
       IsCanceled,
       RetryCount,
       MaxRetryCount,
       HeartbeatDateTime,
       InputData,
       TaskContext,
       Result,
       ParentTaskId
FROM   [dbo].[TaskInfo]
WHERE  TaskId = @taskId;

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
CREATE PROCEDURE dbo.HardDeleteResource_2
@resourceTypeId SMALLINT, @resourceId VARCHAR (64), @keepCurrentVersion SMALLINT
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @resourceSurrogateIds TABLE (
    ResourceSurrogateId BIGINT NOT NULL);
DELETE dbo.Resource
OUTPUT deleted.ResourceSurrogateId INTO @resourceSurrogateIds
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceId = @resourceId
       AND NOT (@keepCurrentVersion = 1
                AND IsHistory = 0);
DELETE dbo.ResourceWriteClaim
WHERE  ResourceSurrogateId IN (SELECT ResourceSurrogateId
                               FROM   @resourceSurrogateIds);
DELETE dbo.CompartmentAssignment
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                   FROM   @resourceSurrogateIds);
DELETE dbo.ReferenceSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                   FROM   @resourceSurrogateIds);
DELETE dbo.TokenSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                   FROM   @resourceSurrogateIds);
DELETE dbo.TokenText
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                   FROM   @resourceSurrogateIds);
DELETE dbo.StringSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                   FROM   @resourceSurrogateIds);
DELETE dbo.UriSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                   FROM   @resourceSurrogateIds);
DELETE dbo.NumberSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                   FROM   @resourceSurrogateIds);
DELETE dbo.QuantitySearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                   FROM   @resourceSurrogateIds);
DELETE dbo.DateTimeSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                   FROM   @resourceSurrogateIds);
DELETE dbo.ReferenceTokenCompositeSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                   FROM   @resourceSurrogateIds);
DELETE dbo.TokenTokenCompositeSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                   FROM   @resourceSurrogateIds);
DELETE dbo.TokenDateTimeCompositeSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                   FROM   @resourceSurrogateIds);
DELETE dbo.TokenQuantityCompositeSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                   FROM   @resourceSurrogateIds);
DELETE dbo.TokenStringCompositeSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                   FROM   @resourceSurrogateIds);
DELETE dbo.TokenNumberNumberCompositeSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                   FROM   @resourceSurrogateIds);
COMMIT TRANSACTION;

GO
CREATE PROCEDURE dbo.InitDefrag
@QueueType TINYINT, @GroupId BIGINT, @DefragItems INT=NULL OUTPUT
WITH EXECUTE AS 'dbo'
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'InitDefrag', @st AS DATETIME = getUTCdate(), @ObjectId AS INT, @msg AS VARCHAR (1000), @Rows AS INT, @MinFragPct AS INT = isnull((SELECT Number
                                                                                                                                                                  FROM   dbo.Parameters
                                                                                                                                                                  WHERE  Id = 'Defrag.MinFragPct'), 10), @MinSizeGB AS FLOAT = isnull((SELECT Number
                                                                                                                                                                                                                                       FROM   dbo.Parameters
                                                                                                                                                                                                                                       WHERE  Id = 'Defrag.MinSizeGB'), 0.1), @DefinitionsSorted AS StringList;
DECLARE @Mode AS VARCHAR (200) = 'G=' + CONVERT (VARCHAR, @GroupId) + ' MF=' + CONVERT (VARCHAR, @MinFragPct) + ' MS=' + CONVERT (VARCHAR, @MinSizeGB);
DECLARE @Definitions AS TABLE (
    Def    VARCHAR (900) PRIMARY KEY,
    FragGB FLOAT        );
BEGIN TRY
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Start';
    SELECT *
    INTO   #filter
    FROM   (SELECT   object_id,
                     sum(reserved_page_count * 8.0 / 1024 / 1024) AS ReservedGB
            FROM     sys.dm_db_partition_stats AS A
            WHERE    object_id IN (SELECT object_id
                                   FROM   sys.objects
                                   WHERE  type = 'U'
                                          AND name NOT IN ('EventLog'))
            GROUP BY object_id) AS A
    WHERE  ReservedGB > @MinSizeGB;
    WHILE EXISTS (SELECT *
                  FROM   #filter)
        BEGIN
            SET @ObjectId = (SELECT   TOP 1 object_id
                             FROM     #filter
                             ORDER BY ReservedGB DESC);
            INSERT INTO @Definitions
            SELECT object_name(@ObjectId) + ';' + I.name + ';' + CONVERT (VARCHAR, partition_number) + ';' + CONVERT (VARCHAR, CASE WHEN EXISTS (SELECT *
                                                                                                                                                 FROM   sys.partition_schemes AS PS
                                                                                                                                                 WHERE  PS.data_space_id = I.data_space_id) THEN 1 ELSE 0 END) + ';' + CONVERT (VARCHAR, (SELECT sum(reserved_page_count)
                                                                                                                                                                                                                                          FROM   sys.dm_db_partition_stats AS S
                                                                                                                                                                                                                                          WHERE  S.object_id = A.object_id
                                                                                                                                                                                                                                                 AND S.index_id = A.index_id
                                                                                                                                                                                                                                                 AND S.partition_number = A.partition_number) * 8.0 / 1024 / 1024),
                   FragGB
            FROM   (SELECT object_id,
                           index_id,
                           partition_number,
                           A.avg_fragmentation_in_percent * A.page_count * 8.0 / 1024 / 1024 / 100 AS FragGB
                    FROM   sys.dm_db_index_physical_stats(db_id(), @ObjectId, NULL, NULL, 'LIMITED') AS A
                    WHERE  index_id > 0
                           AND avg_fragmentation_in_percent >= @MinFragPct
                           AND A.page_count > 500) AS A
                   INNER JOIN
                   sys.indexes AS I
                   ON I.object_id = A.object_id
                      AND I.index_id = A.index_id;
            SET @Rows = @@rowcount;
            SET @msg = object_name(@ObjectId);
            EXECUTE dbo.LogEvent @Process = @SP, @Status = 'Run', @Mode = @Mode, @Target = '@Definitions', @Action = 'Insert', @Rows = @Rows, @Text = @msg;
            DELETE #filter
            WHERE  object_id = @ObjectId;
        END
    INSERT INTO @DefinitionsSorted
    SELECT   Def + ';' + CONVERT (VARCHAR, FragGB)
    FROM     @Definitions
    ORDER BY FragGB DESC;
    SET @DefragItems = @@rowcount;
    IF @DefragItems > 0
        EXECUTE dbo.EnqueueJobs @QueueType = @QueueType, @Definitions = @DefinitionsSorted, @GroupId = @GroupId, @ForceOneActiveJobGroup = 1;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
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
@QueueType TINYINT, @JobId BIGINT, @Version BIGINT, @Data BIGINT=NULL, @CurrentResult VARCHAR (MAX)=NULL, @CancelRequested BIT=0 OUTPUT
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'PutJobHeartbeat', @Mode AS VARCHAR (100), @st AS DATETIME = getUTCdate(), @Rows AS INT = 0, @PartitionId AS TINYINT = @JobId % 16;
SET @Mode = 'Q=' + CONVERT (VARCHAR, @QueueType) + ' J=' + CONVERT (VARCHAR, @JobId) + ' P=' + CONVERT (VARCHAR, @PartitionId) + ' V=' + CONVERT (VARCHAR, @Version) + ' D=' + isnull(CONVERT (VARCHAR, @Data), 'NULL');
BEGIN TRY
    IF @CurrentResult IS NULL
        UPDATE dbo.JobQueue
        SET    @CancelRequested = CancelRequested,
               HeartbeatDate    = getUTCdate(),
               Data             = isnull(@Data, Data)
        WHERE  QueueType = @QueueType
               AND PartitionId = @PartitionId
               AND JobId = @JobId
               AND Status = 1
               AND Version = @Version;
    ELSE
        UPDATE dbo.JobQueue
        SET    @CancelRequested = CancelRequested,
               HeartbeatDate    = getUTCdate(),
               Data             = isnull(@Data, Data),
               Result           = @CurrentResult
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
CREATE PROCEDURE dbo.ReadResource
@resourceTypeId SMALLINT, @resourceId VARCHAR (64), @version INT=NULL
AS
SET NOCOUNT ON;
IF (@version IS NULL)
    BEGIN
        SELECT ResourceSurrogateId,
               Version,
               IsDeleted,
               IsHistory,
               RawResource,
               IsRawResourceMetaSet,
               SearchParamHash
        FROM   dbo.Resource
        WHERE  ResourceTypeId = @resourceTypeId
               AND ResourceId = @resourceId
               AND IsHistory = 0;
    END
ELSE
    BEGIN
        SELECT ResourceSurrogateId,
               Version,
               IsDeleted,
               IsHistory,
               RawResource,
               IsRawResourceMetaSet,
               SearchParamHash
        FROM   dbo.Resource
        WHERE  ResourceTypeId = @resourceTypeId
               AND ResourceId = @resourceId
               AND Version = @version;
    END

GO
CREATE PROCEDURE dbo.ReindexResource_2
@resourceTypeId SMALLINT, @resourceId VARCHAR (64), @eTag INT=NULL, @searchParamHash VARCHAR (64), @resourceWriteClaims dbo.BulkResourceWriteClaimTableType_1 READONLY, @compartmentAssignments dbo.BulkCompartmentAssignmentTableType_1 READONLY, @referenceSearchParams dbo.BulkReferenceSearchParamTableType_1 READONLY, @tokenSearchParams dbo.BulkTokenSearchParamTableType_2 READONLY, @tokenTextSearchParams dbo.BulkTokenTextTableType_1 READONLY, @stringSearchParams dbo.BulkStringSearchParamTableType_2 READONLY, @numberSearchParams dbo.BulkNumberSearchParamTableType_1 READONLY, @quantitySearchParams dbo.BulkQuantitySearchParamTableType_1 READONLY, @uriSearchParams dbo.BulkUriSearchParamTableType_1 READONLY, @dateTimeSearchParms dbo.BulkDateTimeSearchParamTableType_2 READONLY, @referenceTokenCompositeSearchParams dbo.BulkReferenceTokenCompositeSearchParamTableType_2 READONLY, @tokenTokenCompositeSearchParams dbo.BulkTokenTokenCompositeSearchParamTableType_2 READONLY, @tokenDateTimeCompositeSearchParams dbo.BulkTokenDateTimeCompositeSearchParamTableType_2 READONLY, @tokenQuantityCompositeSearchParams dbo.BulkTokenQuantityCompositeSearchParamTableType_2 READONLY, @tokenStringCompositeSearchParams dbo.BulkTokenStringCompositeSearchParamTableType_2 READONLY, @tokenNumberNumberCompositeSearchParams dbo.BulkTokenNumberNumberCompositeSearchParamTableType_2 READONLY
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @resourceSurrogateId AS BIGINT;
DECLARE @version AS BIGINT;
SELECT @resourceSurrogateId = ResourceSurrogateId,
       @version = Version
FROM   dbo.Resource WITH (UPDLOCK, HOLDLOCK)
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceId = @resourceId
       AND IsHistory = 0;
IF (@etag IS NOT NULL
    AND @etag <> @version)
    BEGIN
        THROW 50412, 'Precondition failed', 1;
    END
UPDATE dbo.Resource
SET    SearchParamHash = @searchParamHash
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.ResourceWriteClaim
WHERE  ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.CompartmentAssignment
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.ReferenceSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.TokenSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.TokenText
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.StringSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.UriSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.NumberSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.QuantitySearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.DateTimeSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.ReferenceTokenCompositeSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.TokenTokenCompositeSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.TokenDateTimeCompositeSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.TokenQuantityCompositeSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.TokenStringCompositeSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.TokenNumberNumberCompositeSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
INSERT INTO dbo.ResourceWriteClaim (ResourceSurrogateId, ClaimTypeId, ClaimValue)
SELECT @resourceSurrogateId,
       ClaimTypeId,
       ClaimValue
FROM   @resourceWriteClaims;
INSERT INTO dbo.CompartmentAssignment (ResourceTypeId, ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                CompartmentTypeId,
                ReferenceResourceId,
                0
FROM   @compartmentAssignments;
INSERT INTO dbo.ReferenceSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                BaseUri,
                ReferenceResourceTypeId,
                ReferenceResourceId,
                ReferenceResourceVersion,
                0
FROM   @referenceSearchParams;
INSERT INTO dbo.TokenSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SystemId,
                Code,
                CodeOverflow,
                0
FROM   @tokenSearchParams;
INSERT INTO dbo.TokenText (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                Text,
                0
FROM   @tokenTextSearchParams;
INSERT INTO dbo.StringSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsHistory, IsMin, IsMax)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                Text,
                TextOverflow,
                0,
                IsMin,
                IsMax
FROM   @stringSearchParams;
INSERT INTO dbo.UriSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                Uri,
                0
FROM   @uriSearchParams;
INSERT INTO dbo.NumberSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SingleValue,
                LowValue,
                HighValue,
                0
FROM   @numberSearchParams;
INSERT INTO dbo.QuantitySearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SystemId,
                QuantityCodeId,
                SingleValue,
                LowValue,
                HighValue,
                0
FROM   @quantitySearchParams;
INSERT INTO dbo.DateTimeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsHistory, IsMin, IsMax)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                StartDateTime,
                EndDateTime,
                IsLongerThanADay,
                0,
                IsMin,
                IsMax
FROM   @dateTimeSearchParms;
INSERT INTO dbo.ReferenceTokenCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                BaseUri1,
                ReferenceResourceTypeId1,
                ReferenceResourceId1,
                ReferenceResourceVersion1,
                SystemId2,
                Code2,
                CodeOverflow2,
                0
FROM   @referenceTokenCompositeSearchParams;
INSERT INTO dbo.TokenTokenCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SystemId1,
                Code1,
                CodeOverflow1,
                SystemId2,
                Code2,
                CodeOverflow2,
                0
FROM   @tokenTokenCompositeSearchParams;
INSERT INTO dbo.TokenDateTimeCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SystemId1,
                Code1,
                CodeOverflow1,
                StartDateTime2,
                EndDateTime2,
                IsLongerThanADay2,
                0
FROM   @tokenDateTimeCompositeSearchParams;
INSERT INTO dbo.TokenQuantityCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SystemId1,
                Code1,
                CodeOverflow1,
                SingleValue2,
                SystemId2,
                QuantityCodeId2,
                LowValue2,
                HighValue2,
                0
FROM   @tokenQuantityCompositeSearchParams;
INSERT INTO dbo.TokenStringCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SystemId1,
                Code1,
                CodeOverflow1,
                Text2,
                TextOverflow2,
                0
FROM   @tokenStringCompositeSearchParams;
INSERT INTO dbo.TokenNumberNumberCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
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
                HasRange,
                0
FROM   @tokenNumberNumberCompositeSearchParams;
COMMIT TRANSACTION;

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
CREATE PROCEDURE dbo.ResetTask_2
@taskId VARCHAR (64), @runId VARCHAR (50), @result VARCHAR (MAX)
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @retryCount AS SMALLINT = NULL;
IF NOT EXISTS (SELECT *
               FROM   dbo.TaskInfo
               WHERE  TaskId = @taskId
                      AND RunId = @runId)
    BEGIN
        THROW 50404, 'Task not exist or runid not match', 1;
    END
UPDATE dbo.TaskInfo
SET    Status      = 3,
       EndDateTime = SYSUTCDATETIME(),
       Result      = @result,
       @retryCount = retryCount
WHERE  TaskId = @taskId
       AND RunId = @runId
       AND (MaxRetryCount <> -1
            AND RetryCount >= MaxRetryCount);
IF @retryCount IS NULL
    UPDATE dbo.TaskInfo
    SET    Status      = 1,
           Result      = @result,
           RetryCount  = RetryCount + 1,
           RestartInfo = ISNULL(RestartInfo, '') + ' Prev: Worker=' + Worker + ' Start=' + CONVERT (VARCHAR, StartDateTime, 121)
    WHERE  TaskId = @taskId
           AND RunId = @runId
           AND Status <> 3
           AND (MaxRetryCount = -1
                OR RetryCount < MaxRetryCount);
EXECUTE dbo.GetTaskDetails @TaskId = @taskId;

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
CREATE PROCEDURE [dbo].[TaskKeepAlive]
@taskId VARCHAR (64), @runId VARCHAR (50)
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT *
               FROM   [dbo].[TaskInfo]
               WHERE  TaskId = @taskId
                      AND RunId = @runId)
    BEGIN
        THROW 50404, 'Task not exist or runid not match', 1;
    END
DECLARE @heartbeatDateTime AS DATETIME2 (7) = SYSUTCDATETIME();
UPDATE dbo.TaskInfo
SET    HeartbeatDateTime = @heartbeatDateTime
WHERE  TaskId = @taskId;
SELECT TaskId,
       QueueId,
       Status,
       TaskTypeId,
       RunId,
       IsCanceled,
       RetryCount,
       MaxRetryCount,
       HeartbeatDateTime,
       InputData,
       TaskContext,
       Result
FROM   [dbo].[TaskInfo]
WHERE  TaskId = @taskId;
COMMIT TRANSACTION;

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
CREATE PROCEDURE dbo.UpdateExportJob
@id VARCHAR (64), @status VARCHAR (10), @rawJobRecord VARCHAR (MAX), @jobVersion BINARY (8)
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @currentJobVersion AS BINARY (8);
SELECT @currentJobVersion = JobVersion
FROM   dbo.ExportJob WITH (UPDLOCK, HOLDLOCK)
WHERE  Id = @id;
IF (@currentJobVersion IS NULL)
    BEGIN
        THROW 50404, 'Export job record not found', 1;
    END
IF (@jobVersion <> @currentJobVersion)
    BEGIN
        THROW 50412, 'Precondition failed', 1;
    END
DECLARE @heartbeatDateTime AS DATETIME2 (7) = SYSUTCDATETIME();
UPDATE dbo.ExportJob
SET    Status            = @status,
       HeartbeatDateTime = @heartbeatDateTime,
       RawJobRecord      = @rawJobRecord
WHERE  Id = @id;
SELECT @@DBTS;
COMMIT TRANSACTION;

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
CREATE PROCEDURE [dbo].[UpdateTaskContext]
@taskId VARCHAR (64), @taskContext VARCHAR (MAX), @runId VARCHAR (50)
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT *
               FROM   [dbo].[TaskInfo]
               WHERE  TaskId = @taskId
                      AND RunId = @runId)
    BEGIN
        THROW 50404, 'Task not exist or runid not match', 1;
    END
DECLARE @heartbeatDateTime AS DATETIME2 (7) = SYSUTCDATETIME();
UPDATE dbo.TaskInfo
SET    HeartbeatDateTime = @heartbeatDateTime,
       TaskContext       = @taskContext
WHERE  TaskId = @taskId;
SELECT TaskId,
       QueueId,
       Status,
       TaskTypeId,
       RunId,
       IsCanceled,
       RetryCount,
       MaxRetryCount,
       HeartbeatDateTime,
       InputData,
       TaskContext,
       Result
FROM   [dbo].[TaskInfo]
WHERE  TaskId = @taskId;
COMMIT TRANSACTION;

GO
CREATE PROCEDURE dbo.UpsertResource_7
@baseResourceSurrogateId BIGINT, @resourceTypeId SMALLINT, @resourceId VARCHAR (64), @eTag INT=NULL, @allowCreate BIT, @isDeleted BIT, @keepHistory BIT, @requireETagOnUpdate BIT, @requestMethod VARCHAR (10), @searchParamHash VARCHAR (64), @rawResource VARBINARY (MAX), @resourceWriteClaims dbo.BulkResourceWriteClaimTableType_1 READONLY, @compartmentAssignments dbo.BulkCompartmentAssignmentTableType_1 READONLY, @referenceSearchParams dbo.BulkReferenceSearchParamTableType_1 READONLY, @tokenSearchParams dbo.BulkTokenSearchParamTableType_2 READONLY, @tokenTextSearchParams dbo.BulkTokenTextTableType_1 READONLY, @stringSearchParams dbo.BulkStringSearchParamTableType_2 READONLY, @numberSearchParams dbo.BulkNumberSearchParamTableType_1 READONLY, @quantitySearchParams dbo.BulkQuantitySearchParamTableType_1 READONLY, @uriSearchParams dbo.BulkUriSearchParamTableType_1 READONLY, @dateTimeSearchParms dbo.BulkDateTimeSearchParamTableType_2 READONLY, @referenceTokenCompositeSearchParams dbo.BulkReferenceTokenCompositeSearchParamTableType_2 READONLY, @tokenTokenCompositeSearchParams dbo.BulkTokenTokenCompositeSearchParamTableType_2 READONLY, @tokenDateTimeCompositeSearchParams dbo.BulkTokenDateTimeCompositeSearchParamTableType_2 READONLY, @tokenQuantityCompositeSearchParams dbo.BulkTokenQuantityCompositeSearchParamTableType_2 READONLY, @tokenStringCompositeSearchParams dbo.BulkTokenStringCompositeSearchParamTableType_2 READONLY, @tokenNumberNumberCompositeSearchParams dbo.BulkTokenNumberNumberCompositeSearchParamTableType_2 READONLY, @isResourceChangeCaptureEnabled BIT=0, @comparedVersion INT=NULL
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @previousResourceSurrogateId AS BIGINT, @previousVersion AS BIGINT, @previousIsDeleted AS BIT, @version AS INT, @resourceSurrogateId AS BIGINT, @InitialTranCount AS INT = @@trancount;
IF @InitialTranCount = 0
    BEGIN TRANSACTION;
SELECT @previousResourceSurrogateId = ResourceSurrogateId,
       @previousVersion = Version,
       @previousIsDeleted = IsDeleted
FROM   dbo.Resource WITH (UPDLOCK, HOLDLOCK)
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceId = @resourceId
       AND IsHistory = 0;
IF @previousResourceSurrogateId IS NULL
    SET @version = 1;
ELSE
    BEGIN
        IF @isDeleted = 0
            BEGIN
                IF @comparedVersion IS NULL
                   OR @comparedVersion <> @previousVersion
                    BEGIN
                        THROW 50409, 'Resource has been recently updated or added, please compare the resource content in code for any duplicate updates', 1;
                    END
            END
        SET @version = @previousVersion + 1;
        IF @keepHistory = 1
            UPDATE dbo.Resource
            SET    IsHistory = 1
            WHERE  ResourceTypeId = @resourceTypeId
                   AND ResourceSurrogateId = @previousResourceSurrogateId;
        ELSE
            DELETE dbo.Resource
            WHERE  ResourceTypeId = @resourceTypeId
                   AND ResourceSurrogateId = @previousResourceSurrogateId;
        DELETE dbo.ResourceWriteClaim
        WHERE  ResourceSurrogateId = @previousResourceSurrogateId;
        DELETE dbo.CompartmentAssignment
        WHERE  ResourceTypeId = @resourceTypeId
               AND ResourceSurrogateId = @previousResourceSurrogateId;
        DELETE dbo.ReferenceSearchParam
        WHERE  ResourceTypeId = @resourceTypeId
               AND ResourceSurrogateId = @previousResourceSurrogateId;
        DELETE dbo.TokenSearchParam
        WHERE  ResourceTypeId = @resourceTypeId
               AND ResourceSurrogateId = @previousResourceSurrogateId;
        DELETE dbo.TokenText
        WHERE  ResourceTypeId = @resourceTypeId
               AND ResourceSurrogateId = @previousResourceSurrogateId;
        DELETE dbo.StringSearchParam
        WHERE  ResourceTypeId = @resourceTypeId
               AND ResourceSurrogateId = @previousResourceSurrogateId;
        DELETE dbo.UriSearchParam
        WHERE  ResourceTypeId = @resourceTypeId
               AND ResourceSurrogateId = @previousResourceSurrogateId;
        DELETE dbo.NumberSearchParam
        WHERE  ResourceTypeId = @resourceTypeId
               AND ResourceSurrogateId = @previousResourceSurrogateId;
        DELETE dbo.QuantitySearchParam
        WHERE  ResourceTypeId = @resourceTypeId
               AND ResourceSurrogateId = @previousResourceSurrogateId;
        DELETE dbo.DateTimeSearchParam
        WHERE  ResourceTypeId = @resourceTypeId
               AND ResourceSurrogateId = @previousResourceSurrogateId;
        DELETE dbo.ReferenceTokenCompositeSearchParam
        WHERE  ResourceTypeId = @resourceTypeId
               AND ResourceSurrogateId = @previousResourceSurrogateId;
        DELETE dbo.TokenTokenCompositeSearchParam
        WHERE  ResourceTypeId = @resourceTypeId
               AND ResourceSurrogateId = @previousResourceSurrogateId;
        DELETE dbo.TokenDateTimeCompositeSearchParam
        WHERE  ResourceTypeId = @resourceTypeId
               AND ResourceSurrogateId = @previousResourceSurrogateId;
        DELETE dbo.TokenQuantityCompositeSearchParam
        WHERE  ResourceTypeId = @resourceTypeId
               AND ResourceSurrogateId = @previousResourceSurrogateId;
        DELETE dbo.TokenStringCompositeSearchParam
        WHERE  ResourceTypeId = @resourceTypeId
               AND ResourceSurrogateId = @previousResourceSurrogateId;
        DELETE dbo.TokenNumberNumberCompositeSearchParam
        WHERE  ResourceTypeId = @resourceTypeId
               AND ResourceSurrogateId = @previousResourceSurrogateId;
    END
SET @resourceSurrogateId = @baseResourceSurrogateId + ( NEXT VALUE FOR ResourceSurrogateIdUniquifierSequence);
INSERT INTO dbo.Resource (ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash)
SELECT @resourceTypeId,
       @resourceId,
       @version,
       0,
       @resourceSurrogateId,
       @isDeleted,
       @requestMethod,
       @rawResource,
       CASE WHEN @version = 1 THEN 1 ELSE 0 END,
       @searchParamHash;
INSERT INTO dbo.ResourceWriteClaim (ResourceSurrogateId, ClaimTypeId, ClaimValue)
SELECT @resourceSurrogateId,
       ClaimTypeId,
       ClaimValue
FROM   @resourceWriteClaims;
INSERT INTO dbo.CompartmentAssignment (ResourceTypeId, ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                CompartmentTypeId,
                ReferenceResourceId,
                0
FROM   @compartmentAssignments;
INSERT INTO dbo.ReferenceSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                BaseUri,
                ReferenceResourceTypeId,
                ReferenceResourceId,
                ReferenceResourceVersion,
                0
FROM   @referenceSearchParams;
INSERT INTO dbo.TokenSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SystemId,
                Code,
                CodeOverflow,
                0
FROM   @tokenSearchParams;
INSERT INTO dbo.TokenText (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                Text,
                0
FROM   @tokenTextSearchParams;
INSERT INTO dbo.StringSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsHistory, IsMin, IsMax)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                Text,
                TextOverflow,
                0,
                IsMin,
                IsMax
FROM   @stringSearchParams;
INSERT INTO dbo.UriSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                Uri,
                0
FROM   @uriSearchParams;
INSERT INTO dbo.NumberSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SingleValue,
                LowValue,
                HighValue,
                0
FROM   @numberSearchParams;
INSERT INTO dbo.QuantitySearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SystemId,
                QuantityCodeId,
                SingleValue,
                LowValue,
                HighValue,
                0
FROM   @quantitySearchParams;
INSERT INTO dbo.DateTimeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsHistory, IsMin, IsMax)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                StartDateTime,
                EndDateTime,
                IsLongerThanADay,
                0,
                IsMin,
                IsMax
FROM   @dateTimeSearchParms;
INSERT INTO dbo.ReferenceTokenCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                BaseUri1,
                ReferenceResourceTypeId1,
                ReferenceResourceId1,
                ReferenceResourceVersion1,
                SystemId2,
                Code2,
                CodeOverflow2,
                0
FROM   @referenceTokenCompositeSearchParams;
INSERT INTO dbo.TokenTokenCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SystemId1,
                Code1,
                CodeOverflow1,
                SystemId2,
                Code2,
                CodeOverflow2,
                0
FROM   @tokenTokenCompositeSearchParams;
INSERT INTO dbo.TokenDateTimeCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SystemId1,
                Code1,
                CodeOverflow1,
                StartDateTime2,
                EndDateTime2,
                IsLongerThanADay2,
                0
FROM   @tokenDateTimeCompositeSearchParams;
INSERT INTO dbo.TokenQuantityCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SystemId1,
                Code1,
                CodeOverflow1,
                SingleValue2,
                SystemId2,
                QuantityCodeId2,
                LowValue2,
                HighValue2,
                0
FROM   @tokenQuantityCompositeSearchParams;
INSERT INTO dbo.TokenStringCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SystemId1,
                Code1,
                CodeOverflow1,
                Text2,
                TextOverflow2,
                0
FROM   @tokenStringCompositeSearchParams;
INSERT INTO dbo.TokenNumberNumberCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
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
                HasRange,
                0
FROM   @tokenNumberNumberCompositeSearchParams;
SELECT @version;
IF @isResourceChangeCaptureEnabled = 1
    EXECUTE dbo.CaptureResourceChanges @isDeleted = @isDeleted, @version = @version, @resourceId = @resourceId, @resourceTypeId = @resourceTypeId;
IF @InitialTranCount = 0
    COMMIT TRANSACTION;

GO
CREATE PROCEDURE dbo.UpsertSearchParams
@searchParams dbo.SearchParamTableType_1 READONLY
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
