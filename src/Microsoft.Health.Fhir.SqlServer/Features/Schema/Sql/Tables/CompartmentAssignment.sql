CREATE TABLE dbo.CompartmentAssignment
(
    ResourceTypeId              smallint            NOT NULL,
    ResourceSurrogateId         bigint              NOT NULL,
    CompartmentTypeId           tinyint             NOT NULL,
    ReferenceResourceId         varchar(64)         COLLATE Latin1_General_100_CS_AS NOT NULL,
    IsHistory                   bit                 NOT NULL,
)

ALTER TABLE dbo.CompartmentAssignment SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_CompartmentAssignment
ON dbo.CompartmentAssignment
(
    ResourceTypeId,
    ResourceSurrogateId,
    CompartmentTypeId,
    ReferenceResourceId
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_CompartmentAssignment_CompartmentTypeId_ReferenceResourceId
ON dbo.CompartmentAssignment
(
    ResourceTypeId,
    CompartmentTypeId,
    ReferenceResourceId,
    ResourceSurrogateId
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)
