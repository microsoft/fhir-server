
CREATE TABLE dbo.ClaimType
(
    ClaimTypeId             tinyint IDENTITY(1,1)           NOT NULL,
    CONSTRAINT UQ_ClaimType_ClaimTypeId UNIQUE (ClaimTypeId),
    Name                    varchar(128)                    COLLATE Latin1_General_100_CS_AS NOT NULL,
    CONSTRAINT PKC_ClaimType PRIMARY KEY CLUSTERED (Name)
    WITH (DATA_COMPRESSION = PAGE)
)
