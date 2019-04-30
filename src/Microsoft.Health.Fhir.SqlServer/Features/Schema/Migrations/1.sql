-- Enable RCSI
ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON
GO

-- Drop existing types

DECLARE @sql nvarchar(max) =''

SELECT @sql = @sql + 'DROP PROCEDURE ' + name + '; '
FROM sys.procedures

SELECT @sql = @sql + 'DROP TABLE ' + name + '; '
FROM sys.tables

SELECT @sql = @sql + 'DROP TYPE ' + name + '; '
FROM sys.table_types

SELECT @sql = @sql + 'DROP SEQUENCE ' + name + '; '
FROM sys.sequences

EXEC(@sql)

GO

-- Schema bootstrap

CREATE TABLE SchemaVersion
(
    Version int PRIMARY KEY,
    Status varchar(10)
)

INSERT INTO SchemaVersion
VALUES
    (1, 'started')

GO

CREATE PROCEDURE SelectCurrentSchemaVersion
AS
BEGIN
    SELECT MAX(Version)
    FROM SchemaVersion
    WHERE Status = 'complete'
END
GO

CREATE PROCEDURE UpsertSchemaVersion(
    @version int,
    @status varchar(10)
)
AS
BEGIN
    IF EXISTS(SELECT *
    FROM SchemaVersion
    WHERE Version = @version)
    BEGIN
        UPDATE SchemaVersion
        SET Status = @status
        WHERE Version = @version
    END
    ELSE
    BEGIN
        INSERT INTO SchemaVersion
            (Version, Status)
        VALUES
            (@version, @status)
    END
END
GO


-- Create metadata tables

CREATE TABLE SearchParam
(
    SearchParamId smallint IDENTITY(1,1) NOT NULL,
    Id varchar(128) NOT NULL,
    Name varchar(128) NOT NULL
)

CREATE TABLE ResourceType
(
    ResourceTypeId smallint IDENTITY(1,1) NOT NULL,
    Name nvarchar(50) NOT NULL
)


-- Create System and QuantityCode tables

CREATE TABLE System
(
    SystemId int IDENTITY(1,1) NOT NULL,
    System nvarchar(256) NOT NULL,
)

CREATE UNIQUE CLUSTERED INDEX IXC_System ON System (
    System
)

CREATE TABLE QuantityCode
(
    QuantityCodeId int IDENTITY(1,1) NOT NULL,
    QuantityCode nvarchar(256) NOT NULL
)

CREATE UNIQUE CLUSTERED INDEX IXC_QuantityCode on QuantityCode
(
    QuantityCode
)

-- Resource Table

CREATE TABLE Resource
(
    ResourceTypeId smallint NOT NULL,
    ResourceId varchar(64) NOT NULL,
    Version int NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    LastUpdated datetime NULL,
    RawResource varbinary(max) NOT NULL
)

CREATE UNIQUE CLUSTERED INDEX IXC_Resource ON Resource (
    ResourceSurrogateId
)

CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_ResourceTypeId_ResourceId_Version ON Resource (
    ResourceTypeId, 
    ResourceId,
    Version
)

-- Table types for table-valued parameters

CREATE TYPE ResourceTableType AS TABLE (
    ResourceTypeId smallint NOT NULL,
    ResourceId varchar(64) NOT NULL,
    LocalId int NOT NULL,
    Etag varchar(128) NULL,
    AllowCreate bit NOT NULL,
    RawResource varbinary(max)
)

CREATE TYPE DateSearchParamTableType AS TABLE (
    ResourceTypeId smallint NOT NULL,
    LocalId int NOT NULL,
    SearchParamId smallint NOT NULL,
    StartTime datetime2(7) NOT NULL,
    EndTime datetime2(7) NOT NULL
)

CREATE TYPE NumberSearchParamTableType AS TABLE (
    ResourceTypeId smallint NOT NULL,
    LocalId int NOT NULL,
    SearchParamId smallint NOT NULL,
    Number decimal(18, 6) NULL
)

CREATE TYPE QuantitySearchParamTableType AS TABLE (
    ResourceTypeId smallint NOT NULL,
    LocalId int NOT NULL,
    SearchParamId smallint NOT NULL,
    System nvarchar(256) NULL,
    Code nvarchar(256) NULL,
    Quantity decimal(18, 6) NULL
)

CREATE TYPE ReferenceSearchParamTableType AS TABLE (
    ResourceTypeId smallint NOT NULL,
    LocalId int NOT NULL,
    SearchParamId smallint NOT NULL,
    BaseUri varchar(512) NULL,
    ReferenceResourceTypeId smallint NULL,
    ReferenceResourceId varchar(64) NOT NULL
)

CREATE TYPE StringSearchParamTableType AS TABLE (
    ResourceTypeId smallint NOT NULL,
    LocalId int NOT NULL,
    SearchParamId smallint NOT NULL,
    Value nvarchar(512) NOT NULL
)

CREATE TYPE TokenSearchParamTableType AS TABLE (
    ResourceTypeId smallint NOT NULL,
    LocalId int NOT NULL,
    SearchParamId smallint NOT NULL,
    System nvarchar(256) NULL,
    Code nvarchar(256) NULL
)

CREATE TYPE TokenTextSearchParamTableType AS TABLE (
    ResourceTypeId smallint NOT NULL,
    LocalId int NOT NULL,
    SearchParamId smallint NOT NULL,
    Text nvarchar(512) NULL
)

CREATE TYPE UriSearchParamTableType AS TABLE (
    ResourceTypeId smallint NOT NULL,
    LocalId int NOT NULL,
    SearchParamId smallint NOT NULL,
    Uri varchar(256) NOT NULL
)

