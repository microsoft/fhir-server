-- Enable RCSI
ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON
GO

-- Drop existing types

SET @sql =''

SELECT @sql = @sql + 'DROP PROCEDURE ' + [name] + '; '
FROM sys.procedures

SELECT @sql = @sql + 'DROP TABLE ' + [name] + '; '
FROM sys.tables

SELECT @sql = @sql + 'DROP TYPE ' + [name] + '; '
FROM sys.table_types

SELECT @sql = @sql + 'DROP SEQUENCE ' + [name] + '; '
FROM sys.sequences
exec(@sql)

GO

IF NOT EXISTS(SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SchemaVersion' AND TABLE_SCHEMA = 'dbo')
BEGIN
    CREATE TABLE dbo.SchemaVersion (
        [Version] int PRIMARY KEY, 
        [Status] varchar(10) 
    )
END
GO

INSERT INTO dbo.SchemaVersion
VALUES (1, 'started')
GO

CREATE PROCEDURE dbo.SelectCurrentSchemaVersion
AS BEGIN
    SELECT MAX([Version])
    FROM dbo.SchemaVersion 
    WHERE [Status] = 'complete'
END
GO

CREATE PROCEDURE dbo.UpsertSchemaVersion(
        @version int,
        @status varchar(10) 
    )
AS BEGIN
    IF EXISTS(SELECT * FROM dbo.SchemaVersion WHERE [Version] = @version)
    BEGIN
        UPDATE dbo.SchemaVersion
        SET [Status] = @status
        WHERE [Version] = @version
    END
    ELSE
    BEGIN
        INSERT INTO dbo.SchemaVersion ([Version], [Status])
        VALUES (@version, @status)
    END
END
GO


-- Create metadata tables

CREATE TABLE [dbo].[SearchParam]
(
    [SearchParamId] smallint IDENTITY(1,1) NOT NULL,
    [Id] varchar(128) NOT NULL,
    [Name] varchar(128) NOT NULL
)

CREATE TABLE [dbo].[ResourceType]
(
    [ResourceTypeId] smallint IDENTITY(1,1) NOT NULL,
    [Name] nvarchar(50) NOT NULL
)

-- Resource Table

CREATE TABLE [dbo].[Resource]
(
    [ResourceTypeId] [smallint] NOT NULL,
    [ResourceId] [varchar](64) NOT NULL,
    [Version] [int] NOT NULL,
    [ResourceSurrogateId] [bigint] NOT NULL,
    [LastUpdated] [datetime] NULL,
    [RawResource] [varbinary](max) NOT NULL
)

CREATE UNIQUE CLUSTERED INDEX [IXC_Resource] ON [dbo].[Resource] (
    ResourceSurrogateId
)

CREATE UNIQUE NONCLUSTERED INDEX [IX_Resource_ResourceTypeId_ResourceId_Version] ON [dbo].[Resource] (
    [ResourceTypeId], 
    [ResourceId],
    [Version]
)

-- System Table

CREATE TABLE [dbo].[System] (
    [SystemId] int IDENTITY(1,1) NOT NULL,
    [System] nvarchar(256) NOT NULL,
)

CREATE UNIQUE CLUSTERED INDEX [IXC_System] ON [dbo].[System] (
    [System]
)

-- Table types for table-valued parameters

CREATE TYPE [dbo].[ResourceTableType] AS TABLE (
    [ResourceTypeId] [smallint] NOT NULL,
    [ResourceId] [varchar](64) NOT NULL,
    [LocalId] [int] NOT NULL,
    [Etag] varchar(128) NULL,
    [AllowCreate] bit NOT NULL,
    [RawResource] [varbinary](max)
)

CREATE TYPE [dbo].[DateSearchParamTableType] AS TABLE (
    [ResourceTypeId] [smallint] NOT NULL,
    [LocalId] [int] NOT NULL,
    [SearchParamId] [smallint] NOT NULL,
    [StartTime] [datetime2](7) NOT NULL,
    [EndTime] [datetime2](7) NOT NULL
)

CREATE TYPE [dbo].[NumberSearchParamTableType] AS TABLE (
    [ResourceTypeId] [smallint] NOT NULL,
    [LocalId] [int] NOT NULL,
    [SearchParamId] [smallint] NOT NULL,
    [Number] [decimal](18, 6) NULL
)

CREATE TYPE [dbo].[QuantitySearchParamTableType] AS TABLE (
    [ResourceTypeId] [smallint] NOT NULL,
    [LocalId] [int] NOT NULL,
    [SearchParamId] [smallint] NOT NULL,
    [System] [nvarchar](256) NULL,
    [Code] [nvarchar](256) NULL,
    [Quantity] [decimal](18, 6) NULL
)

CREATE TYPE [dbo].[ReferenceSearchParamTableType] AS TABLE (
    [ResourceTypeId] [smallint] NOT NULL,
    [LocalId] [int] NOT NULL,
    [SearchParamId] [smallint] NOT NULL,
    [BaseUri] [varchar](512) NULL,
    [ReferenceResourceTypeId] [smallint] NULL,
    [ReferenceResourceId] [varchar](64) NOT NULL
)

CREATE TYPE [dbo].[StringSearchParamTableType] AS TABLE (
    [ResourceTypeId] [smallint] NOT NULL,
    [LocalId] [int] NOT NULL,
    [SearchParamId] [smallint] NOT NULL,
    [Value] [nvarchar](512) NOT NULL
)

CREATE TYPE [dbo].[TokenSearchParamTableType] AS TABLE (
    [ResourceTypeId] [smallint] NOT NULL,
    [LocalId] [int] NOT NULL,
    [SearchParamId] [smallint] NOT NULL,
    [System] [nvarchar](256) NULL,
    [Code] [nvarchar](256) NULL
)

CREATE TYPE [dbo].[TokenTextSearchParamTableType] AS TABLE (
    [ResourceTypeId] [smallint] NOT NULL,
    [LocalId] [int] NOT NULL,
    [SearchParamId] [smallint] NOT NULL,
    [Text] [nvarchar](512) NULL
)

CREATE TYPE [dbo].[UriSearchParamTableType] AS TABLE (
    [ResourceTypeId] [smallint] NOT NULL,
    [LocalId] [int] NOT NULL,
    [SearchParamId] [smallint] NOT NULL,
    [Uri] [varchar](256) NOT NULL
)

