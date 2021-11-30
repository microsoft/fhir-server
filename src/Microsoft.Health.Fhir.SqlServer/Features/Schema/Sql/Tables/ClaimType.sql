
CREATE TABLE dbo.ClaimType
(
    ClaimTypeId             tinyint IDENTITY(1,1)           NOT NULL,
    CONSTRAINT PK_ClaimType PRIMARY KEY NONCLUSTERED (ClaimTypeId),
    Name                    varchar(128)                    COLLATE Latin1_General_100_CS_AS NOT NULL
)

CREATE UNIQUE CLUSTERED INDEX IXC_Claim on dbo.ClaimType
(
    Name
)
