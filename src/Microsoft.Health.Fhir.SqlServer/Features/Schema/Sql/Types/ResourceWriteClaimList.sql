--DROP TYPE dbo.ResourceWriteClaimList
GO
CREATE TYPE dbo.ResourceWriteClaimList AS TABLE
(
    ResourceSurrogateId      bigint        NOT NULL
   ,ClaimTypeId              tinyint       NOT NULL
   ,ClaimValue               nvarchar(128) NOT NULL

   --In C# we use LowerInvariant() to mimic case insensitivity, but it does not deal with asterisk insensitivity. Cannot create constraint.
   --PRIMARY KEY (ResourceSurrogateId, ClaimTypeId, ClaimValue)
)
GO
