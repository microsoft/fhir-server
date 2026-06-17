--DROP TYPE dbo.IncludeSpecificationList
GO
CREATE TYPE dbo.IncludeSpecificationList AS TABLE
(
    IncludeId                int          NOT NULL,  -- Unique identifier for this include in the list
    SourceResourceTypeId     smallint     NOT NULL,  -- The resource type that contains the reference (for include) or is referenced (for revinclude)
    SearchParamId            smallint     NULL,      -- The search parameter ID (NULL for wildcard includes)
    TargetResourceTypeId     smallint     NULL,      -- The target resource type (NULL means any type)
    IsReversed               bit          NOT NULL,  -- 0 = _include, 1 = _revinclude
    IsIterate                bit          NOT NULL,  -- 0 = normal, 1 = :iterate modifier
    IsWildCard               bit          NOT NULL,  -- 1 if this is a wildcard include (e.g., _include=*)

    PRIMARY KEY (IncludeId)
)
GO
