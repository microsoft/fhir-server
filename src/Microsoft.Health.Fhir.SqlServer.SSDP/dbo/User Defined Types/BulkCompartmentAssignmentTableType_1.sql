CREATE TYPE dbo.BulkCompartmentAssignmentTableType_1 AS TABLE
(
    Offset                      int                 NOT NULL,
    CompartmentTypeId           tinyint             NOT NULL,
    ReferenceResourceId         varchar(64)         COLLATE Latin1_General_100_CS_AS NOT NULL
)