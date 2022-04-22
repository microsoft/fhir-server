CREATE TYPE [dbo].[BulkResourceWriteClaimTableType_1] AS TABLE (
    [Offset]      INT            NOT NULL,
    [ClaimTypeId] TINYINT        NOT NULL,
    [ClaimValue]  NVARCHAR (128) NOT NULL);

