CREATE TABLE CompartmentAssignment
(
    ResourceTypeId              smallint            NOT NULL,
    ResourceSurrogateId         bigint              NOT NULL,
    CompartmentTypeId           bit(8)             NOT NULL,
    ReferenceResourceId         varchar(64)        NOT NULL,
    CONSTRAINT PKC_CompartmentAssignment PRIMARY KEY(ResourceTypeId, ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId),
    IsHistory                   bit                 NOT NULL
)PARTITION BY RANGE(ResourceTypeId);


CREATE INDEX IX_CompartmentAssignment_CompartmentTypeId_ReferenceResourceId
ON CompartmentAssignment
(
    ResourceTypeId,
    CompartmentTypeId,
    ReferenceResourceId,
    ResourceSurrogateId
)
WHERE IsHistory = 0 :: bit;