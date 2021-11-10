CREATE TABLE dbo.CompartmentType
(
    CompartmentTypeId           tinyint IDENTITY(1,1)           NOT NULL,
    Name                        varchar(128)                    COLLATE Latin1_General_100_CS_AS NOT NULL
)

CREATE UNIQUE CLUSTERED INDEX IXC_CompartmentType on dbo.CompartmentType
(
    Name
)
