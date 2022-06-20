CREATE TYPE dbo.CompartmentAssignmentList AS TABLE
(
    ResourceTypeId              smallint            NOT NULL,
    ResourceSurrogateId         bigint              NOT NULL,
    CompartmentTypeId           tinyint             NOT NULL,
    ReferenceResourceId         varchar(64)         COLLATE Latin1_General_100_CS_AS NOT NULL,
    IsHistory                   bit                 NOT NULL
)
GO
