CREATE TABLE [dbo].[ResourceWriteClaim] (
    [ResourceSurrogateId] BIGINT         NOT NULL,
    [ClaimTypeId]         TINYINT        NOT NULL,
    [ClaimValue]          NVARCHAR (128) NOT NULL
);


GO
CREATE CLUSTERED INDEX [IXC_ResourceWriteClaim]
    ON [dbo].[ResourceWriteClaim]([ResourceSurrogateId] ASC, [ClaimTypeId] ASC) WITH (DATA_COMPRESSION = PAGE);

