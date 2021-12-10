CREATE TABLE dbo.CompartmentType
(
    CompartmentTypeId           tinyint IDENTITY(1,1)           NOT NULL,
    CONSTRAINT UQ_CompartmentType_CompartmentTypeId UNIQUE (CompartmentTypeId),
    Name                        varchar(128)                    COLLATE Latin1_General_100_CS_AS NOT NULL,
    CONSTRAINT PKC_CompartmentType PRIMARY KEY CLUSTERED (Name)
    WITH (DATA_COMPRESSION = PAGE)
)
