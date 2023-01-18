--DROP TYPE dbo.ResourceWriteClaimList
GO
CREATE TYPE dbo.ResourceWriteClaimList AS TABLE
(
    ResourceSurrogateId      bigint        NOT NULL
   ,ClaimTypeId              tinyint       NOT NULL
   ,ClaimValue               nvarchar(128) NOT NULL

   PRIMARY KEY (ResourceSurrogateId, ClaimTypeId, ClaimValue)
)
GO
